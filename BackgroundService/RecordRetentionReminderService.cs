namespace DTIOneLink.Services
{
    // Hourly background run of RecordRetentionReminder for every record owner,
    // so owners who aren't signed in still get their reminder waiting for
    // them. Signed-in users also get theirs checked whenever their
    // notification bell loads (see NotificationsController.List).
    public class RecordRetentionReminderService : BackgroundService
    {
        private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<RecordRetentionReminderService> _logger;

        public RecordRetentionReminderService(
            IServiceScopeFactory scopeFactory,
            ILogger<RecordRetentionReminderService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Scoped dependencies (NotificationService/AppDbContext) need
                    // their own scope per run — this service is a singleton.
                    using var scope = _scopeFactory.CreateScope();
                    var reminder = scope.ServiceProvider.GetRequiredService<RecordRetentionReminder>();
                    await reminder.SendDueRemindersAsync(ownerUserId: null, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "RecordRetentionReminderService: retention check failed");
                }

                await Task.Delay(CheckInterval, stoppingToken);
            }
        }
    }
}
