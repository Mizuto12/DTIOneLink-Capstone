using System.ComponentModel.DataAnnotations;
using DTIOneLink.Security;

namespace DTIOneLink.Models
{
    // Change Password from the profile menu: the user is signed in, so the
    // current password proves it is really them at the keyboard.
    public class ChangePasswordViewModel
    {
        [Required(ErrorMessage = "Current password is required.")]
        [DataType(DataType.Password)]
        [Display(Name = "Current Password")]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "New password is required.")]
        [StringLength(100, MinimumLength = AccountDefaults.MinPasswordLength,
            ErrorMessage = "New password must be at least {2} characters.")]
        [DataType(DataType.Password)]
        [Display(Name = "New Password")]
        public string NewPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please confirm your new password.")]
        [Compare(nameof(NewPassword), ErrorMessage = "Passwords do not match.")]
        [DataType(DataType.Password)]
        [Display(Name = "Confirm New Password")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    // Setting a new password after an emailed code (first sign-in, or
    // Forgot Password). The code already proved who they are, so there is
    // no current-password field.
    public class SetPasswordViewModel
    {
        [Required(ErrorMessage = "New password is required.")]
        [StringLength(100, MinimumLength = AccountDefaults.MinPasswordLength,
            ErrorMessage = "New password must be at least {2} characters.")]
        [DataType(DataType.Password)]
        [Display(Name = "New Password")]
        public string NewPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please confirm your new password.")]
        [Compare(nameof(NewPassword), ErrorMessage = "Passwords do not match.")]
        [DataType(DataType.Password)]
        [Display(Name = "Confirm New Password")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public class ForgotPasswordViewModel
    {
        [Required(ErrorMessage = "Please enter your email.")]
        [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
        [StringLength(256)]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;
    }

    public class EnterCodeViewModel
    {
        [Required(ErrorMessage = "Please enter the 6-digit code.")]
        [Display(Name = "6-digit code")]
        public string Code { get; set; } = string.Empty;
    }
}
