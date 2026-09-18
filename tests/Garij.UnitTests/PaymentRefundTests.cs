using Garij.Application.Configuration;
using Garij.Application.DTOs;
using Garij.Application.Services;
using Garij.Domain.Entities;
using Garij.Domain.Enums;
using Garij.Domain.Exceptions;
using Garij.Infrastructure.Persistence;
using Garij.Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Garij.UnitTests;

/// <summary>
/// Covers defect D-09 sub-task B (Report 05): no refund could be made anywhere in the system, while
/// the Revenue report already had refund handling that nothing could ever feed.
///
/// A refund is a full refund of one payment, recorded by stamping RefundedAt on it. These tests run
/// the real BillingService and ReportingService over one SQLite database, and drive every refund
/// through RefundPaymentAsync - so the Revenue report is checked against data the refund action
/// actually writes, not against hand-built rows.
///
/// Covers TC-PAY-10, and keeps TC-UNT-14's revenue expectations (ReportingServiceTests) intact.
/// </summary>
public class PaymentRefundTests : IDisposable
{
    private const string GarageId = "default-garij-master";

    private readonly SqliteConnection _connection;

    public PaymentRefundTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using var context = NewContext();
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private GarijDbContext NewContext() =>
        new(new DbContextOptionsBuilder<GarijDbContext>().UseSqlite(_connection).Options);

    private static BillingService BillingFor(GarijDbContext context)
    {
        var jobRepository = new ServiceJobRepository(context);
        var jobService = new ServiceJobService(
            jobRepository,
            new VehicleRepository(context),
            new UserRepository(context),
            new MechanicAssignmentRepository(context),
            new NotificationService(new NotificationRepository(context)));

        return new BillingService(
            context,
            new InvoiceRepository(context),
            new PaymentTransactionRepository(context),
            jobRepository,
            jobService,
            Options.Create(new BillingSettings { TaxRatePercent = 15m }));
    }

    private static ReportingService ReportingFor(GarijDbContext context) =>
        new(context, new PartsInventoryService(new PartRepository(context), new JobPartUsedRepository(context)));

    /// <summary>Seeds an unpaid invoice for the given total and returns its id.</summary>
    private async Task<int> SeedInvoiceAsync(decimal totalAmount)
    {
        using var context = NewContext();
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        var customer = new Customer
        {
            FullName = "Refund Test Customer",
            Email = $"rf-{suffix}@test.local",
            PhoneNumber = "+8801711000019",
            Address = "Dhaka",
            CreatedAt = DateTime.UtcNow,
            GarageId = GarageId
        };

        var invoice = new Invoice
        {
            ServiceJob = new ServiceJob
            {
                Customer = customer,
                Vehicle = new Vehicle
                {
                    Customer = customer,
                    LicensePlateNumber = $"RF-{suffix}",
                    Make = "Toyota",
                    Model = "Allion",
                    Year = 2020,
                    Vin = $"VIN-RF-{suffix}",
                    Color = "Silver",
                    GarageId = GarageId
                },
                BookingReference = $"RF-{suffix}",
                JobType = JobType.RoutineService,
                Status = JobStatus.Completed,
                CreatedAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow,
                GarageId = GarageId
            },
            InvoiceNumber = $"INV-RF-{suffix}",
            SubTotal = totalAmount,
            TaxAmount = 0m,
            TotalAmount = totalAmount,
            PaymentStatus = PaymentStatus.Pending,
            IssuedAt = DateTime.UtcNow,
            GarageId = GarageId
        };

        context.Invoices.Add(invoice);
        await context.SaveChangesAsync();
        return invoice.Id;
    }

    private async Task<int> RecordPaymentAsync(int invoiceId, decimal amount)
    {
        using var context = NewContext();
        var payment = await BillingFor(context).RecordPaymentAsync(new PaymentTransactionDto
        {
            InvoiceId = invoiceId,
            Amount = amount,
            PaymentMethod = PaymentMethod.Cash,
            TransactionReference = "RCPT-1"
        });
        return payment.Id;
    }

    private async Task<PaymentTransactionDto> RefundAsync(int invoiceId, int paymentId)
    {
        using var context = NewContext();
        return await BillingFor(context).RefundPaymentAsync(invoiceId, paymentId);
    }

    private async Task<InvoiceDto> ReadInvoiceAsync(int invoiceId)
    {
        using var context = NewContext();
        return (await BillingFor(context).GetInvoiceByIdAsync(invoiceId))!;
    }

    // =====================================================================================
    // A refund reduces the paid amount and reopens the balance.
    // =====================================================================================

    [Fact]
    public async Task RefundPaymentAsync_OnTheOnlyPayment_ReopensTheWholeBalance()
    {
        var invoiceId = await SeedInvoiceAsync(200m);
        var paymentId = await RecordPaymentAsync(invoiceId, 200m);
        Assert.Equal(PaymentStatus.Paid, (await ReadInvoiceAsync(invoiceId)).PaymentStatus);

        var refunded = await RefundAsync(invoiceId, paymentId);

        Assert.True(refunded.IsRefunded);

        var invoice = await ReadInvoiceAsync(invoiceId);
        Assert.Equal(0m, invoice.AmountPaid);
        Assert.Equal(200m, invoice.AmountRefunded);
        Assert.Equal(200m, invoice.OutstandingBalance);
        Assert.Equal(PaymentStatus.Pending, invoice.PaymentStatus);
    }

    [Fact]
    public async Task RefundPaymentAsync_OnOneOfTwoPayments_ReopensOnlyThatPaymentsAmount()
    {
        var invoiceId = await SeedInvoiceAsync(300m);
        await RecordPaymentAsync(invoiceId, 120m);
        var secondPaymentId = await RecordPaymentAsync(invoiceId, 180m);
        Assert.Equal(0m, (await ReadInvoiceAsync(invoiceId)).OutstandingBalance);

        await RefundAsync(invoiceId, secondPaymentId);

        var invoice = await ReadInvoiceAsync(invoiceId);
        Assert.Equal(120m, invoice.AmountPaid);
        Assert.Equal(180m, invoice.AmountRefunded);
        Assert.Equal(180m, invoice.OutstandingBalance);
        Assert.Equal(PaymentStatus.PartiallyPaid, invoice.PaymentStatus);
    }

    [Fact]
    public async Task RefundPaymentAsync_KeepsTheRefundedPaymentInTheHistory_MarkedAsRefunded()
    {
        var invoiceId = await SeedInvoiceAsync(150m);
        var keptId = await RecordPaymentAsync(invoiceId, 50m);
        var refundedId = await RecordPaymentAsync(invoiceId, 100m);

        await RefundAsync(invoiceId, refundedId);

        var payments = (await ReadInvoiceAsync(invoiceId)).Payments;
        Assert.Equal(2, payments.Count);

        var refunded = payments.Single(p => p.Id == refundedId);
        Assert.True(refunded.IsRefunded);
        Assert.NotNull(refunded.RefundedAt);
        Assert.Equal(100m, refunded.Amount);

        Assert.False(payments.Single(p => p.Id == keptId).IsRefunded);
    }

    [Fact]
    public async Task RecordPaymentAsync_AcceptsPaymentOfTheReopenedBalance_AfterARefund()
    {
        // The reopened balance must actually be payable: a refunded payment no longer counts
        // towards what has been paid when the next payment is checked against the balance.
        var invoiceId = await SeedInvoiceAsync(250m);
        var paymentId = await RecordPaymentAsync(invoiceId, 250m);
        await RefundAsync(invoiceId, paymentId);

        await RecordPaymentAsync(invoiceId, 250m);

        var invoice = await ReadInvoiceAsync(invoiceId);
        Assert.Equal(250m, invoice.AmountPaid);
        Assert.Equal(0m, invoice.OutstandingBalance);
        Assert.Equal(PaymentStatus.Paid, invoice.PaymentStatus);
    }

    // =====================================================================================
    // A refund is only possible for a payment that was actually recorded, and only once.
    // =====================================================================================

    [Fact]
    public async Task RefundPaymentAsync_CannotRefundTheSamePaymentTwice()
    {
        var invoiceId = await SeedInvoiceAsync(100m);
        var paymentId = await RecordPaymentAsync(invoiceId, 100m);
        await RefundAsync(invoiceId, paymentId);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => RefundAsync(invoiceId, paymentId));

        Assert.Equal("BR-013", ex.RuleCode);
        Assert.Contains("already refunded", ex.Message);

        var invoice = await ReadInvoiceAsync(invoiceId);
        Assert.Equal(100m, invoice.AmountRefunded);
        Assert.Equal(100m, invoice.OutstandingBalance);
    }

    [Fact]
    public async Task RefundPaymentAsync_RejectsAPaymentThatDoesNotBelongToTheInvoice()
    {
        var invoiceId = await SeedInvoiceAsync(100m);
        var otherInvoiceId = await SeedInvoiceAsync(100m);
        var otherPaymentId = await RecordPaymentAsync(otherInvoiceId, 100m);

        await Assert.ThrowsAsync<NotFoundException>(() => RefundAsync(invoiceId, otherPaymentId));

        Assert.Equal(0m, (await ReadInvoiceAsync(otherInvoiceId)).AmountRefunded);
    }

    [Fact]
    public async Task RefundPaymentAsync_RejectsAnUnknownPaymentOrInvoice()
    {
        var invoiceId = await SeedInvoiceAsync(100m);

        await Assert.ThrowsAsync<NotFoundException>(() => RefundAsync(invoiceId, 99999));
        await Assert.ThrowsAsync<NotFoundException>(() => RefundAsync(99999, 1));
    }

    // =====================================================================================
    // A refund is excluded from the Revenue report's collected revenue.
    // =====================================================================================

    [Fact]
    public async Task RevenueReport_ExcludesARefundedPaymentFromCollected_AndShowsItAsTheExcludedAmount()
    {
        var invoiceId = await SeedInvoiceAsync(400m);
        await RecordPaymentAsync(invoiceId, 150m);
        var refundedPaymentId = await RecordPaymentAsync(invoiceId, 250m);

        var periodStart = DateTime.UtcNow.Date.AddDays(-1);
        var periodEnd = DateTime.UtcNow.Date.AddDays(1);

        RevenueReportDto before;
        using (var context = NewContext())
        {
            before = await ReportingFor(context).GetRevenueReportAsync(periodStart, periodEnd);
        }

        Assert.Equal(400m, before.TotalCollected);
        Assert.Equal(0m, before.RefundedPaymentAmount);

        await RefundAsync(invoiceId, refundedPaymentId);

        RevenueReportDto after;
        using (var context = NewContext())
        {
            after = await ReportingFor(context).GetRevenueReportAsync(periodStart, periodEnd);
        }

        // Collected drops by exactly the refunded payment, in the total and the monthly breakdown.
        Assert.Equal(150m, after.TotalCollected);
        Assert.Equal(150m, after.MonthlyCollectedBreakdown.Sum(m => m.CollectedAmount));
        Assert.Equal(1, after.MonthlyCollectedBreakdown.Sum(m => m.TransactionCount));

        // The excluded amount is reported, and reconciles back to everything received.
        Assert.Equal(250m, after.RefundedPaymentAmount);
        Assert.Equal(1, after.RefundedPaymentCount);
        Assert.Equal(before.TotalCollected, after.TotalCollected + after.RefundedPaymentAmount);

        // The invoice is still owed, so it stays in billed revenue; it is not a voided invoice.
        Assert.Equal(before.TotalBilledGross, after.TotalBilledGross);
        Assert.Equal(before.TotalInvoices, after.TotalInvoices);
        Assert.Equal(0, after.RefundedInvoiceCount);
    }

    [Fact]
    public async Task RevenueReport_StillCountsAPaymentWithoutARefund_OnAnInvoiceMarkedRefunded()
    {
        // TC-UNT-14's contract, checked here beside the new behaviour so the two are seen together:
        // an invoice marked Refunded is excluded from billed and counted in the refunded-invoice
        // figures, and a payment on it that was never refunded remains collected.
        var invoiceId = await SeedInvoiceAsync(345m);
        await RecordPaymentAsync(invoiceId, 345m);

        using (var context = NewContext())
        {
            var invoice = await context.Invoices.SingleAsync(i => i.Id == invoiceId);
            invoice.PaymentStatus = PaymentStatus.Refunded;
            await context.SaveChangesAsync();
        }

        using var reportContext = NewContext();
        var report = await ReportingFor(reportContext).GetRevenueReportAsync(DateTime.UtcNow.Date.AddDays(-1), DateTime.UtcNow.Date.AddDays(1));

        Assert.Equal(0m, report.TotalBilledGross);
        Assert.Equal(1, report.RefundedInvoiceCount);
        Assert.Equal(345m, report.RefundedGrossAmount);
        Assert.Equal(345m, report.TotalCollected);
        Assert.Equal(0m, report.RefundedPaymentAmount);
    }
}
