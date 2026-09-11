using System.Net;
using System.Text.RegularExpressions;
using Garij.Domain.Entities;
using Garij.Domain.Enums;
using Garij.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Garij.IntegrationTests;

/// <summary>
/// Comprehensive integration tests for multi-tenant garage isolation:
/// - Verifies that Customers, Vehicles, Service Jobs, Parts/Inventory, and Invoices
///   created in one garage are strictly invisible and inaccessible to any other garage.
/// - Verifies cross-garage resource access by ID returns 404 NotFound.
/// - Verifies public booking status lookup continues to resolve jobs across garages without auth.
/// </summary>
public class GarageFullMultiTenantIsolationIntegrationTests : IClassFixture<AuthorizationTestFactory>
{
    private static readonly Regex AntiForgeryTokenPattern = new(
        "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
        RegexOptions.Compiled);

    private readonly AuthorizationTestFactory _factory;

    public GarageFullMultiTenantIsolationIntegrationTests(AuthorizationTestFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateNonRedirectingClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private HttpClient CreateRedirectingClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });

    private static async Task<string> ExtractAntiForgeryTokenAsync(HttpResponseMessage response)
    {
        var html = await response.Content.ReadAsStringAsync();
        var match = AntiForgeryTokenPattern.Match(html);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private async Task<(HttpClient Client, string Email, string GarageId)> SetupGarageOwnerAsync(string name, string workshopName)
    {
        var client = CreateNonRedirectingClient();
        var email = $"owner_{Guid.NewGuid():N}@garagetest.com";
        var password = "Owner@123456!";

        // 1. Register account
        var regPage = await client.GetAsync("/Account/Register");
        var regToken = await ExtractAntiForgeryTokenAsync(regPage);
        var regForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["FullName"] = name,
            ["Email"] = email,
            ["PhoneNumber"] = "+1555123456",
            ["Password"] = password,
            ["ConfirmPassword"] = password,
            ["__RequestVerificationToken"] = regToken
        });
        var regResponse = await client.PostAsync("/Account/Register", regForm);
        Assert.Equal(HttpStatusCode.Redirect, regResponse.StatusCode);

        // 2. Complete purchase/checkout as Admin
        var purchasePage = await client.GetAsync("/Purchase");
        var purchaseToken = await ExtractAntiForgeryTokenAsync(purchasePage);
        var checkoutForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["BuyerName"] = name,
            ["BuyerEmail"] = email,
            ["WorkshopName"] = workshopName,
            ["PaymentMethod"] = "CreditCard",
            ["IsTestPayment"] = "true",
            ["AccountRole"] = "Admin",
            ["__RequestVerificationToken"] = purchaseToken
        });
        var checkoutResponse = await client.PostAsync("/Purchase/Checkout", checkoutForm);
        Assert.Equal(HttpStatusCode.Redirect, checkoutResponse.StatusCode);

        // 3. Extract GarageId
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GarijDbContext>();
        var user = await db.StaffUsers.FirstOrDefaultAsync(u => u.Email == email);
        Assert.NotNull(user);
        Assert.False(string.IsNullOrWhiteSpace(user.GarageId));

        return (client, email, user.GarageId);
    }

    [Fact]
    public async Task CustomersAndVehicles_AreStrictlyIsolated_BetweenDifferentGarages()
    {
        // Arrange: 2 independent garage owners
        var (ownerAClient, _, garageAId) = await SetupGarageOwnerAsync("Owner A", "Garage Alpha");
        var (ownerBClient, _, garageBId) = await SetupGarageOwnerAsync("Owner B", "Garage Beta");
        Assert.NotEqual(garageAId, garageBId);

        // 1. Owner A creates Customer A
        var custAPage = await ownerAClient.GetAsync("/Customer/Create");
        var custAToken = await ExtractAntiForgeryTokenAsync(custAPage);
        var custAUnique = $"Alice Alpha {Guid.NewGuid():N}";
        var custAForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["FullName"] = custAUnique,
            ["PhoneNumber"] = "+1555111111",
            ["Email"] = $"alice_{Guid.NewGuid():N}@alpha.test",
            ["Address"] = "100 Alpha St",
            ["__RequestVerificationToken"] = custAToken
        });
        var custAResp = await ownerAClient.PostAsync("/Customer/Create", custAForm);
        Assert.Equal(HttpStatusCode.Redirect, custAResp.StatusCode);

        // 2. Owner B creates Customer B
        var custBPage = await ownerBClient.GetAsync("/Customer/Create");
        var custBToken = await ExtractAntiForgeryTokenAsync(custBPage);
        var custBUnique = $"Bob Beta {Guid.NewGuid():N}";
        var custBForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["FullName"] = custBUnique,
            ["PhoneNumber"] = "+1555222222",
            ["Email"] = $"bob_{Guid.NewGuid():N}@beta.test",
            ["Address"] = "200 Beta Way",
            ["__RequestVerificationToken"] = custBToken
        });
        var custBResp = await ownerBClient.PostAsync("/Customer/Create", custBForm);
        Assert.Equal(HttpStatusCode.Redirect, custBResp.StatusCode);

        // Find customer IDs in DB
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GarijDbContext>();
            var custA = await db.Customers.FirstOrDefaultAsync(c => c.FullName == custAUnique);
            var custB = await db.Customers.FirstOrDefaultAsync(c => c.FullName == custBUnique);
            Assert.NotNull(custA);
            Assert.NotNull(custB);
            Assert.Equal(garageAId, custA.GarageId);
            Assert.Equal(garageBId, custB.GarageId);

            // 3. Verify Customer list isolation
            var listA = await (await ownerAClient.GetAsync("/Customer/Index")).Content.ReadAsStringAsync();
            Assert.Contains(custAUnique, listA);
            Assert.DoesNotContain(custBUnique, listA);

            var listB = await (await ownerBClient.GetAsync("/Customer/Index")).Content.ReadAsStringAsync();
            Assert.Contains(custBUnique, listB);
            Assert.DoesNotContain(custAUnique, listB);

            // 4. Verify Cross-garage customer details return 404 NotFound
            var crossCustDetails = await ownerAClient.GetAsync($"/Customer/Details/{custB.Id}");
            Assert.Equal(HttpStatusCode.NotFound, crossCustDetails.StatusCode);

            var crossCustDetailsB = await ownerBClient.GetAsync($"/Customer/Details/{custA.Id}");
            Assert.Equal(HttpStatusCode.NotFound, crossCustDetailsB.StatusCode);

            // 5. Owner A adds Vehicle A for Customer A
            var vehAPage = await ownerAClient.GetAsync($"/Vehicle/Create?customerId={custA.Id}");
            var vehAToken = await ExtractAntiForgeryTokenAsync(vehAPage);
            var plateA = $"AL-{Random.Shared.Next(1000, 9999)}";
            var vehAForm = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["CustomerId"] = custA.Id.ToString(),
                ["LicensePlateNumber"] = plateA,
                ["Make"] = "Toyota",
                ["Model"] = "Camry",
                ["Year"] = "2023",
                ["Vin"] = "17CHARVIN00000001",
                ["Color"] = "Silver",
                ["__RequestVerificationToken"] = vehAToken
            });
            var vehAResp = await ownerAClient.PostAsync("/Vehicle/Create", vehAForm);
            Assert.Equal(HttpStatusCode.Redirect, vehAResp.StatusCode);

            // 6. Owner B adds Vehicle B for Customer B
            var vehBPage = await ownerBClient.GetAsync($"/Vehicle/Create?customerId={custB.Id}");
            var vehBToken = await ExtractAntiForgeryTokenAsync(vehBPage);
            var plateB = $"BE-{Random.Shared.Next(1000, 9999)}";
            var vehBForm = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["CustomerId"] = custB.Id.ToString(),
                ["LicensePlateNumber"] = plateB,
                ["Make"] = "Honda",
                ["Model"] = "Accord",
                ["Year"] = "2022",
                ["Vin"] = "17CHARVIN00000002",
                ["Color"] = "Black",
                ["__RequestVerificationToken"] = vehBToken
            });
            var vehBResp = await ownerBClient.PostAsync("/Vehicle/Create", vehBForm);
            Assert.Equal(HttpStatusCode.Redirect, vehBResp.StatusCode);
        }

        // Verify Vehicle list and detail isolation
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GarijDbContext>();
            var vehA = await db.Vehicles.FirstOrDefaultAsync(v => v.GarageId == garageAId);
            var vehB = await db.Vehicles.FirstOrDefaultAsync(v => v.GarageId == garageBId);
            Assert.NotNull(vehA);
            Assert.NotNull(vehB);

            var vehListA = await (await ownerAClient.GetAsync("/Vehicle/Index")).Content.ReadAsStringAsync();
            Assert.Contains(vehA.LicensePlateNumber, vehListA);
            Assert.DoesNotContain(vehB.LicensePlateNumber, vehListA);

            var vehListB = await (await ownerBClient.GetAsync("/Vehicle/Index")).Content.ReadAsStringAsync();
            Assert.Contains(vehB.LicensePlateNumber, vehListB);
            Assert.DoesNotContain(vehA.LicensePlateNumber, vehListB);

            // Cross-garage vehicle details return 404
            var crossVehDetailsA = await ownerAClient.GetAsync($"/Vehicle/Details/{vehB.Id}");
            Assert.Equal(HttpStatusCode.NotFound, crossVehDetailsA.StatusCode);

            var crossVehDetailsB = await ownerBClient.GetAsync($"/Vehicle/Details/{vehA.Id}");
            Assert.Equal(HttpStatusCode.NotFound, crossVehDetailsB.StatusCode);
        }
    }

    [Fact]
    public async Task ServiceJobsAndParts_AreStrictlyIsolated_BetweenDifferentGarages()
    {
        var (ownerAClient, _, garageAId) = await SetupGarageOwnerAsync("Owner A2", "Garage Alpha2");
        var (ownerBClient, _, garageBId) = await SetupGarageOwnerAsync("Owner B2", "Garage Beta2");

        int vehAId, vehBId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GarijDbContext>();

            var custA = new Customer { GarageId = garageAId, FullName = "Customer A2", Email = $"ca2_{Guid.NewGuid():N}@test.com", PhoneNumber = "111", Address = "A2" };
            var custB = new Customer { GarageId = garageBId, FullName = "Customer B2", Email = $"cb2_{Guid.NewGuid():N}@test.com", PhoneNumber = "222", Address = "B2" };
            db.Customers.AddRange(custA, custB);
            await db.SaveChangesAsync();

            var vehA = new Vehicle { GarageId = garageAId, CustomerId = custA.Id, LicensePlateNumber = $"PLA2-{Guid.NewGuid():N}"[..8], Make = "Mazda", Model = "3", Year = 2021, Vin = "VIN00000000000003", Color = "Red" };
            var vehB = new Vehicle { GarageId = garageBId, CustomerId = custB.Id, LicensePlateNumber = $"PLB2-{Guid.NewGuid():N}"[..8], Make = "Ford", Model = "Focus", Year = 2020, Vin = "VIN00000000000004", Color = "Blue" };
            db.Vehicles.AddRange(vehA, vehB);
            await db.SaveChangesAsync();

            vehAId = vehA.Id;
            vehBId = vehB.Id;
        }

        // 1. Owner A creates Job A
        var jobAPage = await ownerAClient.GetAsync($"/ServiceJob/Create?vehicleId={vehAId}");
        var jobAToken = await ExtractAntiForgeryTokenAsync(jobAPage);
        var refA = $"REF-A-{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var jobAForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["VehicleId"] = vehAId.ToString(),
            ["JobType"] = nameof(JobType.RoutineService),
            ["Status"] = nameof(JobStatus.Requested),
            ["BookingReference"] = refA,
            ["DiagnosticNotes"] = "Oil change for Alpha",
            ["__RequestVerificationToken"] = jobAToken
        });
        var jobAResp = await ownerAClient.PostAsync("/ServiceJob/Create", jobAForm);
        Assert.Equal(HttpStatusCode.Redirect, jobAResp.StatusCode);

        // 2. Owner B creates Job B
        var jobBPage = await ownerBClient.GetAsync($"/ServiceJob/Create?vehicleId={vehBId}");
        var jobBToken = await ExtractAntiForgeryTokenAsync(jobBPage);
        var refB = $"REF-B-{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var jobBForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["VehicleId"] = vehBId.ToString(),
            ["JobType"] = nameof(JobType.Repair),
            ["Status"] = nameof(JobStatus.Requested),
            ["BookingReference"] = refB,
            ["DiagnosticNotes"] = "Brakes for Beta",
            ["__RequestVerificationToken"] = jobBToken
        });
        var jobBResp = await ownerBClient.PostAsync("/ServiceJob/Create", jobBForm);
        Assert.Equal(HttpStatusCode.Redirect, jobBResp.StatusCode);

        // 3. Owner A creates Part A
        var partAPage = await ownerAClient.GetAsync("/Parts/Create");
        var partAToken = await ExtractAntiForgeryTokenAsync(partAPage);
        var partAName = $"Special Filter A {Guid.NewGuid():N}"[..18];
        var partAForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Name"] = partAName,
            ["PartNumber"] = $"PN-A-{Guid.NewGuid():N}"[..8].ToUpperInvariant(),
            ["UnitPrice"] = "45.00",
            ["QuantityInStock"] = "20",
            ["ReorderLevel"] = "5",
            ["__RequestVerificationToken"] = partAToken
        });
        var partAResp = await ownerAClient.PostAsync("/Parts/Create", partAForm);
        Assert.Equal(HttpStatusCode.Redirect, partAResp.StatusCode);

        // 4. Owner B creates Part B
        var partBPage = await ownerBClient.GetAsync("/Parts/Create");
        var partBToken = await ExtractAntiForgeryTokenAsync(partBPage);
        var partBName = $"Special Rotor B {Guid.NewGuid():N}"[..18];
        var partBForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Name"] = partBName,
            ["PartNumber"] = $"PN-B-{Guid.NewGuid():N}"[..8].ToUpperInvariant(),
            ["UnitPrice"] = "95.00",
            ["QuantityInStock"] = "15",
            ["ReorderLevel"] = "3",
            ["__RequestVerificationToken"] = partBToken
        });
        var partBResp = await ownerBClient.PostAsync("/Parts/Create", partBForm);
        Assert.Equal(HttpStatusCode.Redirect, partBResp.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GarijDbContext>();
            var jobA = await db.ServiceJobs.FirstOrDefaultAsync(j => j.BookingReference == refA);
            var jobB = await db.ServiceJobs.FirstOrDefaultAsync(j => j.BookingReference == refB);
            var partA = await db.Parts.FirstOrDefaultAsync(p => p.Name == partAName);
            var partB = await db.Parts.FirstOrDefaultAsync(p => p.Name == partBName);

            Assert.NotNull(jobA);
            Assert.NotNull(jobB);
            Assert.NotNull(partA);
            Assert.NotNull(partB);
            Assert.Equal(garageAId, jobA.GarageId);
            Assert.Equal(garageBId, jobB.GarageId);
            Assert.Equal(garageAId, partA.GarageId);
            Assert.Equal(garageBId, partB.GarageId);

            // 5. Verify Job List isolation
            var jobListA = await (await ownerAClient.GetAsync("/ServiceJob/Index")).Content.ReadAsStringAsync();
            Assert.Contains(refA, jobListA);
            Assert.DoesNotContain(refB, jobListA);

            var jobListB = await (await ownerBClient.GetAsync("/ServiceJob/Index")).Content.ReadAsStringAsync();
            Assert.Contains(refB, jobListB);
            Assert.DoesNotContain(refA, jobListB);

            // Cross-garage job details return 404
            var crossJobDetailsA = await ownerAClient.GetAsync($"/ServiceJob/Details/{jobB.Id}");
            Assert.Equal(HttpStatusCode.NotFound, crossJobDetailsA.StatusCode);

            var crossJobDetailsB = await ownerBClient.GetAsync($"/ServiceJob/Details/{jobA.Id}");
            Assert.Equal(HttpStatusCode.NotFound, crossJobDetailsB.StatusCode);

            // 6. Verify Parts List isolation
            var partsListA = await (await ownerAClient.GetAsync("/Parts/Index")).Content.ReadAsStringAsync();
            Assert.Contains(partAName, partsListA);
            Assert.DoesNotContain(partBName, partsListA);

            var partsListB = await (await ownerBClient.GetAsync("/Parts/Index")).Content.ReadAsStringAsync();
            Assert.Contains(partBName, partsListB);
            Assert.DoesNotContain(partAName, partsListB);

            // Cross-garage part details return 404
            var crossPartDetailsA = await ownerAClient.GetAsync($"/Parts/Details/{partB.Id}");
            Assert.Equal(HttpStatusCode.NotFound, crossPartDetailsA.StatusCode);

            var crossPartDetailsB = await ownerBClient.GetAsync($"/Parts/Details/{partA.Id}");
            Assert.Equal(HttpStatusCode.NotFound, crossPartDetailsB.StatusCode);
        }
    }

    [Fact]
    public async Task InvoicesAndBilling_AreStrictlyIsolated_BetweenDifferentGarages()
    {
        var (ownerAClient, _, garageAId) = await SetupGarageOwnerAsync("Owner A3", "Garage Alpha3");
        var (ownerBClient, _, garageBId) = await SetupGarageOwnerAsync("Owner B3", "Garage Beta3");

        int invAId, invBId;
        string invANum, invBNum;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GarijDbContext>();

            var custA = new Customer { GarageId = garageAId, FullName = "Customer A3", Email = $"ca3_{Guid.NewGuid():N}@test.com", PhoneNumber = "111", Address = "A3" };
            var custB = new Customer { GarageId = garageBId, FullName = "Customer B3", Email = $"cb3_{Guid.NewGuid():N}@test.com", PhoneNumber = "222", Address = "B3" };
            db.Customers.AddRange(custA, custB);
            await db.SaveChangesAsync();

            var vehA = new Vehicle { GarageId = garageAId, CustomerId = custA.Id, LicensePlateNumber = $"PLA3-{Guid.NewGuid():N}"[..8], Make = "Audi", Model = "A4", Year = 2022, Vin = "VIN00000000000005", Color = "White" };
            var vehB = new Vehicle { GarageId = garageBId, CustomerId = custB.Id, LicensePlateNumber = $"PLB3-{Guid.NewGuid():N}"[..8], Make = "BMW", Model = "330i", Year = 2021, Vin = "VIN00000000000006", Color = "Grey" };
            db.Vehicles.AddRange(vehA, vehB);
            await db.SaveChangesAsync();

            var jobA = new ServiceJob { GarageId = garageAId, VehicleId = vehA.Id, CustomerId = custA.Id, BookingReference = $"BK-A3-{Guid.NewGuid():N}"[..12], JobType = JobType.RoutineService, Status = JobStatus.Completed, CreatedAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow };
            var jobB = new ServiceJob { GarageId = garageBId, VehicleId = vehB.Id, CustomerId = custB.Id, BookingReference = $"BK-B3-{Guid.NewGuid():N}"[..12], JobType = JobType.Repair, Status = JobStatus.Completed, CreatedAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow };
            db.ServiceJobs.AddRange(jobA, jobB);
            await db.SaveChangesAsync();

            invANum = $"INV-A3-{Guid.NewGuid():N}"[..12].ToUpperInvariant();
            invBNum = $"INV-B3-{Guid.NewGuid():N}"[..12].ToUpperInvariant();

            var invA = new Invoice { GarageId = garageAId, ServiceJobId = jobA.Id, InvoiceNumber = invANum, SubTotal = 200m, TaxAmount = 20m, TotalAmount = 220m, PaymentStatus = PaymentStatus.Pending, IssuedAt = DateTime.UtcNow };
            var invB = new Invoice { GarageId = garageBId, ServiceJobId = jobB.Id, InvoiceNumber = invBNum, SubTotal = 150m, TaxAmount = 15m, TotalAmount = 165m, PaymentStatus = PaymentStatus.Pending, IssuedAt = DateTime.UtcNow };
            db.Invoices.AddRange(invA, invB);
            await db.SaveChangesAsync();

            invAId = invA.Id;
            invBId = invB.Id;
        }

        // 1. Verify Invoices list isolation
        var listA = await (await ownerAClient.GetAsync("/Billing")).Content.ReadAsStringAsync();
        Assert.Contains(invANum, listA);
        Assert.DoesNotContain(invBNum, listA);

        var listB = await (await ownerBClient.GetAsync("/Billing")).Content.ReadAsStringAsync();
        Assert.Contains(invBNum, listB);
        Assert.DoesNotContain(invANum, listB);

        // 2. Cross-garage invoice details return 404
        var crossInvA = await ownerAClient.GetAsync($"/Billing/Details/{invBId}");
        Assert.Equal(HttpStatusCode.NotFound, crossInvA.StatusCode);

        var crossInvB = await ownerBClient.GetAsync($"/Billing/Details/{invAId}");
        Assert.Equal(HttpStatusCode.NotFound, crossInvB.StatusCode);
    }

    [Fact]
    public async Task PublicBookingLookup_ResolvesJobsFromAnyGarage_WithoutRequiringAuthentication()
    {
        string refAlpha, plateAlpha;
        string refBeta, plateBeta;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GarijDbContext>();

            var custA = new Customer { GarageId = "garage-alpha-pub", FullName = "Customer Alpha Pub", Email = $"capub_{Guid.NewGuid():N}@test.com", PhoneNumber = "111", Address = "Alpha" };
            var custB = new Customer { GarageId = "garage-beta-pub", FullName = "Customer Beta Pub", Email = $"cbpub_{Guid.NewGuid():N}@test.com", PhoneNumber = "222", Address = "Beta" };
            db.Customers.AddRange(custA, custB);
            await db.SaveChangesAsync();

            plateAlpha = $"PUB-A-{Guid.NewGuid():N}"[..8].ToUpperInvariant();
            plateBeta = $"PUB-B-{Guid.NewGuid():N}"[..8].ToUpperInvariant();

            var vehA = new Vehicle { GarageId = "garage-alpha-pub", CustomerId = custA.Id, LicensePlateNumber = plateAlpha, Make = "Tesla", Model = "Model 3", Year = 2023, Vin = "VIN00000000000007", Color = "White" };
            var vehB = new Vehicle { GarageId = "garage-beta-pub", CustomerId = custB.Id, LicensePlateNumber = plateBeta, Make = "Hyundai", Model = "Ioniq", Year = 2022, Vin = "VIN00000000000008", Color = "Blue" };
            db.Vehicles.AddRange(vehA, vehB);
            await db.SaveChangesAsync();

            refAlpha = $"BK-PUB-A-{Guid.NewGuid():N}"[..14].ToUpperInvariant();
            refBeta = $"BK-PUB-B-{Guid.NewGuid():N}"[..14].ToUpperInvariant();

            var jobA = new ServiceJob { GarageId = "garage-alpha-pub", VehicleId = vehA.Id, CustomerId = custA.Id, BookingReference = refAlpha, JobType = JobType.RoutineService, Status = JobStatus.InProgress, CreatedAt = DateTime.UtcNow };
            var jobB = new ServiceJob { GarageId = "garage-beta-pub", VehicleId = vehB.Id, CustomerId = custB.Id, BookingReference = refBeta, JobType = JobType.Checkup, Status = JobStatus.InProgress, CreatedAt = DateTime.UtcNow };
            db.ServiceJobs.AddRange(jobA, jobB);
            await db.SaveChangesAsync();
        }

        // An anonymous unauthenticated client visits the landing page with booking references
        var anonClient = CreateRedirectingClient();

        // 1. Lookup Alpha's job
        var respA = await anonClient.GetAsync($"/?query={refAlpha}#status-tracker");
        Assert.Equal(HttpStatusCode.OK, respA.StatusCode);
        var htmlA = await respA.Content.ReadAsStringAsync();
        Assert.Contains(refAlpha, htmlA);
        Assert.Contains(plateAlpha, htmlA);

        // 2. Lookup Beta's job
        var respB = await anonClient.GetAsync($"/?query={refBeta}#status-tracker");
        Assert.Equal(HttpStatusCode.OK, respB.StatusCode);
        var htmlB = await respB.Content.ReadAsStringAsync();
        Assert.Contains(refBeta, htmlB);
        Assert.Contains(plateBeta, htmlB);
    }
}
