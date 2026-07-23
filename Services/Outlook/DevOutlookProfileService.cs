namespace DTIOneLink.Services.Outlook
{
    /// <summary>
    /// Placeholder profile service used until Microsoft Graph credentials are wired up.
    /// Returns no profile, so callers keep the details already stored in the database.
    /// </summary>
    public class DevOutlookProfileService : IOutlookProfileService
    {
        private readonly ILogger<DevOutlookProfileService> _logger;

        public DevOutlookProfileService(ILogger<DevOutlookProfileService> logger)
        {
            _logger = logger;
        }

        public Task<OutlookProfile?> GetProfileAsync(string email)
        {
            _logger.LogInformation(
                "Outlook profile sync skipped for {Email} (Microsoft Graph not configured).",
                email);
            return Task.FromResult<OutlookProfile?>(null);
        }
    }
}
