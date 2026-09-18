using Garij.Application.DTOs;

namespace Garij.Application.Interfaces;

public interface IBillingService
{
    Task<InvoiceDto> GenerateInvoiceAsync(int serviceJobId);

    Task<InvoiceDto?> GetInvoiceByIdAsync(int id);

    Task<InvoiceDto?> GetInvoiceByServiceJobAsync(int serviceJobId);

    Task<IEnumerable<InvoiceDto>> GetAllInvoicesAsync();

    Task<PaymentTransactionDto> RecordPaymentAsync(PaymentTransactionDto payment);

    /// <summary>
    /// Refunds one recorded payment on the invoice in full. The payment stays in the history marked
    /// as refunded, stops counting towards the paid amount (reopening the outstanding balance), and
    /// is excluded from collected revenue.
    /// </summary>
    Task<PaymentTransactionDto> RefundPaymentAsync(int invoiceId, int paymentId);

    Task<IEnumerable<PaymentTransactionDto>> GetPaymentsByInvoiceAsync(int invoiceId);
}
