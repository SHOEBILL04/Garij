using System.Net;
using System.Text.RegularExpressions;
using Garij.Domain.Entities;
using Garij.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Garij.IntegrationTests;

/// <summary>
/// Covers defect D-06 (Report 05) through the real edit form: renumbering a part onto another
/// part's number used to be accepted, leaving two rows sharing one part number.
///
/// TC-PRT-13 is a system test, so these drive the actual HTTP endpoints and check both the
/// message the user sees and the rows that end up in the database.
/// </summary>
public class PartNumberUniquenessIntegrationTests : IClassFixture<AuthorizationTestFactory>
{
    private const string GarageId = "default-garij-master";

    private static readonly Regex AntiForgeryTokenPattern = new(
        "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
        RegexOptions.Compiled);

    private readonly AuthorizationTestFactory _factory;

    public PartNumberUniquenessIntegrationTests(AuthorizationTestFactory factory)
    {
        _factory = factory;
    }

    private static async Task<string> ExtractAntiForgeryTokenAsync(HttpResponseMessage response)
    {
        var html = await response.Content.ReadAsStringAsync();
        return AntiForgeryTokenPattern.Match(html).Groups[1].Value;
    }

    private async Task<HttpClient> LoginAsAdminAsync()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var loginPage = await client.GetAsync("/Account/Login");
        var token = await ExtractAntiForgeryTokenAsync(loginPage);

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "admin@garij.com",
            ["Password"] = "Admin@12345",
            ["__RequestVerificationToken"] = token,
        });

        var response = await client.PostAsync("/Account/Login", form);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        return client;
    }

    private static string Suffix() => Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    /// <summary>Seeds a part in the seeded admin's garage and returns its id and number.</summary>
    private (int Id, string PartNumber) SeedPart(string prefix)
    {
        var partNumber = $"{prefix}-{Suffix()}";

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GarijDbContext>();

        var part = new Part
        {
            Name = $"Test Part {partNumber}",
            PartNumber = partNumber,
            UnitPrice = 25.00m,
            QuantityInStock = 10,
            ReorderLevel = 2,
            GarageId = GarageId
        };

        db.Parts.Add(part);
        db.SaveChanges();
        return (part.Id, partNumber);
    }

    private async Task<HttpResponseMessage> PostEditAsync(
        HttpClient client, int partId, string partNumber, string name, decimal unitPrice = 25.00m, int reorderLevel = 2)
    {
        var page = await client.GetAsync($"/Parts/Edit/{partId}");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var token = await ExtractAntiForgeryTokenAsync(page);
        Assert.False(string.IsNullOrEmpty(token), "Could not find __RequestVerificationToken on the part edit form.");

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Id"] = partId.ToString(),
            ["Name"] = name,
            ["PartNumber"] = partNumber,
            ["UnitPrice"] = unitPrice.ToString("0.00"),
            ["ReorderLevel"] = reorderLevel.ToString(),
            ["__RequestVerificationToken"] = token,
        });

        return await client.PostAsync($"/Parts/Edit/{partId}", form);
    }

    private Part ReadPart(int partId)
    {
        using var scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<GarijDbContext>()
            .Parts.AsNoTracking().Single(p => p.Id == partId);
    }

    [Fact]
    public async Task EditingAPart_WithoutChangingItsNumber_StillSaves()
    {
        var client = await LoginAsAdminAsync();
        var part = SeedPart("KEEP");

        var response = await PostEditAsync(client, part.Id, part.PartNumber, "Renamed But Same Number", reorderLevel: 7);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var stored = ReadPart(part.Id);
        Assert.Equal(part.PartNumber, stored.PartNumber);
        Assert.Equal("Renamed But Same Number", stored.Name);
        Assert.Equal(7, stored.ReorderLevel);
    }

    [Fact]
    public async Task EditingAPart_OntoAnotherPartsNumber_IsRejectedWithTheCreatePathMessage()
    {
        // TC-PRT-13.
        var client = await LoginAsAdminAsync();
        var existing = SeedPart("TAKEN");
        var partToEdit = SeedPart("MINE");

        var response = await PostEditAsync(client, partToEdit.Id, existing.PartNumber, "Attempted Duplicate");
        var body = await response.Content.ReadAsStringAsync();

        // Rejected, and redisplayed with the same wording the create screen uses (TC-PRT-02).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"A part with part number &#x27;{existing.PartNumber}&#x27; already exists.", body);

        // The two parts still have their own numbers.
        Assert.Equal(partToEdit.PartNumber, ReadPart(partToEdit.Id).PartNumber);
        Assert.Equal(existing.PartNumber, ReadPart(existing.Id).PartNumber);

        using var scope = _factory.Services.CreateScope();
        var duplicates = scope.ServiceProvider.GetRequiredService<GarijDbContext>()
            .Parts.Count(p => p.PartNumber == existing.PartNumber);
        Assert.Equal(1, duplicates);
    }

    [Fact]
    public async Task CreatingAPart_WithAnExistingNumber_IsStillRejected()
    {
        // TC-PRT-02 must keep behaving as before now that both paths share one check.
        var client = await LoginAsAdminAsync();
        var existing = SeedPart("CREATE");

        var page = await client.GetAsync("/Parts/Create");
        var token = await ExtractAntiForgeryTokenAsync(page);

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Name"] = "Duplicate On Create",
            ["PartNumber"] = existing.PartNumber,
            ["UnitPrice"] = "15.00",
            ["QuantityInStock"] = "5",
            ["ReorderLevel"] = "2",
            ["__RequestVerificationToken"] = token,
        });

        var response = await client.PostAsync("/Parts/Create", form);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"A part with part number &#x27;{existing.PartNumber}&#x27; already exists.", body);

        using var scope = _factory.Services.CreateScope();
        Assert.Equal(1, scope.ServiceProvider.GetRequiredService<GarijDbContext>()
            .Parts.Count(p => p.PartNumber == existing.PartNumber));
    }

    [Fact]
    public async Task EditingAPartsUnitPrice_AfterUsageHasBeenLogged_StillWorks()
    {
        // TC-PRT-12: a different field, and out of scope for this fix.
        var client = await LoginAsAdminAsync();
        var part = SeedPart("PRICE");
        LogUsageAgainst(part.Id);

        var response = await PostEditAsync(
            client, part.Id, part.PartNumber, $"Test Part {part.PartNumber}", unitPrice: 41.75m);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var stored = ReadPart(part.Id);
        Assert.Equal(41.75m, stored.UnitPrice);
        Assert.Equal(part.PartNumber, stored.PartNumber);

        // The price locked onto the existing usage line is untouched by the new unit price.
        using var scope = _factory.Services.CreateScope();
        var usage = scope.ServiceProvider.GetRequiredService<GarijDbContext>()
            .JobPartsUsed.AsNoTracking().Single(j => j.PartId == part.Id);
        Assert.Equal(25.00m, usage.PriceAtUsage);
    }

    /// <summary>Logs one unit of the part against a freshly seeded job, at the part's current price.</summary>
    private void LogUsageAgainst(int partId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GarijDbContext>();
        var suffix = Suffix();

        var customer = new Customer
        {
            FullName = "Part Usage Customer",
            Email = $"pu-{suffix}@test.local",
            PhoneNumber = "+8801711000016",
            Address = "Dhaka",
            CreatedAt = DateTime.UtcNow,
            GarageId = GarageId
        };

        var vehicle = new Vehicle
        {
            Customer = customer,
            LicensePlateNumber = $"PU-{suffix}",
            Make = "Toyota",
            Model = "Allion",
            Year = 2020,
            Vin = $"VIN-PU-{suffix}",
            Color = "Silver",
            GarageId = GarageId
        };

        var job = new ServiceJob
        {
            Customer = customer,
            Vehicle = vehicle,
            BookingReference = $"PU-{suffix}",
            JobType = Garij.Domain.Enums.JobType.RoutineService,
            Status = Garij.Domain.Enums.JobStatus.InProgress,
            CreatedAt = DateTime.UtcNow,
            GarageId = GarageId
        };

        var part = db.Parts.Single(p => p.Id == partId);
        job.JobPartsUsed.Add(new JobPartUsed
        {
            ServiceJob = job,
            PartId = partId,
            QuantityUsed = 1,
            PriceAtUsage = part.UnitPrice
        });

        db.ServiceJobs.Add(job);
        db.SaveChanges();
    }
}
