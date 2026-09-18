using Garij.Domain.Enums;

namespace Garij.Domain.Entities;

public class PaymentTransaction
{
    public int Id { get; set; }

    public int InvoiceId { get; set; }

    public Invoice Invoice { get; set; } = null!;

    public decimal Amount { get; set; }

    public PaymentMethod PaymentMethod { get; set; }

    public string TransactionReference { get; set; } = string.Empty;

    public DateTime PaidAt { get; set; }

    /// <summary>
    /// When the payment was refunded in full; null while it still stands. A refunded payment is
    /// kept rather than deleted, so the payment history still shows it, but it no longer counts
    /// towards the invoice's paid amount or towards collected revenue.
    /// </summary>
    public DateTime? RefundedAt { get; set; }

    public bool IsRefunded => RefundedAt.HasValue;
}
