using System.ComponentModel.DataAnnotations;

namespace Garij.Application.DTOs;

public class PartDto
{
    public int Id { get; set; }

    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    [Display(Name = "Part Number")]
    public string PartNumber { get; set; } = string.Empty;

    [Range(0.01, (double)decimal.MaxValue)]
    [Display(Name = "Unit Price")]
    public decimal UnitPrice { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "Quantity in stock cannot be negative.")]
    [Display(Name = "Quantity In Stock")]
    public int QuantityInStock { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "Reorder level cannot be negative.")]
    [Display(Name = "Reorder Level")]
    public int ReorderLevel { get; set; }
}
