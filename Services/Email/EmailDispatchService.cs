using System.Threading.Channels;
using DTIOneLink.Data;
using DTIOneLink.Models;
using Microsoft.EntityFrameworkCore;

namespace DTIOneLink.Services.Email
{
    // Verification-code emails. Kept in memory only (never in the database,
    // where the code would sit in plain text) and sent right away, never
    // behind notification emails. Queuing also means a page returns at the
    // same speed whether or not an email was sent, so response time can't
    // reveal which email addresses have accounts. If the app restarts before
    // sending, the code is lost — the user simply asks for a new one.
    public class OtpEmailQueue
    {
        private readonly Channel<EmailMessage> _channel =
            Channel.CreateBounded<EmailMessage>(new BoundedChannelOptions(200) { FullMode = BoundedChannelFullMode.DropWrite });

        public bool TryEnqueue(EmailMessage message) => _channel.Writer.TryWrite(message);

        public ChannelReader<EmailMessage> Reader => _channel.Reader;
    }

    // Sends code emails as soon as they are queued, and every few seconds
    // sends waiting notification emails from the EmailOutbox table.
    public class EmailDispatchService : BackgroundService
    {
        private static readonly TimeSpan OutboxPollInterval = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan[] RetryDelays =
        {
            TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromHours(1)
        };
        private const int MaxAttempts = 5;
        private static readonly TimeSpan KeepSentFor = TimeSpan.FromDays(30);

        // Starts the in-app alert so SuperAdmins can find it (and so it is
        // posted at most once a day).
        public const string EmailProblemAlertPrefix = "Emails from DTI OneLink are not being sent";

        private readonly OtpEmailQueue _otpQueue;
        private readonly IEmailSender _sender;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<EmailDispatchService> _logger;

        public EmailDispatchService(OtpEmailQueue otpQueue, IEmailSender sender,
            IServiceScopeFactory scopeFactory, ILogger<EmailDispatchService> logger)
        {
            _otpQueue = otpQueue;
            _sender = sender;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
            Task.WhenAll(SendCodesAsync(stoppingToken), SendOutboxAsync(stoppingToken));

        private async Task SendCodesAsync(CancellationToken stoppingToken)
        {
            try
            {
                await foreach (var message in _otpQueue.Reader.ReadAllAsync(stoppingToken))
                {
                    try
                    {
                        var result = await _sender.SendAsync(message, stoppingToken);
                        if (result == EmailSendResult.NotConfigured)
                        {
                            await AlertSuperAdminsAsync(stoppingToken);
                        }
                        else if (result != EmailSendResult.Sent)
                        {
                            // Not retried: the user can press "Resend code".
                            _logger.LogWarning("A verification code email could not be sent ({Result}).", result);
                        }
                    }
                    catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                    {
                        _logger.LogError(ex, "Sending a verification code email failed.");
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
        }

        private async Task SendOutboxAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(OutboxPollInterval);
            try
            {
                do
                {
                    try
                    {
                        await ProcessOutboxBatchAsync(stoppingToken);
                    }
                    catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                    {
                        _logger.LogError(ex, "Processing the email outbox failed.");
                    }
                }
                while (await timer.WaitForNextTickAsync(stoppingToken));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
        }

        private async Task ProcessOutboxBatchAsync(CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTime.UtcNow;

            var batch = await db.EmailOutbox
                .Where(m => m.SentAtUtc == null && m.Attempts < MaxAttempts && m.NextAttemptAtUtc <= now)
                .OrderBy(m => m.Id)
                .Take(20)
                .ToListAsync(stoppingToken);

            foreach (var item in batch)
            {
                var result = await _sender.SendAsync(
                    new EmailMessage(item.ToEmail, item.ToName, item.Subject, item.TextBody, item.HtmlBody), stoppingToken);

                if (result == EmailSendResult.Sent)
                {
                    item.SentAtUtc = DateTime.UtcNow;
                    item.LastError = null;
                }
                else if (result == EmailSendResult.NotConfigured)
                {
                    // The key is the problem, not this email: keep it waiting
                    // without using up its attempts, and stop this round.
                    item.NextAttemptAtUtc = DateTime.UtcNow.AddMinutes(15);
                    item.LastError = "Brevo is not configured or refused the API key.";
                    await db.SaveChangesAsync(stoppingToken);
                    await AlertSuperAdminsAsync(stoppingToken);
                    return;
                }
                else
                {
                    item.Attempts = result == EmailSendResult.Rejected ? MaxAttempts : item.Attempts + 1;
                    item.NextAttemptAtUtc = DateTime.UtcNow + RetryDelays[Math.Min(item.Attempts, RetryDelays.Length) - 1];
                    item.LastError = result == EmailSendResult.Rejected ? "Brevo rejected this email." : "Temporary sending failure.";
                }

                await db.SaveChangesAsync(stoppingToken);
            }

            // Housekeeping: sent emails, and ones that gave up, are kept 30 days.
            var cutoff = DateTime.UtcNow - KeepSentFor;
            await db.EmailOutbox
                .Where(m => m.CreatedAtUtc < cutoff && (m.SentAtUtc != null || m.Attempts >= MaxAttempts))
                .ExecuteDeleteAsync(stoppingToken);
        }

        // Posts an in-app notice to every active SuperAdmin, at most once a
        // day, so a dead API key doesn't go unnoticed in an office with no IT
        // staff. Admin password reset keeps working in the meantime.
        private async Task AlertSuperAdminsAsync(CancellationToken stoppingToken)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var since = DateTime.UtcNow.AddDays(-1);
                var alreadyAlerted = await db.Notifications.AnyAsync(n =>
                    n.Type == NotificationType.System &&
                    n.CreatedAt >= since &&
                    n.Message.StartsWith(EmailProblemAlertPrefix), stoppingToken);
                if (alreadyAlerted) return;

                var superAdminIds = await db.Users
                    .Where(u => u.IsActive && u.Role == "SuperAdmin")
                    .Select(u => u.Id)
                    .ToListAsync(stoppingToken);

                foreach (var id in superAdminIds)
                {
                    db.Notifications.Add(new Notification
                    {
                        RecipientUserId = id,
                        Type = NotificationType.System,
                        Message = EmailProblemAlertPrefix + ". Verification codes and email notices are not reaching staff. " +
                                  "The Brevo API key has likely expired or been removed: replace it (see the handover guide). " +
                                  "Until then, use Reset Password in User Management for anyone locked out.",
                        Link = "/UserManagement/Index"
                    });
                }
                await db.SaveChangesAsync(stoppingToken);
                _logger.LogError("Email sending is not configured or the Brevo key was refused; SuperAdmins were alerted.");
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Could not post the email problem alert.");
            }
        }
    }
}
