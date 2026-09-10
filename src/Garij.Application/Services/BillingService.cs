using Garij.Application.Configuration;
using Garij.Application.DTOs;
using Garij.Application.Interfaces;
using Garij.Domain.Entities;
using Garij.Domain.Enums;
using Garij.Domain.Exceptions;
using Garij.Infrastructure.Persistence;
using Garij.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace Garij.Application.Services;

public class BillingService : IBillingService
{
    private readonly GarijDbContext _context;
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IPaymentTransactionRepository _paymentTransactionRepository;
    private readonly IServiceJobRepository _serviceJobRepository;
    private readonly IServiceJobService _serviceJobService;
    private readonly decimal _taxRatePercent;

    public BillingService(
        GarijDbContext context,
        IInvoiceRepository invoiceRepository,
        IPaymentTransactionRepository paymentTransactionRepository,
        IServiceJobRepository serviceJobRepository,
        IServiceJobService serviceJobService,
        IOptions<BillingSettings> billingSettings)
    {
        _context = context;
        _invoiceRepository = invoiceRepository;
        _paymentTransactionRepository = paymentTransactionRepository;
        _serviceJobRepository = serviceJobRepository;
        _serviceJobService = serviceJobService;
        _taxRatePercent = billingSettings.Value.TaxRatePercent;
    }

    /// <summary>
    /// Opens a transaction on the same scoped DbContext every repository in this request
    /// shares, so writes made through any repository (including ServiceJobService's own
    /// SaveChangesAsync call) enlist in it and roll back together on failure.
    /// </summary>
    private Task<IDbContextTransaction> BeginTransactionAsync() => _context.Database.BeginTransactionAsync();

    /// <summary>
    /// Line totals (PriceAtBooking × Quantity, PriceAtUsage × QuantityUsed) are already at
    /// 2-decimal money precision, so they are summed as-is with no intermediate rounding.
    /// Only the resulting SubTotal is rounded (per line rounding first can drift the total
    /// by a cent versus rounding once at the end), and TaxAmount is rounded off that
    /// already-rounded SubTotal.
    /// </summary>
    private (decimal SubTotal, decimal TaxAmount, decimal TotalAmount) CalculateTotals(
        IEnumerable<JobServiceDetail> serviceDetails,
        IEnumerable<JobPartUsed> partsUsed)
    {
        var labourTotal = serviceDetails.Sum(jsd => jsd.PriceAtBooking * jsd.Quantity);
        var partsTotal = partsUsed.Sum(jpu => jpu.PriceAtUsage * jpu.QuantityUsed);

        var subTotal = Math.Round(labourTotal + partsTotal, 2, MidpointRounding.AwayFromZero);
        var taxAmount = Math.Round(subTotal * (_taxRatePercent / 100m), 2, MidpointRounding.AwayFromZero);
        var totalAmount = subTotal + taxAmount;

        return (subTotal, taxAmount, totalAmount);
    }

    /// <summary>Mirrors ServiceJobService.GenerateUniqueBookingReferenceAsync's candidate-loop shape.</summary>
    private async Task<string> GenerateUniqueInvoiceNumberAsync()
    {
        var year = DateTime.UtcNow.Year;
        var existingNumbers = (await _invoiceRepository.GetAllAsync())
            .Select(i => i.InvoiceNumber)
            .ToHashSet();

        var count = existingNumbers.Count + 1;
        string candidate;
        do
        {
            candidate = $"INV-{year}-{count:D4}";
            count++;
        }
        while (existingNumbers.Contains(candidate));

        return candidate;
    }

    private async Task EnsureNotAlreadyInvoicedAsync(int serviceJobId)
    {
        var existing = await _invoiceRepository.GetByServiceJobIdAsync(serviceJobId);
        if (existing is not null)
        {
            throw new BusinessRuleException("BR-010", $"Service job {serviceJobId} already has invoice '{existing.InvoiceNumber}'.");
        }
    }

    private static void EnsureHasLineItems(ServiceJob job)
    {
        if (job.JobServiceDetails.Count == 0 && job.JobPartsUsed.Count == 0)
        {
            throw new BusinessRuleException("BR-011", $"Service job {job.Id} has no billable line items — log services or parts before generating an invoice.");
        }
    }

    /// <summary>
    /// Sums labour and parts, then wraps invoice creation and the job's transition to
    /// Completed in one transaction: a failure anywhere between the insert and the status
    /// update rolls back both, so a mid-operation crash can never leave stock deducted
    /// (already committed by the parts-logging step, before this ever runs) with no invoice,
    /// or an invoice with the job still not marked Completed. See ROADMAP.md Stage 2, Risk
    /// Watch, and does not perform transition-legality validation — that is
    /// ValidateStatusTransition's job, not built yet (see the Stage 2 readiness report).
    /// </summary>
    public async Task<InvoiceDto> GenerateInvoiceAsync(int serviceJobId)
    {
        var job = await _serviceJobRepository.GetByIdWithDetailsAsync(serviceJobId)
            ?? throw new NotFoundException(nameof(ServiceJob), serviceJobId);

        await EnsureNotAlreadyInvoicedAsync(serviceJobId);
        EnsureHasLineItems(job);

        var (subTotal, taxAmount, totalAmount) = CalculateTotals(job.JobServiceDetails, job.JobPartsUsed);
        var invoiceNumber = await GenerateUniqueInvoiceNumberAsync();

        var invoice = new Invoice
        {
            ServiceJobId = serviceJobId,
            InvoiceNumber = invoiceNumber,
            SubTotal = subTotal,
            TaxAmount = taxAmount,
            TotalAmount = totalAmount,
            PaymentStatus = PaymentStatus.Pending,
            IssuedAt = DateTime.UtcNow
        };

        await using var transaction = await BeginTransactionAsync();
        try
        {
            await _invoiceRepository.AddAsync(invoice);
            await _invoiceRepository.SaveChangesAsync();

            await _serviceJobService.UpdateServiceJobStatusAsync(serviceJobId, JobStatus.Completed);

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

        return await MapToDetailedDtoAsync(invoice, job);
    }

    public async Task<InvoiceDto?> GetInvoiceByIdAsync(int id)
    {
        var invoice = await _invoiceRepository.GetByIdWithPaymentsAsync(id);
        return invoice is null ? null : await MapToDetailedDtoAsync(invoice);
    }

    public async Task<InvoiceDto?> GetInvoiceByServiceJobAsync(int serviceJobId)
    {
        var invoice = await _invoiceRepository.GetByServiceJobIdAsync(serviceJobId);
        if (invoice is null)
        {
            return null;
        }

        var withPayments = await _invoiceRepository.GetByIdWithPaymentsAsync(invoice.Id);
        return await MapToDetailedDtoAsync(withPayments!);
    }

    public async Task<IEnumerable<InvoiceDto>> GetAllInvoicesAsync()
    {
        var invoices = await _invoiceRepository.GetAllAsync();
        return invoices.Select(MapToSummaryDto);
    }

    /// <summary>
    /// PaymentStatus is always recomputed from the sum of every payment on the invoice, never
    /// from just the payment being recorded — a sequence of partials must land on the right
    /// status at each step, not only on the final one.
    /// </summary>
    public async Task<PaymentTransactionDto> RecordPaymentAsync(PaymentTransactionDto payment)
    {
        if (payment.Amount <= 0)
        {
            throw new BusinessRuleException("BR-012", "Payment amount must be greater than zero.");
        }

        var invoice = await _invoiceRepository.GetByIdWithPaymentsAsync(payment.InvoiceId)
            ?? throw new NotFoundException(nameof(Invoice), payment.InvoiceId);

        var amountPaidSoFar = invoice.PaymentTransactions.Sum(p => p.Amount);
        var outstanding = invoice.TotalAmount - amountPaidSoFar;

        if (payment.Amount > outstanding)
        {
            throw new BusinessRuleException(
                "BR-013",
                $"Payment of {payment.Amount:0.00} exceeds the outstanding balance of {outstanding:0.00} on invoice '{invoice.InvoiceNumber}'.");
        }

        var entity = new PaymentTransaction
        {
            InvoiceId = invoice.Id,
            Amount = payment.Amount,
            PaymentMethod = payment.PaymentMethod,
            TransactionReference = payment.TransactionReference ?? string.Empty,
            PaidAt = DateTime.UtcNow
        };

        var newAmountPaid = amountPaidSoFar + payment.Amount;
        invoice.PaymentStatus = newAmountPaid == invoice.TotalAmount
            ? PaymentStatus.Paid
            : newAmountPaid > 0
                ? PaymentStatus.PartiallyPaid
                : PaymentStatus.Pending;

        await _paymentTransactionRepository.AddAsync(entity);
        _invoiceRepository.Update(invoice);
        await _paymentTransactionRepository.SaveChangesAsync();

        return MapPaymentToDto(entity);
    }

    public async Task<IEnumerable<PaymentTransactionDto>> GetPaymentsByInvoiceAsync(int invoiceId)
    {
        var invoice = await _invoiceRepository.GetByIdWithPaymentsAsync(invoiceId)
            ?? throw new NotFoundException(nameof(Invoice), invoiceId);

        return invoice.PaymentTransactions.OrderBy(p => p.PaidAt).Select(MapPaymentToDto);
    }

    /// <summary>Full breakdown (line items, payment history, running balance) for a single invoice view.</summary>
    private async Task<InvoiceDto> MapToDetailedDtoAsync(Invoice invoice, ServiceJob? job = null)
    {
        job ??= await _serviceJobRepository.GetByIdWithDetailsAsync(invoice.ServiceJobId);

        var serviceLines = job?.JobServiceDetails.Select(jsd => new InvoiceLineItemDto
        {
            Description = jsd.ServiceCatalog?.Name ?? "Service",
            Quantity = jsd.Quantity,
            UnitPrice = jsd.PriceAtBooking,
            LineTotal = jsd.PriceAtBooking * jsd.Quantity
        }).ToList() ?? new List<InvoiceLineItemDto>();

        var partLines = job?.JobPartsUsed.Select(jpu => new InvoiceLineItemDto
        {
            Description = jpu.Part?.Name ?? "Part",
            Quantity = jpu.QuantityUsed,
            UnitPrice = jpu.PriceAtUsage,
            LineTotal = jpu.PriceAtUsage * jpu.QuantityUsed
        }).ToList() ?? new List<InvoiceLineItemDto>();

        var payments = invoice.PaymentTransactions
            .OrderBy(p => p.PaidAt)
            .Select(MapPaymentToDto)
            .ToList();
        var amountPaid = payments.Sum(p => p.Amount);

        return new InvoiceDto
        {
            Id = invoice.Id,
            ServiceJobId = invoice.ServiceJobId,
            InvoiceNumber = invoice.InvoiceNumber,
            SubTotal = invoice.SubTotal,
            TaxAmount = invoice.TaxAmount,
            TotalAmount = invoice.TotalAmount,
            PaymentStatus = invoice.PaymentStatus,
            IssuedAt = invoice.IssuedAt,
            BookingReference = job?.BookingReference ?? string.Empty,
            CustomerName = job?.Customer?.FullName ?? string.Empty,
            VehicleDescription = job is null ? string.Empty : $"{job.Vehicle.Year} {job.Vehicle.Make} {job.Vehicle.Model}".Trim(),
            ServiceLines = serviceLines,
            PartLines = partLines,
            Payments = payments,
            AmountPaid = amountPaid,
            OutstandingBalance = invoice.TotalAmount - amountPaid
        };
    }

    /// <summary>Header-only shape for the invoice list view — avoids an N+1 job/payment load per row.</summary>
    private static InvoiceDto MapToSummaryDto(Invoice invoice) => new()
    {
        Id = invoice.Id,
        ServiceJobId = invoice.ServiceJobId,
        InvoiceNumber = invoice.InvoiceNumber,
        SubTotal = invoice.SubTotal,
        TaxAmount = invoice.TaxAmount,
        TotalAmount = invoice.TotalAmount,
        PaymentStatus = invoice.PaymentStatus,
        IssuedAt = invoice.IssuedAt
    };

    private static PaymentTransactionDto MapPaymentToDto(PaymentTransaction payment) => new()
    {
        Id = payment.Id,
        InvoiceId = payment.InvoiceId,
        Amount = payment.Amount,
        PaymentMethod = payment.PaymentMethod,
        TransactionReference = payment.TransactionReference,
        PaidAt = payment.PaidAt
    };
}
