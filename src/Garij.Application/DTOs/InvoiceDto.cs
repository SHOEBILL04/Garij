using Garij.Domain.Enums;

namespace Garij.Application.DTOs;

public class InvoiceDto
{
    public int Id { get; set; }

    public int ServiceJobId { get; set; }

    public string InvoiceNumber { get; set; } = string.Empty;

    public decimal SubTotal { get; set; }

    public decimal TaxAmount { get; set; }

    public decimal TotalAmount { get; set; }

    public PaymentStatus PaymentStatus { get; set; }

    public DateTime IssuedAt { get; set; }

    public string BookingReference { get; set; } = string.Empty;

    public string CustomerName { get; set; } = string.Empty;

    public string VehicleDescription { get; set; } = string.Empty;

    public List<InvoiceLineItemDto> ServiceLines { get; set; } = new();

    public List<InvoiceLineItemDto> PartLines { get; set; } = new();

    public List<PaymentTransactionDto> Payments { get; set; } = new();

    public decimal AmountPaid { get; set; }

    public decimal OutstandingBalance { get; set; }
}
