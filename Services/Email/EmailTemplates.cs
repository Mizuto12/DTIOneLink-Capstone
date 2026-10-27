using System.Text.Encodings.Web;
using DTIOneLink.Models;

namespace DTIOneLink.Services.Email
{
    // Plain, short emails: a greeting, one message, and what to do next.
    // Everything that comes from users (names, task titles, remarks) is
    // HTML-encoded before it goes into the HTML version.
    public static class EmailTemplates
    {
        private static readonly HtmlEncoder Html = HtmlEncoder.Default;

        public static EmailMessage Code(string toEmail, string toName, OneTimeCodePurpose purpose, string code, int minutesValid)
        {
            var (subject, reason, ignoreNote) = purpose switch
            {
                OneTimeCodePurpose.PasswordReset =>
                    ("Your DTI OneLink password reset code",
                     "Use this code to reset your DTI OneLink password.",
                     "If you did not ask to reset your password, ignore this email. Your password has not been changed."),
                OneTimeCodePurpose.Login =>
                    ("Your DTI OneLink sign-in code",
                     "Use this code to finish signing in to DTI OneLink.",
                     "If you did not try to sign in to DTI OneLink, ignore this email and tell your administrator — someone else entered your password."),
                _ =>
                    ("Your DTI OneLink verification code",
                     "Use this code to confirm your email address in DTI OneLink.",
                     "If you did not try to sign in to DTI OneLink, ignore this email and tell your administrator.")
            };

            var text =
                $"Hello {toName},\n\n{reason}\n\nYour code: {code}\n\n" +
                $"The code expires in {minutesValid} minutes and can only be used once. Never share it with anyone, including DTI staff.\n\n" +
                $"{ignoreNote}\n\n— DTI OneLink, DTI Laguna Provincial Office";

            var html = Wrap(
                $"<p>Hello {Html.Encode(toName)},</p>" +
                $"<p>{Html.Encode(reason)}</p>" +
                $"<p style=\"font-size:32px;font-weight:700;letter-spacing:8px;color:#1c3f94;margin:24px 0;\">{code}</p>" +
                $"<p>The code expires in <strong>{minutesValid} minutes</strong> and can only be used once. Never share it with anyone, including DTI staff.</p>" +
                $"<p style=\"color:#5a6b8c;\">{Html.Encode(ignoreNote)}</p>");

            return new EmailMessage(toEmail, toName, subject, text, html);
        }

        public static EmailMessage ResetLink(string toEmail, string toName, string link, int minutesValid)
        {
            const string subject = "Reset your DTI OneLink password";

            var text =
                $"Hello {toName},\n\nUse this link to reset your DTI OneLink password:\n\n{link}\n\n" +
                $"The link expires in {minutesValid} minutes and can only be used once. Never share it with anyone, including DTI staff.\n\n" +
                "If you did not ask to reset your password, ignore this email. Your password has not been changed.\n\n" +
                "— DTI OneLink, DTI Laguna Provincial Office";

            var html = Wrap(
                $"<p>Hello {Html.Encode(toName)},</p>" +
                "<p>Use the button below to reset your DTI OneLink password.</p>" +
                $"<p><a href=\"{Html.Encode(link)}\" style=\"display:inline-block;background:#1c3f94;color:#ffffff;padding:10px 20px;border-radius:20px;text-decoration:none;\">Reset Password</a></p>" +
                $"<p style=\"color:#5a6b8c;font-size:13px;\">Or paste this link into your browser:<br>{Html.Encode(link)}</p>" +
                $"<p>The link expires in <strong>{minutesValid} minutes</strong> and can only be used once. Never share it with anyone, including DTI staff.</p>" +
                "<p style=\"color:#5a6b8c;\">If you did not ask to reset your password, ignore this email. Your password has not been changed.</p>");

            return new EmailMessage(toEmail, toName, subject, text, html);
        }

        public static EmailMessage ActivationLink(string toEmail, string toName, string link, int hoursValid)
        {
            const string subject = "Activate your DTI OneLink account";

            var text =
                $"Hello {toName},\n\nAn account was created for you in DTI OneLink. Use this link to activate it and choose your own password:\n\n{link}\n\n" +
                $"The link expires in {hoursValid} hours and can only be used once. Never share it with anyone, including DTI staff.\n\n" +
                "If you were not expecting this, contact your administrator.\n\n" +
                "— DTI OneLink, DTI Laguna Provincial Office";

            var html = Wrap(
                $"<p>Hello {Html.Encode(toName)},</p>" +
                "<p>An account was created for you in DTI OneLink. Use the button below to activate it and choose your own password.</p>" +
                $"<p><a href=\"{Html.Encode(link)}\" style=\"display:inline-block;background:#1c3f94;color:#ffffff;padding:10px 20px;border-radius:20px;text-decoration:none;\">Activate Account</a></p>" +
                $"<p style=\"color:#5a6b8c;font-size:13px;\">Or paste this link into your browser:<br>{Html.Encode(link)}</p>" +
                $"<p>The link expires in <strong>{hoursValid} hours</strong> and can only be used once. Never share it with anyone, including DTI staff.</p>" +
                "<p style=\"color:#5a6b8c;\">If you were not expecting this, contact your administrator.</p>");

            return new EmailMessage(toEmail, toName, subject, text, html);
        }

        public static EmailMessage PasswordChanged(string toEmail, string toName, DateTime changedAtLocal)
        {
            const string subject = "Your DTI OneLink password was changed";
            var when = changedAtLocal.ToString("MMMM d, yyyy 'at' h:mm tt");

            var text =
                $"Hello {toName},\n\nThe password for your DTI OneLink account was changed on {when}.\n\n" +
                "If you made this change, you don't need to do anything.\n" +
                "If you did NOT make this change, contact your administrator right away.\n\n" +
                "— DTI OneLink, DTI Laguna Provincial Office";

            var html = Wrap(
                $"<p>Hello {Html.Encode(toName)},</p>" +
                $"<p>The password for your DTI OneLink account was changed on <strong>{Html.Encode(when)}</strong>.</p>" +
                "<p>If you made this change, you don't need to do anything.</p>" +
                "<p><strong>If you did not make this change, contact your administrator right away.</strong></p>");

            return new EmailMessage(toEmail, toName, subject, text, html);
        }

        // An in-app notification, also sent by email. The link is relative
        // ("/Employee/Details/5"); it becomes a full link only when App:BaseUrl
        // is set.
        public static EmailMessage Notification(string toEmail, string toName, string subject, string message, string? link, string? baseUrl)
        {
            var url = !string.IsNullOrWhiteSpace(link) && !string.IsNullOrWhiteSpace(baseUrl) && link.StartsWith('/')
                ? baseUrl.TrimEnd('/') + link
                : null;

            var text =
                $"Hello {toName},\n\n{message}\n\n" +
                (url != null ? $"Open it in DTI OneLink: {url}\n\n" : "Log in to DTI OneLink to view it.\n\n") +
                "— DTI OneLink, DTI Laguna Provincial Office\n(You get this email because the same notice was posted in DTI OneLink.)";

            var html = Wrap(
                $"<p>Hello {Html.Encode(toName)},</p>" +
                $"<p>{Html.Encode(message)}</p>" +
                (url != null
                    ? $"<p><a href=\"{Html.Encode(url)}\" style=\"display:inline-block;background:#1c3f94;color:#ffffff;padding:10px 20px;border-radius:20px;text-decoration:none;\">Open in DTI OneLink</a></p>"
                    : "<p>Log in to DTI OneLink to view it.</p>") +
                "<p style=\"color:#5a6b8c;font-size:12px;\">You get this email because the same notice was posted in DTI OneLink.</p>");

            return new EmailMessage(toEmail, toName, subject, text, html);
        }

        private static string Wrap(string inner) =>
            "<div style=\"font-family:Segoe UI,Arial,sans-serif;font-size:15px;color:#1d2733;max-width:520px;\">" +
            "<p style=\"font-size:18px;font-weight:700;color:#1c3f94;margin:0 0 16px;\">DTI OneLink</p>" +
            inner +
            "<p style=\"color:#5a6b8c;font-size:12px;margin-top:24px;\">DTI Laguna Provincial Office</p></div>";
    }
}
