namespace DTIOneLink.Models
{
    // The page a password-reset or activation link's GET request lands on.
    // It only ever shows a "click to continue" confirmation — the token
    // itself is checked but not consumed yet (OneTimeCodeService.PeekLinkAsync)
    // — so an email security gateway that pre-fetches the link to scan it
    // can't burn it before the person sees this page. Only submitting the
    // form here actually consumes the token.
    public class ConfirmLinkViewModel
    {
        public int Uid { get; set; }
        public string Token { get; set; } = string.Empty;
        public bool IsActivation { get; set; }
    }
}
