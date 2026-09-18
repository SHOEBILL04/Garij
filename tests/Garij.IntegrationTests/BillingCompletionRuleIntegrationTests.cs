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

namespace Garij.IntegrationTests;

/// <summary>
/// BR-008 guards the transition to Completed, and generating an invoice is the second
/// door to that transition - GenerateInvoiceAsync marks the job Completed through the
/// real ServiceJobService. These tests wire both real services together so the rule is
/// exercised through the invoicing path as well as the direct status update.
///
/// BR-008 is implemented as "at least one logged part OR one recorded service" rather
/// than the literally documented "at least one part": a labour-only job (a diagnostic,
/// an inspection) carries real work and is already invoiceable under BR-011, so a
/// parts-only reading would make such a job permanently un-invoiceable.
/// </summary>
public class BillingCompletionRuleIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<GarijDbContext> _options;

    public BillingCompletionRuleIntegrationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<GarijDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new GarijDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private static (BillingService billing, ServiceJobService jobs, JobServiceDetailService labour) CreateServices(GarijDbContext context)
    {
        var jobRepo = new ServiceJobRepository(context);
        var notificationService = new NotificationService(new NotificationRepository(context));

        var serviceJobService = new ServiceJobService(
            jobRepo,
            new VehicleRepository(context),
            new UserRepository(context),
            new MechanicAssignmentRepository(context),
            notificationService);

        var billingService = new BillingService(
            context,
            new InvoiceRepository(context),
            new PaymentTransactionRepository(context),
            jobRepo,
            serviceJobService,
            Options.Create(new BillingSettings { TaxRatePercent = 15m }));

        var labourService = new JobServiceDetailService(
            new ServiceCatalogRepository(context),
            new JobServiceDetailRepository(context),
            jobRepo);

        return (billingService, serviceJobService, labourService);
    }

    /// <summary>Seeds an InProgress job, optionally with a labour line and/or a parts line.</summary>
    private static async Task<int> SeedJobAsync(GarijDbContext context, string reference, bool withLabour, bool withParts)
    {
        var customer = new Customer { FullName = "Nadia Islam", Email = $"{reference}@test.local", PhoneNumber = "+8801711000009", Address = "Dhaka", CreatedAt = DateTime.UtcNow };
        var vehicle = new Vehicle { Customer = customer, LicensePlateNumber = reference, Make = "Toyota", Model = "Allion", Year = 2020, Vin = $"VIN-{reference}", Color = "Silver" };

        var job = new ServiceJob
        {
            Customer = customer,
            Vehicle = vehicle,
            BookingReference = reference,
            JobType = JobType.RoutineService,
            Status = JobStatus.InProgress,
            CreatedAt = DateTime.UtcNow
        };

        if (withLabour)
        {
            var catalog = new ServiceCatalog { Name = "Diagnostic Inspection", Description = "Full diagnostic", BasePrice = 80.00m, EstimatedDurationMinutes = 60 };
            job.JobServiceDetails.Add(new JobServiceDetail { ServiceJob = job, ServiceCatalog = catalog, Quantity = 1, PriceAtBooking = 80.00m });
        }

        if (withParts)
        {
            var part = new Part { Name = "Engine Oil 5W-30", PartNumber = $"OIL-{reference}", UnitPrice = 20.00m, QuantityInStock = 100, ReorderLevel = 10 };
            job.JobPartsUsed.Add(new JobPartUsed { ServiceJob = job, Part = part, QuantityUsed = 1, PriceAtUsage = 20.00m });
        }

        context.ServiceJobs.Add(job);
        await context.SaveChangesAsync();

        return job.Id;
    }

    [Fact]
    public async Task GenerateInvoiceAsync_InvoicesAndCompletesJob_WhenPartsAreLogged()
    {
        // Arrange
        await using var context = new GarijDbContext(_options);
        var (billing, _, _) = CreateServices(context);
        var jobId = await SeedJobAsync(context, "BIL-PARTS", withLabour: true, withParts: true);

        // Act
        var invoice = await billing.GenerateInvoiceAsync(jobId);

        // Assert
        Assert.Equal(100.00m, invoice.SubTotal);

        var persisted = await context.ServiceJobs.FindAsync(jobId);
        Assert.NotNull(persisted);
        Assert.Equal(JobStatus.Completed, persisted.Status);
        Assert.NotNull(persisted.CompletedAt);
    }

    [Fact]
    public async Task GenerateInvoiceAsync_InvoicesAndCompletesJob_WhenOnlyLabourIsLogged()
    {
        // Arrange: no parts at all - the case a literal "parts only" BR-008 would deadlock.
        await using var context = new GarijDbContext(_options);
        var (billing, _, _) = CreateServices(context);
        var jobId = await SeedJobAsync(context, "BIL-LABOUR", withLabour: true, withParts: false);

        // Act
        var invoice = await billing.GenerateInvoiceAsync(jobId);

        // Assert
        Assert.Equal(80.00m, invoice.SubTotal);
        Assert.Empty(invoice.PartLines);

        var persisted = await context.ServiceJobs.FindAsync(jobId);
        Assert.NotNull(persisted);
        Assert.Equal(JobStatus.Completed, persisted.Status);
        Assert.NotNull(persisted.CompletedAt);
    }

    [Fact]
    public async Task EmptyJob_IsRefusedByBothDoorsToCompleted_AndStaysInProgress()
    {
        // Arrange: nothing logged - no labour, no parts.
        await using var context = new GarijDbContext(_options);
        var (billing, jobs, _) = CreateServices(context);
        var jobId = await SeedJobAsync(context, "BIL-EMPTY", withLabour: false, withParts: false);

        // Act & Assert - door 1, the direct status update: BR-008 refuses it.
        var statusEx = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            jobs.UpdateServiceJobStatusAsync(jobId, JobStatus.Completed));
        Assert.Equal("BR-008", statusEx.RuleCode);

        // Act & Assert - door 2, invoicing: BR-011 screens the same empty job out before
        // the transaction opens, so BR-008 never has to fire on this path. Either way the
        // job cannot reach Completed with nothing logged against it.
        var invoiceEx = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            billing.GenerateInvoiceAsync(jobId));
        Assert.Equal("BR-011", invoiceEx.RuleCode);

        // The job is untouched and no invoice was written.
        var persisted = await context.ServiceJobs.FindAsync(jobId);
        Assert.NotNull(persisted);
        Assert.Equal(JobStatus.InProgress, persisted.Status);
        Assert.Null(persisted.CompletedAt);
        Assert.False(await context.Invoices.AnyAsync(i => i.ServiceJobId == jobId));
    }

    /// <summary>
    /// TC-ACC-03: labour attached through the real labour service, parts through the real
    /// parts service, then one invoice that must carry both. Before D-03 there was no way
    /// to attach the labour half at all, so every invoice was parts-only.
    /// </summary>
    [Fact]
    public async Task Invoice_CarriesBothLabourAndParts_WhenEachIsLoggedThroughItsOwnService()
    {
        // Arrange: a bare job, plus a catalogue service and a part to draw from.
        await using var context = new GarijDbContext(_options);
        var (billing, _, labour) = CreateServices(context);
        var partsService = new PartsInventoryService(new PartRepository(context), new JobPartUsedRepository(context));

        var jobId = await SeedJobAsync(context, "BIL-BOTH", withLabour: false, withParts: false);

        var catalogService = new ServiceCatalog { Name = "Wheel Alignment", Description = "4-wheel laser alignment", BasePrice = 65.00m, EstimatedDurationMinutes = 50 };
        context.ServiceCatalogs.Add(catalogService);
        var part = new Part { Name = "Brake Pads Front Set", PartNumber = "BRK-PAD-F", UnitPrice = 60.00m, QuantityInStock = 10, ReorderLevel = 2 };
        context.Parts.Add(part);
        await context.SaveChangesAsync();

        // Act: attach one labour line (2 x 65.00) and one parts line (3 x 60.00).
        await labour.LogJobServiceAsync(new JobServiceDetailDto { ServiceJobId = jobId, ServiceCatalogId = catalogService.Id, Quantity = 2 });
        await partsService.RecordPartUsageAsync(new JobPartUsedDto { ServiceJobId = jobId, PartId = part.Id, QuantityUsed = 3 });

        var invoice = await billing.GenerateInvoiceAsync(jobId);

        // Assert: 130.00 labour + 180.00 parts = 310.00, +15% tax = 356.50.
        var serviceLine = Assert.Single(invoice.ServiceLines);
        Assert.Equal("Wheel Alignment", serviceLine.Description);
        Assert.Equal(130.00m, serviceLine.LineTotal);

        var partLine = Assert.Single(invoice.PartLines);
        Assert.Equal(180.00m, partLine.LineTotal);

        Assert.Equal(310.00m, invoice.SubTotal);
        Assert.Equal(46.50m, invoice.TaxAmount);
        Assert.Equal(356.50m, invoice.TotalAmount);

        // Parts logging still decrements stock - untouched by this change.
        Assert.Equal(7, (await context.Parts.FindAsync(part.Id))!.QuantityInStock);
    }

    /// <summary>
    /// TC-BIL-08 plus the D-02 interaction: labour attached through the new service is
    /// enough on its own to satisfy BR-008, so a job that never consumed a part completes.
    /// </summary>
    [Fact]
    public async Task LabourAttachedThroughService_SatisfiesBr008_AndCompletesJobWithNoParts()
    {
        // Arrange
        await using var context = new GarijDbContext(_options);
        var (_, jobs, labour) = CreateServices(context);
        var jobId = await SeedJobAsync(context, "BIL-LAB08", withLabour: false, withParts: false);

        var catalogService = new ServiceCatalog { Name = "Battery & Electrical System Check", Description = "Load test", BasePrice = 45.00m, EstimatedDurationMinutes = 30 };
        context.ServiceCatalogs.Add(catalogService);
        await context.SaveChangesAsync();

        // Before any labour is attached the job is still empty, so BR-008 holds it back.
        var blocked = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            jobs.UpdateServiceJobStatusAsync(jobId, JobStatus.Completed));
        Assert.Equal("BR-008", blocked.RuleCode);

        // Act: attach labour only - no parts are ever logged against this job.
        await labour.LogJobServiceAsync(new JobServiceDetailDto { ServiceJobId = jobId, ServiceCatalogId = catalogService.Id, Quantity = 1 });
        var completed = await jobs.UpdateServiceJobStatusAsync(jobId, JobStatus.Completed);

        // Assert
        Assert.Equal(JobStatus.Completed, completed.Status);
        Assert.NotNull(completed.CompletedAt);
        Assert.Empty(context.JobPartsUsed.Where(jpu => jpu.ServiceJobId == jobId));
    }
}
