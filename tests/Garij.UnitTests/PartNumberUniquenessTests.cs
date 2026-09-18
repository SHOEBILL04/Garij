using Garij.Application.DTOs;
using Garij.Application.Services;
using Garij.Domain.Entities;
using Garij.Domain.Exceptions;
using Garij.Infrastructure.Persistence;
using Garij.Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Garij.UnitTests;

/// <summary>
/// Covers defect D-06 (Report 05): creating a part rejected a duplicate part number, but
/// editing one did not, so a part could be renumbered onto another part's number and leave
/// two rows sharing one identifier - after which a stock adjustment or a parts-usage log no
/// longer identifies a single part.
///
/// Run against a real SQLite database rather than a fake repository, because half of what is
/// under test (the unique index) lives in the EF Core mapping and the database. A fake would
/// report success and prove nothing.
///
/// Covers TC-PRT-13, and guards TC-PRT-02 and TC-PRT-12 against regression.
/// </summary>
public class PartNumberUniquenessTests : IDisposable
{
    private const string GarageId = "default-garij-master";
    private const string OtherGarageId = "second-garage";

    private readonly SqliteConnection _connection;
    private readonly GarijDbContext _seedContext;

    public PartNumberUniquenessTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _seedContext = NewContext();
        _seedContext.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _seedContext.Dispose();
        _connection.Dispose();
    }

    private GarijDbContext NewContext() =>
        new(new DbContextOptionsBuilder<GarijDbContext>().UseSqlite(_connection).Options);

    private static PartsInventoryService ServiceFor(GarijDbContext context) =>
        new(new PartRepository(context), new JobPartUsedRepository(context));

    private async Task<int> SeedPartAsync(
        string partNumber,
        string name = "Seeded Part",
        decimal unitPrice = 20.00m,
        string? garageId = GarageId)
    {
        var part = new Part
        {
            Name = name,
            PartNumber = partNumber,
            UnitPrice = unitPrice,
            QuantityInStock = 10,
            ReorderLevel = 2,
            GarageId = garageId
        };

        _seedContext.Parts.Add(part);
        await _seedContext.SaveChangesAsync();
        return part.Id;
    }

    private static PartDto DtoFor(Part part) => new()
    {
        Id = part.Id,
        Name = part.Name,
        PartNumber = part.PartNumber,
        UnitPrice = part.UnitPrice,
        QuantityInStock = part.QuantityInStock,
        ReorderLevel = part.ReorderLevel,
        GarageId = part.GarageId
    };

    // =================================================================================
    // (a) Editing a part without changing its number still saves.
    // =================================================================================

    [Fact]
    public async Task UpdatePartAsync_ShouldSucceed_WhenThePartNumberIsUnchanged()
    {
        await SeedPartAsync("FLT-OIL-A", name: "Oil Filter Type-A");

        using var context = NewContext();
        var part = await context.Parts.SingleAsync(p => p.PartNumber == "FLT-OIL-A");

        var dto = DtoFor(part);
        dto.Name = "Oil Filter Type-A (Premium)";
        dto.ReorderLevel = 8;

        var updated = await ServiceFor(context).UpdatePartAsync(dto);

        // The part must not be blocked by matching itself.
        Assert.Equal("FLT-OIL-A", updated.PartNumber);
        Assert.Equal("Oil Filter Type-A (Premium)", updated.Name);
        Assert.Equal(8, updated.ReorderLevel);

        using var verify = NewContext();
        var stored = await verify.Parts.SingleAsync(p => p.Id == part.Id);
        Assert.Equal("Oil Filter Type-A (Premium)", stored.Name);
        Assert.Equal(8, stored.ReorderLevel);
    }

    [Fact]
    public async Task UpdatePartAsync_ShouldSucceed_WhenTheNumberChangesToOneNobodyUses()
    {
        await SeedPartAsync("FLT-OIL-A");
        await SeedPartAsync("BRK-PAD-F");

        using var context = NewContext();
        var part = await context.Parts.SingleAsync(p => p.PartNumber == "FLT-OIL-A");

        var dto = DtoFor(part);
        dto.PartNumber = "FLT-OIL-B";

        var updated = await ServiceFor(context).UpdatePartAsync(dto);

        Assert.Equal("FLT-OIL-B", updated.PartNumber);

        using var verify = NewContext();
        Assert.Equal("FLT-OIL-B", (await verify.Parts.SingleAsync(p => p.Id == part.Id)).PartNumber);
    }

    // =================================================================================
    // (b) TC-PRT-13: editing a part onto another part's number is rejected.
    // =================================================================================

    [Fact]
    public async Task UpdatePartAsync_ShouldRejectTheDuplicate_WhenRenumberedOntoAnotherPart()
    {
        await SeedPartAsync("FLT-OIL-A", name: "Oil Filter Type-A");
        var targetId = await SeedPartAsync("BRK-PAD-F", name: "Brake Pads Front Set");

        using var context = NewContext();
        var partToEdit = await context.Parts.SingleAsync(p => p.Id == targetId);

        var dto = DtoFor(partToEdit);
        dto.PartNumber = "FLT-OIL-A";

        var ex = await Assert.ThrowsAsync<ValidationException>(() => ServiceFor(context).UpdatePartAsync(dto));

        // Same wording as the create path (TC-PRT-02).
        Assert.Equal("A part with part number 'FLT-OIL-A' already exists.", ex.Errors[nameof(PartDto.PartNumber)][0]);

        // And nothing was written: the two parts still have their own numbers.
        using var verify = NewContext();
        Assert.Equal("BRK-PAD-F", (await verify.Parts.SingleAsync(p => p.Id == targetId)).PartNumber);
        Assert.Equal(1, await verify.Parts.CountAsync(p => p.PartNumber == "FLT-OIL-A"));
    }

    [Fact]
    public async Task AddPartAsync_ShouldStillRejectADuplicate_WithTheSameMessage()
    {
        // TC-PRT-02: the create path's behaviour must be unchanged now that both paths share
        // one check.
        await SeedPartAsync("FLT-OIL-A");

        using var context = NewContext();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => ServiceFor(context).AddPartAsync(new PartDto
        {
            Name = "Duplicate Oil Filter",
            PartNumber = "FLT-OIL-A",
            UnitPrice = 12.00m,
            QuantityInStock = 5,
            ReorderLevel = 2,
            GarageId = GarageId
        }));

        Assert.Equal("A part with part number 'FLT-OIL-A' already exists.", ex.Errors[nameof(PartDto.PartNumber)][0]);
        Assert.Equal(1, await NewContext().Parts.CountAsync(p => p.PartNumber == "FLT-OIL-A"));
    }

    [Fact]
    public async Task UpdatePartAsync_ShouldAllowANumberUsedByAnotherGarage()
    {
        // The check is scoped to the garage, so two workshops may stock the same
        // manufacturer part number without colliding.
        await SeedPartAsync("FLT-OIL-A", garageId: OtherGarageId);
        var partId = await SeedPartAsync("BRK-PAD-F", garageId: GarageId);

        using var context = NewContext();
        var part = await context.Parts.SingleAsync(p => p.Id == partId);

        var dto = DtoFor(part);
        dto.PartNumber = "FLT-OIL-A";

        var updated = await ServiceFor(context).UpdatePartAsync(dto);

        Assert.Equal("FLT-OIL-A", updated.PartNumber);
        Assert.Equal(2, await NewContext().Parts.CountAsync(p => p.PartNumber == "FLT-OIL-A"));
    }

    // =================================================================================
    // (c) The database rejects a duplicate even if the application check is bypassed.
    // =================================================================================

    [Fact]
    public async Task UniqueIndex_ShouldRejectADuplicate_WhenWrittenStraightThroughTheDbContext()
    {
        await SeedPartAsync("FLT-OIL-A");

        using var context = NewContext();
        context.Parts.Add(new Part
        {
            Name = "Smuggled Duplicate",
            PartNumber = "FLT-OIL-A",
            UnitPrice = 12.00m,
            QuantityInStock = 1,
            ReorderLevel = 1,
            GarageId = GarageId
        });

        // No service, no validation - straight at the database.
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        Assert.Contains("UNIQUE", ex.InnerException!.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await NewContext().Parts.CountAsync(p => p.PartNumber == "FLT-OIL-A"));
    }

    [Fact]
    public async Task UniqueIndex_ShouldRejectARenumberingDuplicate_WhenWrittenStraightThroughTheRepository()
    {
        // The same bypass on the edit path the defect describes: a code path that renumbers a
        // part without running the service check is still stopped by the database.
        await SeedPartAsync("FLT-OIL-A");
        var targetId = await SeedPartAsync("BRK-PAD-F");

        using var context = NewContext();
        var repository = new PartRepository(context);
        var part = await repository.GetByIdAsync(targetId);
        part!.PartNumber = "FLT-OIL-A";
        repository.Update(part);

        await Assert.ThrowsAsync<DbUpdateException>(() => repository.SaveChangesAsync());

        using var verify = NewContext();
        Assert.Equal("BRK-PAD-F", (await verify.Parts.SingleAsync(p => p.Id == targetId)).PartNumber);
    }

    [Fact]
    public async Task UniqueIndex_ShouldAllowTheSameNumberInDifferentGarages()
    {
        await SeedPartAsync("FLT-OIL-A", garageId: GarageId);
        await SeedPartAsync("FLT-OIL-A", garageId: OtherGarageId);

        Assert.Equal(2, await NewContext().Parts.CountAsync(p => p.PartNumber == "FLT-OIL-A"));
    }

    [Fact]
    public void UniqueIndex_IsDeclaredOnGarageIdAndPartNumber()
    {
        // Pins the shape of the index: scoped to the garage, and unique.
        using var context = NewContext();
        var index = context.Model
            .FindEntityType(typeof(Part))!
            .GetIndexes()
            .Single(i => i.Properties.Any(p => p.Name == nameof(Part.PartNumber)));

        Assert.True(index.IsUnique);
        Assert.Equal(
            new[] { nameof(Part.GarageId), nameof(Part.PartNumber) },
            index.Properties.Select(p => p.Name).ToArray());
    }

    // =================================================================================
    // (5) TC-PRT-12: changing unit price after usage has been logged is a different field
    // and must keep working exactly as before.
    // =================================================================================

    [Fact]
    public async Task UpdatePartAsync_ShouldStillAllowAPriceChange_AfterUsageHasBeenLogged()
    {
        var partId = await SeedPartAsync("FLT-OIL-A", unitPrice: 12.00m);
        var jobId = await SeedServiceJobAsync();

        using (var usageContext = NewContext())
        {
            await ServiceFor(usageContext).RecordPartUsageAsync(new JobPartUsedDto
            {
                ServiceJobId = jobId,
                PartId = partId,
                QuantityUsed = 2
            });
        }

        using var context = NewContext();
        var part = await context.Parts.SingleAsync(p => p.Id == partId);
        var dto = DtoFor(part);
        dto.UnitPrice = 18.50m;

        var updated = await ServiceFor(context).UpdatePartAsync(dto);

        Assert.Equal(18.50m, updated.UnitPrice);
        Assert.Equal("FLT-OIL-A", updated.PartNumber);

        // The price already locked onto the usage line is unaffected by the new price.
        using var verify = NewContext();
        Assert.Equal(18.50m, (await verify.Parts.SingleAsync(p => p.Id == partId)).UnitPrice);
        Assert.Equal(12.00m, (await verify.JobPartsUsed.SingleAsync(j => j.PartId == partId)).PriceAtUsage);
    }

    private async Task<int> SeedServiceJobAsync()
    {
        var customer = new Customer
        {
            FullName = "Uniqueness Test Customer",
            Email = $"uniq-{Guid.NewGuid():N}@test.local",
            PhoneNumber = "+8801711000015",
            Address = "Dhaka",
            CreatedAt = DateTime.UtcNow,
            GarageId = GarageId
        };

        var vehicle = new Vehicle
        {
            Customer = customer,
            LicensePlateNumber = $"UQ-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
            Make = "Toyota",
            Model = "Allion",
            Year = 2020,
            Vin = $"VIN-{Guid.NewGuid().ToString("N")[..8]}",
            Color = "Silver",
            GarageId = GarageId
        };

        var job = new ServiceJob
        {
            Customer = customer,
            Vehicle = vehicle,
            BookingReference = $"UQ-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
            JobType = Garij.Domain.Enums.JobType.RoutineService,
            Status = Garij.Domain.Enums.JobStatus.InProgress,
            CreatedAt = DateTime.UtcNow,
            GarageId = GarageId
        };

        _seedContext.ServiceJobs.Add(job);
        await _seedContext.SaveChangesAsync();
        return job.Id;
    }
}
