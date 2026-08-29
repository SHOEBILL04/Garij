namespace Garij.Application.DTOs;

using System.ComponentModel.DataAnnotations;

public class VehicleDto
{
    public int Id { get; set; }

    [Required]
    [Range(1, int.MaxValue, ErrorMessage = "Select a customer.")]
    [Display(Name = "Customer")]
    public int CustomerId { get; set; }

    public string CustomerName { get; set; } = string.Empty;

    [Required]
    [StringLength(20)]
    [RegularExpression(@"^[A-Z]{2,3}-\d{3,4}$", ErrorMessage = "Use a plate format like DHA-1234 or BR-002.")]
    [Display(Name = "License plate")]
    public string LicensePlateNumber { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string Make { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string Model { get; set; } = string.Empty;

    [Range(1900, 2100)]
    public int Year { get; set; }

    [StringLength(50)]
    [Display(Name = "VIN")]
    public string Vin { get; set; } = string.Empty;

    [StringLength(50)]
    public string Color { get; set; } = string.Empty;
}
