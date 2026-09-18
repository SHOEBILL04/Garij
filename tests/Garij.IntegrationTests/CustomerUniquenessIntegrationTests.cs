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
/// Covers defect D-10 (Report 05) through the real customer forms: registering or editing a
/// customer onto an e-mail or phone number already on file in the garage used to be accepted.
///
/// Before this fix the Create and Edit actions did not catch the service's validation error at all,
/// so these also prove the refusal comes back on the form, under the field it concerns, rather than
/// as an error page.
///
/// Covers TC-CUS-09.
/// </summary>
public class CustomerUniquenessIntegrationTests : IClassFixture<AuthorizationTestFactory>
{
    private const string GarageId = "default-garij-master";
    private const string EmailTakenMessage = "A customer with this e-mail address already exists.";
    private const string PhoneTakenMessage = "A customer with this phone number already exists.";

    private static readonly Regex AntiForgeryTokenPattern = new(
        "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
        RegexOptions.Compiled);

    private readonly AuthorizationTestFactory _factory;

    public CustomerUniquenessIntegrationTests(AuthorizationTestFactory factory)
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

        var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "admin@garij.com",
            ["Password"] = "Admin@12345",
            ["__RequestVerificationToken"] = token,
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        return client;
    }

    private T WithDb<T>(Func<GarijDbContext, T> work)
    {
        using var scope = _factory.Services.CreateScope();
        return work(scope.ServiceProvider.GetRequiredService<GarijDbContext>());
    }

    /// <summary>Unique e-mail and phone for one test, so tests sharing the class database never collide.</summary>
    private static (string Email, string Phone) UniqueContact()
    {
        var digits = Random.Shared.NextInt64(10_000_000, 99_999_999);
        return ($"cus-{Guid.NewGuid():N}@example.com", $"+88017{digits}");
    }

    private int SeedCustomer(string email, string phone, string garageId = GarageId) => WithDb(db =>
    {
        var customer = new Customer
        {
            FullName = "Existing Customer",
            Email = email,
            PhoneNumber = phone,
            Address = "Dhaka",
            CreatedAt = DateTime.UtcNow,
            GarageId = garageId
        };
        db.Customers.Add(customer);
        db.SaveChanges();
        return customer.Id;
    });

    private int CountByEmail(string email) =>
        WithDb(db => db.Customers.AsNoTracking().Count(c => c.Email == email));

    private async Task<HttpResponseMessage> PostCreateAsync(HttpClient client, string email, string phone)
    {
        var page = await client.GetAsync("/Customer/Create");
        var token = await ExtractAntiForgeryTokenAsync(page);
        Assert.False(string.IsNullOrEmpty(token), "Could not find __RequestVerificationToken on the customer form.");

        return await client.PostAsync("/Customer/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["FullName"] = "Registered Through Form",
            ["PhoneNumber"] = phone,
            ["Email"] = email,
            ["Address"] = "Khulna",
            ["__RequestVerificationToken"] = token,
        }));
    }

    private async Task<HttpResponseMessage> PostEditAsync(HttpClient client, int id, string fullName, string email, string phone)
    {
        var page = await client.GetAsync($"/Customer/Edit/{id}");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var token = await ExtractAntiForgeryTokenAsync(page);

        return await client.PostAsync($"/Customer/Edit/{id}", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Id"] = id.ToString(),
            ["FullName"] = fullName,
            ["PhoneNumber"] = phone,
            ["Email"] = email,
            ["Address"] = "Khulna",
            ["__RequestVerificationToken"] = token,
        }));
    }

    /// <summary>The message rendered in the validation span for one specific input.</summary>
    private static void AssertFieldError(string html, string field, string message) =>
        Assert.Matches($"data-valmsg-for=\"{field}\"[^>]*>{Regex.Escape(message)}<", html);

    [Fact]
    public async Task RegisteringACustomer_WithUniqueDetails_Succeeds()
    {
        var client = await LoginAsAdminAsync();
        var (email, phone) = UniqueContact();

        var response = await PostCreateAsync(client, email, phone);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(1, CountByEmail(email));
    }

    [Fact]
    public async Task RegisteringACustomer_WithAnEmailAlreadyOnFile_IsRejectedUnderTheEmailField()
    {
        // TC-CUS-09.
        var client = await LoginAsAdminAsync();
        var (email, phone) = UniqueContact();
        SeedCustomer(email, phone);
        var (_, otherPhone) = UniqueContact();

        var response = await PostCreateAsync(client, email, otherPhone);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertFieldError(html, "Email", EmailTakenMessage);
        Assert.DoesNotContain(PhoneTakenMessage, html);
        Assert.Equal(1, CountByEmail(email));
    }

    [Fact]
    public async Task RegisteringACustomer_WithAPhoneAlreadyOnFile_IsRejectedUnderThePhoneField()
    {
        var client = await LoginAsAdminAsync();
        var (email, phone) = UniqueContact();
        SeedCustomer(email, phone);
        var (otherEmail, _) = UniqueContact();

        var response = await PostCreateAsync(client, otherEmail, phone);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertFieldError(html, "PhoneNumber", PhoneTakenMessage);
        Assert.Equal(0, CountByEmail(otherEmail));
    }

    [Fact]
    public async Task RegisteringACustomer_WhoseDetailsBelongToAnotherGaragesCustomer_Succeeds()
    {
        var client = await LoginAsAdminAsync();
        var (email, phone) = UniqueContact();
        SeedCustomer(email, phone, garageId: "some-other-garage");

        var response = await PostCreateAsync(client, email, phone);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(2, CountByEmail(email));
    }

    [Fact]
    public async Task EditingACustomer_WithoutChangingEmailOrPhone_StillSaves()
    {
        var client = await LoginAsAdminAsync();
        var (email, phone) = UniqueContact();
        var id = SeedCustomer(email, phone);

        var response = await PostEditAsync(client, id, "Renamed Customer", email, phone);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("Renamed Customer", WithDb(db => db.Customers.AsNoTracking().Single(c => c.Id == id).FullName));
    }

    [Fact]
    public async Task EditingACustomer_OntoAnotherCustomersEmailAndPhone_IsRejectedUnderBothFields()
    {
        var client = await LoginAsAdminAsync();
        var (takenEmail, takenPhone) = UniqueContact();
        SeedCustomer(takenEmail, takenPhone);
        var (myEmail, myPhone) = UniqueContact();
        var myId = SeedCustomer(myEmail, myPhone);

        var response = await PostEditAsync(client, myId, "Existing Customer", takenEmail, takenPhone);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertFieldError(html, "Email", EmailTakenMessage);
        AssertFieldError(html, "PhoneNumber", PhoneTakenMessage);

        var stored = WithDb(db => db.Customers.AsNoTracking().Single(c => c.Id == myId));
        Assert.Equal(myEmail, stored.Email);
        Assert.Equal(myPhone, stored.PhoneNumber);
    }
}
