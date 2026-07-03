using System.ComponentModel.DataAnnotations;

namespace DTIOneLink.Models
{
    /// <summary>
    /// Bound model for the DTI Laguna Provincial Office login form.
    /// </summary>
    public class LoginViewModel
    {
        [Required(ErrorMessage = "Username is required.")]
        [Display(Name = "Username")]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password is required.")]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; }

        public string? ReturnUrl { get; set; }
    }
}
