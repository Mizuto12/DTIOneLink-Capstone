using System.ComponentModel.DataAnnotations;

namespace DTIOneLink.Models
{
    public class OtpViewModel
    {
        [Required(ErrorMessage = "Enter the 6-digit code sent to your email.")]
        [RegularExpression(@"^\d{6}$", ErrorMessage = "The code must be 6 digits.")]
        [Display(Name = "Verification Code")]
        public string Code { get; set; } = string.Empty;

        // Masked email shown on the page (e.g. j***@dti.gov.ph).
        public string MaskedEmail { get; set; } = string.Empty;
    }
}
