namespace Garij.Application.DTOs;

using System.ComponentModel.DataAnnotations;

public class CustomerDto
{
    public int Id { get; set; }

    [Required]
    [StringLength(200)]
    [Display(Name = "Full name")]
    public string FullName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [Phone]
    [StringLength(20)]
    [Display(Name = "Phone number")]
    public string PhoneNumber { get; set; } = string.Empty;

    [StringLength(500)]
    public string Address { get; set; } = string.Empty;
}
