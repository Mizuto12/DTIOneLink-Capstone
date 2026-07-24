namespace DTIOneLink.Services.Email
{
    /// <summary>
    /// Development email sender — writes the message (and OTP) to the application log
    /// instead of contacting a real mail service. Swap this registration for a
    /// Microsoft Graph / Outlook implementation once credentials are configured.
    /// </summary>
    public class DevEmailSender : IEmailSender
    {
        private readonly ILogger<DevEmailSender> _logger;

        public DevEmailSender(ILogger<DevEmailSender> logger)
        {
            _logger = logger;
        }

        public Task SendAsync(string toEmail, string subject, string body)
        {
            _logger.LogWarning(
                "[DEV EMAIL] To: {ToEmail} | Subject: {Subject}\n{Body}",
                toEmail, subject, body);
            return Task.CompletedTask;
        }

        public Task SendOtpAsync(string toEmail, string code)
        {
            _logger.LogWarning(
                "================ DEV OTP ================\n" +
                " To:   {ToEmail}\n" +
                " Code: {Code}\n" +
                "========================================",
                toEmail, code);
            return Task.CompletedTask;
        }
    }
}
