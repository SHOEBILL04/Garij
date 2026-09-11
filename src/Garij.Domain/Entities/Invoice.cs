using Garij.Domain.Enums;

namespace Garij.Domain.Entities;

public class Invoice
{
    public int Id { get; set; }

    /// <summary>1:1 with ServiceJob.</summary>
    public int ServiceJobId { get; set; }

    public ServiceJob ServiceJob { get; set; } = null!;

    public string InvoiceNumber { get; set; } = string.Empty;

    public decimal SubTotal { get; set; }

    public decimal TaxAmount { get; set; }

    public decimal TotalAmount { get; set; }

    public PaymentStatus PaymentStatus { get; set; }

    public DateTime IssuedAt { get; set; }

    public string? GarageId { get; set; }

    public ICollection<PaymentTransaction> PaymentTransactions { get; set; } = new List<PaymentTransaction>();
}
