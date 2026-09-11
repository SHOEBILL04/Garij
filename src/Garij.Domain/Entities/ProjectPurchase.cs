using Garij.Domain.Enums;

namespace Garij.Domain.Entities;

/// <summary>
/// Represents a one-time software purchase and lifetime license grant for Garij.
/// </summary>
public class ProjectPurchase
{
    public int Id { get; set; }

    /// <summary>
    /// Unique cryptographically generated license key (e.g., GRJ-LIC-XXXX-XXXX-XXXX).
    /// </summary>
    public string LicenseKey { get; set; } = string.Empty;

    /// <summary>
    /// Optional foreign key to AspNetUsers.Id if the buyer has an authenticated identity account.
    /// </summary>
    public string? IdentityUserId { get; set; }

    public string BuyerName { get; set; } = string.Empty;

    public string BuyerEmail { get; set; } = string.Empty;

    public string? WorkshopName { get; set; }
 
    /// <summary>
    /// Garage identifier associated with this license grant.
    /// </summary>
    public string? GarageId { get; set; }

    public decimal Amount { get; set; }

    public string Currency { get; set; } = "USD";

    public string PaymentMethod { get; set; } = "CreditCard";

    public string TransactionReference { get; set; } = string.Empty;

    public DateTime PurchasedAt { get; set; } = DateTime.UtcNow;

    public LicenseStatus Status { get; set; } = LicenseStatus.Active;

    public bool IsActive { get; set; } = true;

    public string? Notes { get; set; }
}
