using Garij.Application.DTOs;
using Garij.Application.Services;
using Garij.Domain.Entities;
using Garij.Domain.Enums;
using Garij.Domain.Exceptions;
using Garij.Infrastructure.Persistence;
using Garij.Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Garij.UnitTests;

/// <summary>
/// Stock decrement under contention, exercised against a real SQLite database rather than
/// fakes. The behaviour under test — the optimistic concurrency token and the CHECK
/// constraints — lives in the database and its EF Core mapping, so a fake repository would
/// happily report success and prove nothing.
///
/// Every context shares one open in-memory connection, so they all see the same database
/// while keeping independent change trackers. That is what lets a stale snapshot exist.
/// </summary>
public class PartsInventoryConcurrencyTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly GarijDbContext _seedContext;

    public PartsInventoryConcurrencyTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _seedContext = NewContext();
        _seedContext.Database.EnsureCreated();
    }

    [Fact]
    public async Task RecordPartUsageAsync_ShouldRejectTheSecondWriter_WhenTwoMechanicsLogTheSamePartAtOnce()
    {
        var (jobId, partId) = await SeedJobAndPartAsync(quantityInStock: 10);

        // Two mechanics open the parts-logging screen at the same moment. Each context
        // loads its own snapshot of the part, and both of them see 10 in stock.
        using var firstMechanic = NewContext();
        using var secondMechanic = NewContext();
        await firstMechanic.Parts.FindAsync(partId);
        await secondMechanic.Parts.FindAsync(partId);

        await ServiceFor(firstMechanic).RecordPartUsageAsync(
            new JobPartUsedDto { ServiceJobId = jobId, PartId = partId, QuantityUsed = 6 });

        // The second mechanic's arithmetic (10 - 6 = 4) was correct when they loaded the
        // page and is wrong by the time they submit. The token in the UPDATE's WHERE clause
        // no longer matches, so the write affects zero rows and is rejected.
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            ServiceFor(secondMechanic).RecordPartUsageAsync(
                new JobPartUsedDto { ServiceJobId = jobId, PartId = partId, QuantityUsed = 6 }));

        Assert.Equal("BR-017", ex.RuleCode);

        // Without the token both writes would have committed: 12 units dispensed from a
        // stock of 10, and the count still reading 4 because the second write overwrote
        // the first rather than building on it.
        using var verify = NewContext();
        Assert.Equal(4, (await verify.Parts.FindAsync(partId))!.QuantityInStock);
        Assert.Equal(1, await verify.JobPartsUsed.CountAsync(j => j.PartId == partId));
    }

    [Fact]
    public async Task RecordPartUsageAsync_ShouldNotWriteTheUsageLine_WhenTheStockUpdateLosesTheRace()
    {
        var (jobId, partId) = await SeedJobAndPartAsync(quantityInStock: 4);

        using var firstMechanic = NewContext();
        using var secondMechanic = NewContext();
        await firstMechanic.Parts.FindAsync(partId);
        await secondMechanic.Parts.FindAsync(partId);

        await ServiceFor(firstMechanic).RecordPartUsageAsync(
            new JobPartUsedDto { ServiceJobId = jobId, PartId = partId, QuantityUsed = 1 });

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            ServiceFor(secondMechanic).RecordPartUsageAsync(
                new JobPartUsedDto { ServiceJobId = jobId, PartId = partId, QuantityUsed = 1 }));

        // The decrement and the usage line are saved together, so a rejected decrement must
        // not leave an orphaned line behind that the invoice would later bill for.
        using var verify = NewContext();
        Assert.Equal(3, (await verify.Parts.FindAsync(partId))!.QuantityInStock);
        Assert.Equal(1, await verify.JobPartsUsed.CountAsync(j => j.PartId == partId));
    }

    [Fact]
    public async Task AdjustStockAsync_ShouldRejectTheSecondWriter_WhenTwoStaffAdjustTheSamePartAtOnce()
    {
        var (_, partId) = await SeedJobAndPartAsync(quantityInStock: 20);

        using var firstClerk = NewContext();
        using var secondClerk = NewContext();
        await firstClerk.Parts.FindAsync(partId);
        await secondClerk.Parts.FindAsync(partId);

        await ServiceFor(firstClerk).AdjustStockAsync(partId, 30);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            ServiceFor(secondClerk).AdjustStockAsync(partId, 30));

        Assert.Equal("BR-017", ex.RuleCode);

        // One delivery of 30 was booked in, so the count is 50 and not 80.
        using var verify = NewContext();
        Assert.Equal(50, (await verify.Parts.FindAsync(partId))!.QuantityInStock);
    }

    [Fact]
    public async Task RecordPartUsageAsync_ShouldSucceed_WhenTheSecondWriterReloadsAfterTheConflict()
    {
        var (jobId, partId) = await SeedJobAndPartAsync(quantityInStock: 10);

        using var firstMechanic = NewContext();
        using var secondMechanic = NewContext();
        await firstMechanic.Parts.FindAsync(partId);
        await secondMechanic.Parts.FindAsync(partId);

        await ServiceFor(firstMechanic).RecordPartUsageAsync(
            new JobPartUsedDto { ServiceJobId = jobId, PartId = partId, QuantityUsed = 6 });

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            ServiceFor(secondMechanic).RecordPartUsageAsync(
                new JobPartUsedDto { ServiceJobId = jobId, PartId = partId, QuantityUsed = 6 }));

        // BR-017 tells the user to reload and retry, so that path has to actually work.
        using var retry = NewContext();
        await ServiceFor(retry).RecordPartUsageAsync(
            new JobPartUsedDto { ServiceJobId = jobId, PartId = partId, QuantityUsed = 4 });

        using var verify = NewContext();
        Assert.Equal(0, (await verify.Parts.FindAsync(partId))!.QuantityInStock);
        Assert.Equal(2, await verify.JobPartsUsed.CountAsync(j => j.PartId == partId));
    }

    [Fact]
    public async Task Database_ShouldRejectNegativeStock_WhenTheServiceLayerIsBypassed()
    {
        var (_, partId) = await SeedJobAndPartAsync(quantityInStock: 5);

        using var context = NewContext();
        var part = await context.Parts.FindAsync(partId);
        part!.QuantityInStock = -1;

        // CK_Part_QuantityInStock is the second line of defence: a code path that skips
        // PartsInventoryService still cannot leave the inventory in an impossible state.
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        // Named explicitly so this cannot start passing because of some unrelated failure
        // (a concurrency conflict, say) that also surfaces as a DbUpdateException.
        Assert.Contains("CK_Part_QuantityInStock", ex.InnerException!.Message);
    }

    [Fact]
    public async Task Database_ShouldRejectNonPositivePartUsage_WhenTheServiceLayerIsBypassed()
    {
        var (jobId, partId) = await SeedJobAndPartAsync(quantityInStock: 5);

        using var context = NewContext();
        context.JobPartsUsed.Add(new JobPartUsed
        {
            ServiceJobId = jobId,
            PartId = partId,
            QuantityUsed = 0,
            PriceAtUsage = 10.00m
        });

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        Assert.Contains("CK_JobPartUsed_QuantityUsed", ex.InnerException!.Message);
    }

    [Fact]
    public async Task GetLowStockPartsAsync_ShouldReportThePart_OnceUsageDrivesStockToTheReorderLevel()
    {
        var (jobId, partId) = await SeedJobAndPartAsync(quantityInStock: 5, reorderLevel: 4);

        using var beforeContext = NewContext();
        Assert.Empty(await ServiceFor(beforeContext).GetLowStockPartsAsync());

        using var usageContext = NewContext();
        await ServiceFor(usageContext).RecordPartUsageAsync(
            new JobPartUsedDto { ServiceJobId = jobId, PartId = partId, QuantityUsed = 1 });

        // Reorder alerts fire at the level, not below it, so 4 of 4 already counts as low.
        using var afterContext = NewContext();
        var lowStock = await ServiceFor(afterContext).GetLowStockPartsAsync();

        var reported = Assert.Single(lowStock);
        Assert.Equal(partId, reported.Id);
        Assert.Equal(4, reported.QuantityInStock);
    }

    private GarijDbContext NewContext() =>
        new(new DbContextOptionsBuilder<GarijDbContext>().UseSqlite(_connection).Options);

    private static PartsInventoryService ServiceFor(GarijDbContext context) =>
        new(new PartRepository(context), new JobPartUsedRepository(context));

    /// <summary>
    /// Seeds a complete customer/vehicle/job chain so the usage lines satisfy their foreign
    /// keys, and returns the job and part the tests act on.
    /// </summary>
    private async Task<(int JobId, int PartId)> SeedJobAndPartAsync(int quantityInStock, int reorderLevel = 2)
    {
        var suffix = await _seedContext.Parts.CountAsync() + 1;

        var customer = new Customer
        {
            FullName = "Rubaiat Ar Rabib",
            Email = $"customer{suffix}@garij.com",
            PhoneNumber = "+8801700000000",
            Address = "Khulna",
            CreatedAt = DateTime.UtcNow
        };
        _seedContext.Customers.Add(customer);
        await _seedContext.SaveChangesAsync();

        var vehicle = new Vehicle
        {
            CustomerId = customer.Id,
            LicensePlateNumber = $"KHL-{suffix:D4}",
            Make = "Toyota",
            Model = "Corolla",
            Year = 2022,
            Vin = $"VIN{suffix:D6}",
            Color = "White"
        };
        _seedContext.Vehicles.Add(vehicle);
        await _seedContext.SaveChangesAsync();

        var job = new ServiceJob
        {
            CustomerId = customer.Id,
            VehicleId = vehicle.Id,
            BookingReference = $"GRJ-2026-{suffix:D4}",
            JobType = JobType.Repair,
            Status = JobStatus.InProgress,
            CreatedAt = DateTime.UtcNow
        };
        _seedContext.ServiceJobs.Add(job);
        await _seedContext.SaveChangesAsync();

        var part = new Part
        {
            Name = "Premium Brake Pads Front Set",
            PartNumber = $"BRK-PAD-{suffix:D4}",
            UnitPrice = 60.00m,
            QuantityInStock = quantityInStock,
            ReorderLevel = reorderLevel
        };
        _seedContext.Parts.Add(part);
        await _seedContext.SaveChangesAsync();

        return (job.Id, part.Id);
    }

    public void Dispose()
    {
        _seedContext.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
