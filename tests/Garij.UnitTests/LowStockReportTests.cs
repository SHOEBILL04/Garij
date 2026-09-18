using Garij.Application.Services;
using Garij.Domain.Entities;
using Garij.Infrastructure.Persistence;
using Garij.Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Garij.UnitTests;

/// <summary>
/// Covers defect D-09 sub-task A (Report 05): the Low Stock report page rendered "This report
/// isn't built yet." and ReportingService.GetLowStockReportAsync threw NotImplementedException.
///
/// The report must list exactly what the Parts Inventory screen's "Low stock" badge flags, so
/// these tests check it against that badge's rule (QuantityInStock &lt;= ReorderLevel) on every
/// side of the boundary, rather than only against a hand-picked expected list.
///
/// Covers TC-RPT-08 and the Low Stock part of TC-ACC-05.
/// </summary>
public class LowStockReportTests : IDisposable
{
    private const string GarageId = "default-garij-master";

    private readonly SqliteConnection _connection;
    private readonly GarijDbContext _context;
    private readonly ReportingService _reportingService;
    private readonly PartsInventoryService _partsInventoryService;

    public LowStockReportTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _context = new GarijDbContext(new DbContextOptionsBuilder<GarijDbContext>().UseSqlite(_connection).Options);
        _context.Database.EnsureCreated();

        _partsInventoryService = new PartsInventoryService(new PartRepository(_context), new JobPartUsedRepository(_context));
        _reportingService = new ReportingService(_context, _partsInventoryService);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private void SeedPart(string partNumber, int quantityInStock, int reorderLevel, string garageId = GarageId, decimal unitPrice = 10m)
    {
        _context.Parts.Add(new Part
        {
            Name = $"Part {partNumber}",
            PartNumber = partNumber,
            UnitPrice = unitPrice,
            QuantityInStock = quantityInStock,
            ReorderLevel = reorderLevel,
            GarageId = garageId
        });
        _context.SaveChanges();
    }

    /// <summary>The Parts Inventory badge's rule, exactly as Views/Parts/Index.cshtml applies it.</summary>
    private static bool BadgeShowsLowStock(int quantityInStock, int reorderLevel) => quantityInStock <= reorderLevel;

    [Fact]
    public async Task GetLowStockReportAsync_ListsExactlyThePartsAtOrBelowTheirReorderLevel()
    {
        SeedPart("BELOW", quantityInStock: 2, reorderLevel: 5);
        SeedPart("EQUAL", quantityInStock: 5, reorderLevel: 5);   // boundary: at the level counts
        SeedPart("ABOVE", quantityInStock: 6, reorderLevel: 5);   // boundary: one over does not
        SeedPart("EMPTY", quantityInStock: 0, reorderLevel: 3);
        SeedPart("PLENTY", quantityInStock: 40, reorderLevel: 10);

        var report = (await _reportingService.GetLowStockReportAsync()).ToList();

        Assert.Equal(
            new[] { "BELOW", "EMPTY", "EQUAL" },
            report.Select(r => r.PartNumber).OrderBy(n => n).ToArray());
    }

    [Fact]
    public async Task GetLowStockReportAsync_AgreesWithTheInventoryBadge_ForEveryPart()
    {
        SeedPart("P-01", 0, 0);
        SeedPart("P-02", 1, 0);
        SeedPart("P-03", 3, 4);
        SeedPart("P-04", 4, 4);
        SeedPart("P-05", 5, 4);
        SeedPart("P-06", 10, 25);
        SeedPart("P-07", 25, 10);

        // What the Parts Inventory screen lists, and which of those rows carry the badge.
        var inventory = await _partsInventoryService.GetAllPartsAsync();
        var badged = inventory
            .Where(p => BadgeShowsLowStock(p.QuantityInStock, p.ReorderLevel))
            .Select(p => p.PartNumber)
            .OrderBy(n => n)
            .ToArray();

        var reported = (await _reportingService.GetLowStockReportAsync())
            .Select(r => r.PartNumber)
            .OrderBy(n => n)
            .ToArray();

        Assert.NotEmpty(badged);
        Assert.Equal(badged, reported);
    }

    [Fact]
    public async Task GetLowStockReportAsync_OnlyReportsTheCurrentGarage()
    {
        SeedPart("MINE-LOW", 1, 5, garageId: GarageId);
        SeedPart("THEIRS-LOW", 1, 5, garageId: "another-garage");

        var report = await _reportingService.GetLowStockReportAsync();

        Assert.Equal("MINE-LOW", Assert.Single(report).PartNumber);
    }

    [Fact]
    public async Task GetLowStockReportAsync_CarriesStockFiguresAndPutsTheMostUrgentFirst()
    {
        SeedPart("AT-LEVEL", quantityInStock: 5, reorderLevel: 5, unitPrice: 12.50m);
        SeedPart("FAR-BELOW", quantityInStock: 1, reorderLevel: 9);
        SeedPart("OUT", quantityInStock: 0, reorderLevel: 2);

        var report = (await _reportingService.GetLowStockReportAsync()).ToList();

        Assert.Equal(new[] { "OUT", "FAR-BELOW", "AT-LEVEL" }, report.Select(r => r.PartNumber).ToArray());

        var outOfStock = report[0];
        Assert.True(outOfStock.IsOutOfStock);
        Assert.Equal(2, outOfStock.ShortfallBelowReorderLevel);

        var farBelow = report[1];
        Assert.False(farBelow.IsOutOfStock);
        Assert.Equal(1, farBelow.CurrentStock);
        Assert.Equal(9, farBelow.ReorderLevel);
        Assert.Equal(8, farBelow.ShortfallBelowReorderLevel);

        var atLevel = report[2];
        Assert.Equal(0, atLevel.ShortfallBelowReorderLevel);
        Assert.Equal(12.50m, atLevel.UnitPrice);
    }

    [Fact]
    public async Task GetLowStockReportAsync_IsEmpty_WhenEveryPartIsAboveItsReorderLevel()
    {
        SeedPart("OK-1", 10, 2);
        SeedPart("OK-2", 3, 2);

        Assert.Empty(await _reportingService.GetLowStockReportAsync());
    }

    [Fact]
    public async Task GetLowStockReportAsync_PicksUpAPartOnceStockDropsToTheReorderLevel()
    {
        SeedPart("DRAINING", quantityInStock: 4, reorderLevel: 3);
        Assert.Empty(await _reportingService.GetLowStockReportAsync());

        var partId = _context.Parts.Single(p => p.PartNumber == "DRAINING").Id;
        await _partsInventoryService.AdjustStockAsync(partId, -1);

        var report = await _reportingService.GetLowStockReportAsync();
        Assert.Equal("DRAINING", Assert.Single(report).PartNumber);
    }
}
