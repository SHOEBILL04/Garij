using Garij.Application.DTOs;

namespace Garij.Web.Models;

/// <summary>Backs the refund confirmation page: the invoice for context and the payment being refunded.</summary>
public class RefundPaymentViewModel
{
    public InvoiceDto Invoice { get; set; } = new();

    public PaymentTransactionDto Payment { get; set; } = new();
}
