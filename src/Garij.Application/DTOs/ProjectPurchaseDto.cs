using Garij.Domain.Enums;

namespace Garij.Application.DTOs;

public class ProjectPurchaseDto
{
    public int Id { get; set; }

    public string LicenseKey { get; set; } = string.Empty;

    public string? IdentityUserId { get; set; }

    public string BuyerName { get; set; } = string.Empty;

    public string BuyerEmail { get; set; } = string.Empty;

    public string? WorkshopName { get; set; }
 
    public string? GarageId { get; set; }

    public decimal Amount { get; set; }

    public string Currency { get; set; } = "USD";

    public string PaymentMethod { get; set; } = string.Empty;

    public string TransactionReference { get; set; } = string.Empty;

    public DateTime PurchasedAt { get; set; }

    public LicenseStatus Status { get; set; }

    public bool IsActive { get; set; }

    public string? Notes { get; set; }
}
