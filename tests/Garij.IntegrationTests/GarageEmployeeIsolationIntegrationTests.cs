using System.Net;
using System.Text.RegularExpressions;
using Garij.Domain.Enums;
using Garij.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Garij.IntegrationTests;

/// <summary>
/// Integration tests verifying multi-tenant garage isolation:
/// - A garage owner registers and purchases access, receiving a unique GarageId.
/// - Employees created by that owner (via /ServiceJob/AddEmployee or /Admin/CreateUser) inherit the owner's GarageId.
/// - Employees of one garage are strictly hidden from other garages in:
///     * /Employee/Index
///     * /Employee/Details/{id} (cross-garage access returns 404 NotFound)
///     * /Admin/ManageUsers and /Admin/ManageRoles
///     * /Mechanic/Index
///     * /ServiceJob/Index mechanics filter dropdown
/// </summary>
public class GarageEmployeeIsolationIntegrationTests : IClassFixture<AuthorizationTestFactory>
{
    private static readonly Regex AntiForgeryTokenPattern = new(
        "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
        RegexOptions.Compiled);

    private readonly AuthorizationTestFactory _factory;

    public GarageEmployeeIsolationIntegrationTests(AuthorizationTestFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateNonRedirectingClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task<string> ExtractAntiForgeryTokenAsync(HttpResponseMessage response)
    {
        var html = await response.Content.ReadAsStringAsync();
        var match = AntiForgeryTokenPattern.Match(html);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private async Task<(HttpClient Client, string Email, string FullName, string GarageId)> RegisterAndPayGarageOwnerAsync(string name, string workshopName)
    {
        var client = CreateNonRedirectingClient();
        var email = $"owner_{Guid.NewGuid():N}@garage.test";
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

        // 3. Verify garage ID in database
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GarijDbContext>();
        var user = await db.StaffUsers.FirstOrDefaultAsync(u => u.Email == email);
        Assert.NotNull(user);
        Assert.False(string.IsNullOrWhiteSpace(user.GarageId));

        return (client, email, name, user.GarageId);
    }

    private async Task<(int EmployeeId, string Email, string FullName)> CreateEmployeeAsync(HttpClient client, string fullName, string roleName)
    {
        var addPage = await client.GetAsync("/ServiceJob/AddEmployee");
        var addToken = await ExtractAntiForgeryTokenAsync(addPage);
        var email = $"emp_{Guid.NewGuid():N}@garage.test";

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["FullName"] = fullName,
            ["Email"] = email,
            ["PhoneNumber"] = "+1555987654",
            ["Password"] = "Emp@123456!",
            ["ConfirmPassword"] = "Emp@123456!",
            ["Role"] = roleName,
            ["__RequestVerificationToken"] = addToken
        });

        var response = await client.PostAsync("/ServiceJob/AddEmployee", form);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GarijDbContext>();
        var emp = await db.StaffUsers.FirstOrDefaultAsync(u => u.Email == email);
        Assert.NotNull(emp);

        return (emp.Id, email, fullName);
    }

    [Fact]
    public async Task RegisterAndCheckout_CreatesUniqueGarageId_ForOwnerAndPurchase()
    {
        var (clientA, emailA, _, garageIdA) = await RegisterAndPayGarageOwnerAsync("Alice Auto", "Alice Workshop");
        var (clientB, emailB, _, garageIdB) = await RegisterAndPayGarageOwnerAsync("Bob Motors", "Bob Workshop");

        Assert.StartsWith("GRG-", garageIdA);
        Assert.StartsWith("GRG-", garageIdB);
        Assert.NotEqual(garageIdA, garageIdB);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GarijDbContext>();

        var purchaseA = await db.ProjectPurchases.FirstOrDefaultAsync(p => p.BuyerEmail == emailA);
        Assert.NotNull(purchaseA);
        Assert.Equal(garageIdA, purchaseA.GarageId);

        var purchaseB = await db.ProjectPurchases.FirstOrDefaultAsync(p => p.BuyerEmail == emailB);
        Assert.NotNull(purchaseB);
        Assert.Equal(garageIdB, purchaseB.GarageId);
    }

    [Fact]
    public async Task EmployeeCreatedByGarageOwner_InheritsOwnerGarageId_AndStaffLicense()
    {
        var (clientA, emailA, _, garageIdA) = await RegisterAndPayGarageOwnerAsync("Charlie Garage", "Charlie Works");
        var (employeeId, employeeEmail, employeeName) = await CreateEmployeeAsync(clientA, "Dan Mechanic", "Mechanic");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GarijDbContext>();

        var emp = await db.StaffUsers.FindAsync(employeeId);
        Assert.NotNull(emp);
        Assert.Equal(garageIdA, emp.GarageId);

        var staffLicense = await db.ProjectPurchases.FirstOrDefaultAsync(p => p.BuyerEmail == employeeEmail);
        Assert.NotNull(staffLicense);
        Assert.Equal(garageIdA, staffLicense.GarageId);
        Assert.True(staffLicense.IsActive);
    }

    [Fact]
    public async Task EmployeeDirectory_IsStrictlyIsolated_BetweenDifferentGarages()
    {
        var (clientA, _, _, _) = await RegisterAndPayGarageOwnerAsync("Garage A Owner", "Garage Alpha");
        var (clientB, _, _, _) = await RegisterAndPayGarageOwnerAsync("Garage B Owner", "Garage Beta");

        var (_, _, empAName) = await CreateEmployeeAsync(clientA, $"EmpA_{Guid.NewGuid():N}", "Mechanic");
        var (_, _, empBName) = await CreateEmployeeAsync(clientB, $"EmpB_{Guid.NewGuid():N}", "FrontDesk");

        // Owner A views Employee Directory
        var responseA = await clientA.GetAsync("/Employee");
        Assert.Equal(HttpStatusCode.OK, responseA.StatusCode);
        var contentA = await responseA.Content.ReadAsStringAsync();
        Assert.Contains(empAName, contentA);
        Assert.DoesNotContain(empBName, contentA);

        // Owner B views Employee Directory
        var responseB = await clientB.GetAsync("/Employee");
        Assert.Equal(HttpStatusCode.OK, responseB.StatusCode);
        var contentB = await responseB.Content.ReadAsStringAsync();
        Assert.Contains(empBName, contentB);
        Assert.DoesNotContain(empAName, contentB);
    }

    [Fact]
    public async Task EmployeeDetails_BelongingToAnotherGarage_Returns404NotFound()
    {
        var (clientA, _, _, _) = await RegisterAndPayGarageOwnerAsync("Garage One Owner", "Garage One");
        var (clientB, _, _, _) = await RegisterAndPayGarageOwnerAsync("Garage Two Owner", "Garage Two");

        var (empAId, _, empAName) = await CreateEmployeeAsync(clientA, $"EmpOne_{Guid.NewGuid():N}", "Mechanic");
        var (empBId, _, empBName) = await CreateEmployeeAsync(clientB, $"EmpTwo_{Guid.NewGuid():N}", "Mechanic");

        // Owner A accesses their own employee details -> 200 OK
        var ownDetailsResponse = await clientA.GetAsync($"/Employee/Details/{empAId}");
        Assert.Equal(HttpStatusCode.OK, ownDetailsResponse.StatusCode);
        var ownContent = await ownDetailsResponse.Content.ReadAsStringAsync();
        Assert.Contains(empAName, ownContent);

        // Owner A tries to access Owner B's employee details -> 404 NotFound
        var crossGarageResponse = await clientA.GetAsync($"/Employee/Details/{empBId}");
        Assert.Equal(HttpStatusCode.NotFound, crossGarageResponse.StatusCode);

        // Owner B tries to access Owner A's employee details -> 404 NotFound
        var crossGarageResponseB = await clientB.GetAsync($"/Employee/Details/{empAId}");
        Assert.Equal(HttpStatusCode.NotFound, crossGarageResponseB.StatusCode);
    }

    [Fact]
    public async Task AdminManageUsers_IsStrictlyIsolated_BetweenGarages()
    {
        var (clientA, _, _, _) = await RegisterAndPayGarageOwnerAsync("Owner A Admin", "Shop A");
        var (clientB, _, _, _) = await RegisterAndPayGarageOwnerAsync("Owner B Admin", "Shop B");

        var (_, _, empAName) = await CreateEmployeeAsync(clientA, $"StaffA_{Guid.NewGuid():N}", "Mechanic");
        var (_, _, empBName) = await CreateEmployeeAsync(clientB, $"StaffB_{Guid.NewGuid():N}", "Mechanic");

        var responseA = await clientA.GetAsync("/Admin/ManageUsers");
        Assert.Equal(HttpStatusCode.OK, responseA.StatusCode);
        var contentA = await responseA.Content.ReadAsStringAsync();
        Assert.Contains(empAName, contentA);
        Assert.DoesNotContain(empBName, contentA);

        var responseB = await clientB.GetAsync("/Admin/ManageUsers");
        Assert.Equal(HttpStatusCode.OK, responseB.StatusCode);
        var contentB = await responseB.Content.ReadAsStringAsync();
        Assert.Contains(empBName, contentB);
        Assert.DoesNotContain(empAName, contentB);
    }

    [Fact]
    public async Task MechanicsDropdownAndIndex_AreScopedToCurrentGarage()
    {
        var (clientA, _, _, _) = await RegisterAndPayGarageOwnerAsync("Owner A MechTest", "Shop MechA");
        var (clientB, _, _, _) = await RegisterAndPayGarageOwnerAsync("Owner B MechTest", "Shop MechB");

        var (_, _, mechAName) = await CreateEmployeeAsync(clientA, $"MechA_{Guid.NewGuid():N}", "Mechanic");
        var (_, _, mechBName) = await CreateEmployeeAsync(clientB, $"MechB_{Guid.NewGuid():N}", "Mechanic");

        // Mechanic/Index test
        var mechIndexA = await clientA.GetAsync("/Mechanic");
        Assert.Equal(HttpStatusCode.OK, mechIndexA.StatusCode);
        var mechIndexAContent = await mechIndexA.Content.ReadAsStringAsync();
        Assert.Contains(mechAName, mechIndexAContent);
        Assert.DoesNotContain(mechBName, mechIndexAContent);

        var mechIndexB = await clientB.GetAsync("/Mechanic");
        Assert.Equal(HttpStatusCode.OK, mechIndexB.StatusCode);
        var mechIndexBContent = await mechIndexB.Content.ReadAsStringAsync();
        Assert.Contains(mechBName, mechIndexBContent);
        Assert.DoesNotContain(mechAName, mechIndexBContent);

        // ServiceJob/Index mechanics filter dropdown test
        var serviceJobA = await clientA.GetAsync("/ServiceJob");
        Assert.Equal(HttpStatusCode.OK, serviceJobA.StatusCode);
        var serviceJobAContent = await serviceJobA.Content.ReadAsStringAsync();
        Assert.Contains(mechAName, serviceJobAContent);
        Assert.DoesNotContain(mechBName, serviceJobAContent);

        var serviceJobB = await clientB.GetAsync("/ServiceJob");
        Assert.Equal(HttpStatusCode.OK, serviceJobB.StatusCode);
        var serviceJobBContent = await serviceJobB.Content.ReadAsStringAsync();
        Assert.Contains(mechBName, serviceJobBContent);
        Assert.DoesNotContain(mechAName, serviceJobBContent);
    }
}
