namespace DTIOneLink.Services.Email
{
    public interface IEmailSender
    {
        Task SendAsync(string toEmail, string subject, string body);

        Task SendOtpAsync(string toEmail, string code);
    }
}
