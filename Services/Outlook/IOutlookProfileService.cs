namespace DTIOneLink.Services.Outlook
{
    /// <summary>
    /// Profile data pulled from a user's Outlook / Microsoft 365 account.
    /// </summary>
    public record OutlookProfile(string? DisplayName, string? Department, string? JobTitle);

    /// <summary>
    /// Syncs profile details from Outlook via Microsoft Graph. The current
    /// implementation is a no-op placeholder; a Graph-backed implementation
    /// is dropped in once Azure AD credentials are configured.
    /// </summary>
    public interface IOutlookProfileService
    {
        Task<OutlookProfile?> GetProfileAsync(string email);
    }
}
