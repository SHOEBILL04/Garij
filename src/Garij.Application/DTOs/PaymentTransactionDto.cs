using System.ComponentModel.DataAnnotations;
using Garij.Domain.Enums;

namespace Garij.Application.DTOs;

public class PaymentTransactionDto
{
    public int Id { get; set; }

    public int InvoiceId { get; set; }

    public decimal Amount { get; set; }

    public PaymentMethod PaymentMethod { get; set; }

    // Mirrors PaymentTransactionConfiguration's HasMaxLength(100) on the column.
    [StringLength(100, ErrorMessage = "Transaction reference cannot exceed 100 characters.")]
    [Display(Name = "Transaction reference")]
    public string TransactionReference { get; set; } = string.Empty;

    public DateTime PaidAt { get; set; }

    public DateTime? RefundedAt { get; set; }

    public bool IsRefunded => RefundedAt.HasValue;
}
