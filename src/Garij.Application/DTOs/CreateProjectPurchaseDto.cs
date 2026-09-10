using System.ComponentModel.DataAnnotations;

namespace Garij.Application.DTOs;

public class CreateProjectPurchaseDto
{
    [Required(ErrorMessage = "Full Name is required.")]
    [StringLength(100, ErrorMessage = "Full Name cannot exceed 100 characters.")]
    public string BuyerName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Invalid email address format.")]
    [StringLength(256)]
    public string BuyerEmail { get; set; } = string.Empty;

    [StringLength(150, ErrorMessage = "Workshop name cannot exceed 150 characters.")]
    public string? WorkshopName { get; set; }

    [DataType(DataType.Password)]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be at least 6 characters long.")]
    public string? Password { get; set; }

    [Required(ErrorMessage = "Please select a payment method.")]
    public string PaymentMethod { get; set; } = "CreditCard";

    public string? CardholderName { get; set; }

    public string? CardNumber { get; set; }

    public string? ExpiryDate { get; set; }

    public string? Cvv { get; set; }

    public bool IsTestPayment { get; set; } = false;

    public string? ExistingLicenseKey { get; set; }

    /// <summary>
    /// The initial role assigned to the buyer's account (Admin, FrontDesk, Mechanic). Defaults to Admin.
    /// </summary>
    public string AccountRole { get; set; } = "Admin";
}
