using DTIOneLink.Models;
using DTIOneLink.Services.Email;

namespace DTIOneLink.Tests;

// EmailTemplates is pure string formatting (no network/SMTP), so it's a good
// unit test target even though EmailSender/EmailDispatchService are not.
public class EmailTemplatesTests
{
    [Fact]
    public void Code_PasswordReset_UsesResetSpecificSubjectAndIgnoreNote()
    {
        var email = EmailTemplates.Code("user@dti.gov.ph", "Juan Dela Cruz", OneTimeCodePurpose.PasswordReset, "123456", 10);

        Assert.Equal("user@dti.gov.ph", email.ToEmail);
        Assert.Equal("Your DTI OneLink password reset code", email.Subject);
        Assert.Contains("123456", email.TextBody);
        Assert.Contains("123456", email.HtmlBody);
        Assert.Contains("Your password has not been changed.", email.TextBody);
        Assert.Contains("10 minutes", email.TextBody);
    }

    [Fact]
    public void Code_ConfirmEmail_UsesSignInSpecificSubjectAndIgnoreNote()
    {
        var email = EmailTemplates.Code("user@dti.gov.ph", "Juan Dela Cruz", OneTimeCodePurpose.ConfirmEmail, "654321", 10);

        Assert.Equal("Your DTI OneLink verification code", email.Subject);
        Assert.Contains("tell your administrator", email.TextBody);
    }

    [Fact]
    public void Code_HtmlEncodesUserSuppliedName()
    {
        // FullName comes from user data (e.g. User Management) and must not
        // be able to inject markup into the HTML email body.
        var email = EmailTemplates.Code("user@dti.gov.ph", "<script>alert(1)</script>", OneTimeCodePurpose.ConfirmEmail, "111111", 10);

        Assert.DoesNotContain("<script>", email.HtmlBody);
        Assert.Contains("&lt;script&gt;", email.HtmlBody);
    }

    [Fact]
    public void Notification_WithLinkAndBaseUrl_BuildsAbsoluteClickableLink()
    {
        var email = EmailTemplates.Notification(
            "user@dti.gov.ph", "Juan", "You have a new task", "You've been assigned: \"Report\"",
            "/Employee/Details/5", "https://onelink.dti.gov.ph/");

        Assert.Contains("https://onelink.dti.gov.ph/Employee/Details/5", email.TextBody);
        Assert.Contains("https://onelink.dti.gov.ph/Employee/Details/5", email.HtmlBody);
    }

    [Fact]
    public void Notification_WithoutBaseUrl_FallsBackToLoginPrompt()
    {
        // App:BaseUrl isn't configured in every environment — the email must
        // still be sendable without a clickable link.
        var email = EmailTemplates.Notification(
            "user@dti.gov.ph", "Juan", "Subject", "message text",
            "/Employee/Details/5", baseUrl: null);

        Assert.DoesNotContain("Employee/Details/5", email.TextBody);
        Assert.Contains("Log in to DTI OneLink to view it.", email.TextBody);
    }

    [Fact]
    public void Notification_RelativeLinkMustStartWithSlash_OtherwiseNoAbsoluteUrl()
    {
        var email = EmailTemplates.Notification(
            "user@dti.gov.ph", "Juan", "Subject", "message",
            "not-a-path", "https://onelink.dti.gov.ph");

        Assert.Contains("Log in to DTI OneLink to view it.", email.TextBody);
    }

    [Fact]
    public void Notification_HtmlEncodesUserSuppliedMessage()
    {
        var email = EmailTemplates.Notification(
            "user@dti.gov.ph", "Juan", "Subject", "<b>injected</b>", null, null);

        Assert.DoesNotContain("<b>injected</b>", email.HtmlBody);
        Assert.Contains("&lt;b&gt;injected&lt;/b&gt;", email.HtmlBody);
    }

    [Fact]
    public void PasswordChanged_IncludesFormattedDateAndWarning()
    {
        var changedAt = new DateTime(2026, 3, 14, 15, 30, 0);
        var email = EmailTemplates.PasswordChanged("user@dti.gov.ph", "Juan", changedAt);

        Assert.Equal("Your DTI OneLink password was changed", email.Subject);
        Assert.Contains("March 14, 2026", email.TextBody);
        Assert.Contains("contact your administrator right away", email.TextBody);
    }
}
