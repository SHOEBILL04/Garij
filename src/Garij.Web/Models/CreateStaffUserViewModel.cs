using System.ComponentModel.DataAnnotations;
using Garij.Domain.Enums;

namespace Garij.Web.Models;

public class CreateStaffUserViewModel
{
    [Required(ErrorMessage = "Full Name is required.")]
    [Display(Name = "Full Name")]
    [StringLength(100, ErrorMessage = "Full Name cannot exceed 100 characters.")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email address is required.")]
    [EmailAddress(ErrorMessage = "Invalid email format.")]
    [Display(Name = "Email Address")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Phone Number is required.")]
    [Phone(ErrorMessage = "Invalid phone number.")]
    [Display(Name = "Phone Number")]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Role is required.")]
    [Display(Name = "Account Role")]
    public UserRole Role { get; set; } = UserRole.FrontDesk;

    [Required(ErrorMessage = "Password is required.")]
    [StringLength(100, ErrorMessage = "The {0} must be at least {2} characters long.", MinimumLength = 6)]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    [Display(Name = "Confirm Password")]
    [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
