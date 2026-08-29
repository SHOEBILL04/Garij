using Garij.Application.DTOs;
using Garij.Application.Interfaces;

namespace Garij.Application.Services;

public class BillingService : IBillingService
{
    /// <summary>
    /// Stage 1: structure only. Stage 2 will implement this to:
    /// - Compute SubTotal as the sum of JobServiceDetail (PriceAtBooking × Quantity)
    ///   plus JobPartUsed (PriceAtUsage × QuantityUsed) for the given service job.
    /// - Compute TaxAmount and TotalAmount from that SubTotal.
    /// - Generate a unique InvoiceNumber.
    /// - Wrap invoice creation and the job's transition to Completed status in a
    ///   single EF Core transaction with rollback, so a mid-operation failure cannot
    ///   deduct stock without producing an invoice (see ROADMAP.md Stage 2, Risk Watch).
    /// </summary>
    public Task<InvoiceDto> GenerateInvoiceAsync(int serviceJobId) => throw new NotImplementedException();

    public Task<InvoiceDto?> GetInvoiceByIdAsync(int id) => throw new NotImplementedException();

    public Task<InvoiceDto?> GetInvoiceByServiceJobAsync(int serviceJobId) => throw new NotImplementedException();

    public Task<IEnumerable<InvoiceDto>> GetAllInvoicesAsync() => throw new NotImplementedException();

    public Task<PaymentTransactionDto> RecordPaymentAsync(PaymentTransactionDto payment) => throw new NotImplementedException();

    public Task<IEnumerable<PaymentTransactionDto>> GetPaymentsByInvoiceAsync(int invoiceId) => throw new NotImplementedException();
}
