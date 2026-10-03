using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace DTIOneLink.Services.Email
{
    public record EmailMessage(string ToEmail, string ToName, string Subject, string TextBody, string HtmlBody);

    public enum EmailSendResult
    {
        Sent,
        // Worth trying again later (network error, Brevo busy or down).
        TemporaryFailure,
        // Brevo refused this message (e.g. invalid address); retrying won't help.
        Rejected,
        // The API key is missing, wrong, expired, or this server's IP is not
        // authorised in Brevo. Nothing will send until someone fixes it.
        NotConfigured
    }

    public interface IEmailSender
    {
        Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default);

        // Same as SendAsync, plus a plain description of what happened
        // (Brevo's own error text on failure). Used by the SuperAdmin
        // "Send test email" check in User Management.
        Task<(EmailSendResult Result, string Detail)> SendWithDetailAsync(EmailMessage message, CancellationToken cancellationToken = default);
    }

    // Settings section "Brevo". The API key is never stored in the project:
    // locally it comes from `dotnet user-secrets`, in production from the
    // BREVO_API_KEY GitHub secret written by the deploy workflow.
    public class BrevoOptions
    {
        public string? ApiKey { get; set; }
        public string? SenderEmail { get; set; }
        public string SenderName { get; set; } = "DTI OneLink";

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(SenderEmail);
    }

    // Sends through Brevo's transactional email API:
    // POST https://api.brevo.com/v3/smtp/email (201 Created on success).
    public class BrevoEmailSender : IEmailSender
    {
        private readonly HttpClient _http;
        private readonly BrevoOptions _options;
        private readonly ILogger<BrevoEmailSender> _logger;

        public BrevoEmailSender(HttpClient http, IOptions<BrevoOptions> options, ILogger<BrevoEmailSender> logger)
        {
            _http = http;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default) =>
            (await SendWithDetailAsync(message, cancellationToken)).Result;

        public async Task<(EmailSendResult Result, string Detail)> SendWithDetailAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            object recipient = string.IsNullOrWhiteSpace(message.ToName)
                ? new { email = message.ToEmail }
                : new { email = message.ToEmail, name = message.ToName };

            var body = new
            {
                sender = new { name = _options.SenderName, email = _options.SenderEmail },
                to = new[] { recipient },
                subject = message.Subject,
                textContent = message.TextBody,
                htmlContent = message.HtmlBody
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email")
            {
                Content = JsonContent.Create(body)
            };
            request.Headers.Add("api-key", _options.ApiKey);
            request.Headers.Add("accept", "application/json");

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Could not reach Brevo to send \"{Subject}\".", message.Subject);
                return (EmailSendResult.TemporaryFailure, "The server could not reach Brevo: " + ex.Message);
            }

            using (response)
            {
                if (response.IsSuccessStatusCode)
                {
                    return (EmailSendResult.Sent, $"Brevo accepted the email ({(int)response.StatusCode}).");
                }

                // Brevo's error body describes the problem; it never contains the key.
                var detail = await response.Content.ReadAsStringAsync(cancellationToken);
                if (detail.Length > 300) detail = detail[..300];

                var said = $"Brevo answered {(int)response.StatusCode}: {detail}";
                switch (response.StatusCode)
                {
                    case HttpStatusCode.Unauthorized:
                    case HttpStatusCode.Forbidden:
                        _logger.LogError("Brevo refused the API key ({Status}): {Detail}", (int)response.StatusCode, detail);
                        return (EmailSendResult.NotConfigured, said);
                    case HttpStatusCode.TooManyRequests:
                        _logger.LogWarning("Brevo rate limit reached: {Detail}", detail);
                        return (EmailSendResult.TemporaryFailure, said);
                    case >= HttpStatusCode.InternalServerError:
                        _logger.LogWarning("Brevo error {Status}: {Detail}", (int)response.StatusCode, detail);
                        return (EmailSendResult.TemporaryFailure, said);
                    default:
                        _logger.LogError("Brevo rejected \"{Subject}\" ({Status}): {Detail}", message.Subject, (int)response.StatusCode, detail);
                        return (EmailSendResult.Rejected, said);
                }
            }
        }
    }

    // Used when no Brevo key is configured. In Development it prints the
    // email (including any code) to the console so the whole flow can be
    // tested without Brevo; in Production it prints nothing sensitive.
    public class UnconfiguredEmailSender : IEmailSender
    {
        private readonly IHostEnvironment _environment;
        private readonly ILogger<UnconfiguredEmailSender> _logger;

        public UnconfiguredEmailSender(IHostEnvironment environment, ILogger<UnconfiguredEmailSender> logger)
        {
            _environment = environment;
            _logger = logger;
        }

        public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            if (_environment.IsDevelopment())
            {
                _logger.LogWarning(
                    "Brevo is not configured, so this email was NOT sent. Development copy:\nTo: {To}\nSubject: {Subject}\n{Body}",
                    message.ToEmail, message.Subject, message.TextBody);
                return Task.FromResult(EmailSendResult.Sent);
            }

            _logger.LogError("Brevo is not configured (Brevo:ApiKey / Brevo:SenderEmail). Email \"{Subject}\" was not sent.", message.Subject);
            return Task.FromResult(EmailSendResult.NotConfigured);
        }

        public async Task<(EmailSendResult Result, string Detail)> SendWithDetailAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            var result = await SendAsync(message, cancellationToken);
            return (result, result == EmailSendResult.Sent
                ? "Development: the email was printed to the console instead of being sent."
                : "The server has no Brevo API key or sender email set (Brevo:ApiKey / Brevo:SenderEmail), so nothing is sent.");
        }
    }
}
