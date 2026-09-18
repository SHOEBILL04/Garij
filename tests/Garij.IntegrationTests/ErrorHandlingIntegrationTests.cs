using System.Net;
using System.Text.RegularExpressions;
using Garij.Domain.Entities;
using Garij.Domain.Enums;
using Garij.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Garij.IntegrationTests;

/// <summary>
/// Covers defect D-08 (Report 05): error responses used to be issued as a redirect, so the
/// status code the handler had just calculated was replaced by a 200 on a single generic
/// page, and some refusals returned a bare status body with no site layout and no message.
///
/// These tests make real HTTP requests and assert on the status code of the response itself
/// (never following redirects) plus the presence of the site layout and a readable message
/// in the body, so a regression back to redirect-based error handling fails here.
///
/// Covers TC-EXC-01, TC-EXC-08, TC-BIL-06 and TC-NOT-04.
/// </summary>
public class ErrorHandlingIntegrationTests : IClassFixture<AuthorizationTestFactory>
{
    /// <summary>Markup rendered by _Layout on every normal page, used as the "site layout is present" probe.</summary>
    private const string LayoutMarker = "POWERED BY GARIJ";

    private static readonly Regex AntiForgeryTokenPattern = new(
        "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
        RegexOptions.Compiled);

    private readonly AuthorizationTestFactory _factory;

    public ErrorHandlingIntegrationTests(AuthorizationTestFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Redirects are never followed: the whole point of the defect is that the status code
    /// on the first response must be the real one, not a 302 leading to a 200.
    /// </summary>
    private HttpClient CreateNonRedirectingClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task<string> ExtractAntiForgeryTokenAsync(HttpResponseMessage response)
    {
        var html = await response.Content.ReadAsStringAsync();
        return AntiForgeryTokenPattern.Match(html).Groups[1].Value;
    }

    private async Task<HttpClient> LoginAsAdminAsync()
    {
        var client = CreateNonRedirectingClient();

        var loginPage = await client.GetAsync("/Account/Login");
        var token = await ExtractAntiForgeryTokenAsync(loginPage);
        Assert.False(string.IsNullOrEmpty(token), "Could not find __RequestVerificationToken on the login page.");

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

    // -------------------------------------------------------------------------------------
    // TC-EXC-01 / TC-EXC-08: a missing entity returns a real 404 on the styled error page.
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task RequestingNonexistentCustomer_ReturnsNotFoundStatus_NotARedirect()
    {
        var client = await LoginAsAdminAsync();

        var response = await client.GetAsync("/Customer/Details/99999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task RequestingNonexistentCustomer_RendersErrorPageInsideSiteLayout()
    {
        var client = await LoginAsAdminAsync();

        var response = await client.GetAsync("/Customer/Details/99999");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        // Site layout and its navigation, not the framework's bare status page.
        Assert.Contains(LayoutMarker, body);
        Assert.Contains("<nav", body);
        Assert.Contains("Status Lookup", body);

        // A readable, relevant message rather than an empty body.
        Assert.Contains("Page not found", body);
        Assert.Contains("could not find", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/Customer/Details/99999", body);
    }

    [Fact]
    public async Task ErrorPagesForDifferentFailures_KeepTheirOwnStatusCodes()
    {
        var client = await LoginAsAdminAsync();

        // A missing record and a malformed request must not collapse onto one generic response.
        var notFound = await client.GetAsync("/Customer/Details/99999");
        var badRequest = await client.GetAsync("/Billing/Create?serviceJobId=0");

        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badRequest.StatusCode);

        var notFoundBody = await notFound.Content.ReadAsStringAsync();
        var badRequestBody = await badRequest.Content.ReadAsStringAsync();

        Assert.Contains("HTTP 404", notFoundBody);
        Assert.Contains("HTTP 400", badRequestBody);
    }

    [Fact]
    public async Task UnmatchedRoute_ReturnsNotFoundStatus_WithSiteLayout()
    {
        var client = await LoginAsAdminAsync();

        var response = await client.GetAsync("/NoSuchPageExists/AtAll");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains(LayoutMarker, body);
        Assert.Contains("Page not found", body);
    }

    // -------------------------------------------------------------------------------------
    // TC-BIL-06: generating an invoice for job id 0.
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task GeneratingInvoiceForJobIdZero_ReturnsBadRequestWithMeaningfulMessage()
    {
        var client = await LoginAsAdminAsync();

        var response = await client.GetAsync("/Billing/Create?serviceJobId=0");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(body), "The 400 response body was empty.");

        Assert.Contains("not a valid service job number", body);
        Assert.Contains("Bad request", body);
        Assert.Contains(LayoutMarker, body);
    }

    [Fact]
    public async Task PostingInvoiceForJobIdZero_ReturnsBadRequestWithMeaningfulMessage()
    {
        var client = await LoginAsAdminAsync();

        // Token taken from a valid Create page, so the antiforgery check passes and the
        // refusal under test is the job id, not a missing token.
        var createPage = await client.GetAsync("/Billing/Create?serviceJobId=1");
        var token = await ExtractAntiForgeryTokenAsync(createPage);
        Assert.False(string.IsNullOrEmpty(token), "Could not find __RequestVerificationToken on the invoice form.");

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["serviceJobId"] = "0",
            ["__RequestVerificationToken"] = token,
        });

        var response = await client.PostAsync("/Billing/Create", form);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("not a valid service job number", body);
        Assert.Contains(LayoutMarker, body);
    }

    // -------------------------------------------------------------------------------------
    // TC-NOT-04: responding to a notification with a status other than Approved/Rejected.
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task RespondingWithInvalidNotificationStatus_ReturnsBadRequestWithMeaningfulMessage()
    {
        var client = await LoginAsAdminAsync();
        var notificationId = SeedPendingNotification();

        var respondPage = await client.GetAsync($"/Notification/Respond/{notificationId}");
        Assert.Equal(HttpStatusCode.OK, respondPage.StatusCode);
        var token = await ExtractAntiForgeryTokenAsync(respondPage);
        Assert.False(string.IsNullOrEmpty(token), "Could not find __RequestVerificationToken on the respond form.");

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            // Pending is a real NotificationStatus value but not a valid response to a request.
            ["status"] = nameof(NotificationStatus.Pending),
            ["__RequestVerificationToken"] = token,
        });

        var response = await client.PostAsync($"/Notification/Respond/{notificationId}", form);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(body), "The 400 response body was empty.");

        Assert.Contains("Status must be either Approved or Rejected.", body);
        Assert.Contains(LayoutMarker, body);
    }

    // -------------------------------------------------------------------------------------
    // TC-EXC-08: an exception raised inside an action keeps the status code the global
    // exception middleware calculated for it, instead of being turned into a redirect.
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task ExceptionFromAnAction_ReturnsItsCalculatedStatusCode_NotARedirect()
    {
        var client = await LoginAsAdminAsync();
        var notificationId = SeedPendingNotification();

        var respondPage = await client.GetAsync($"/Notification/Respond/{notificationId}");
        var token = await ExtractAntiForgeryTokenAsync(respondPage);

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            // A valid status against an id that does not exist, so the service raises
            // NotFoundException and the middleware - not the controller - handles it.
            ["status"] = nameof(NotificationStatus.Approved),
            ["__RequestVerificationToken"] = token,
        });

        var response = await client.PostAsync("/Notification/Respond/99999", form);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(response.Headers.Location);

        // The exception's own message reaches the page, inside the site layout.
        Assert.Contains("was not found", body);
        Assert.Contains("99999", body);
        Assert.Contains(LayoutMarker, body);
    }

    /// <summary>
    /// Seeds a pending notification in the seeded admin's garage so the respond form is
    /// reachable, and returns its id.
    /// </summary>
    private int SeedPendingNotification()
    {
        const string garageId = "default-garij-master";
        var reference = $"ERR-{Guid.NewGuid():N}"[..12];

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GarijDbContext>();

        var customer = new Customer
        {
            FullName = "Error Page Test Customer",
            Email = $"{reference}@test.local",
            PhoneNumber = "+8801711000010",
            Address = "Dhaka",
            CreatedAt = DateTime.UtcNow,
            GarageId = garageId
        };

        var vehicle = new Vehicle
        {
            Customer = customer,
            LicensePlateNumber = reference,
            Make = "Toyota",
            Model = "Allion",
            Year = 2020,
            Vin = $"VIN-{reference}",
            Color = "Silver",
            GarageId = garageId
        };

        var job = new ServiceJob
        {
            Customer = customer,
            Vehicle = vehicle,
            BookingReference = reference,
            JobType = JobType.RoutineService,
            Status = JobStatus.CustomerApprovalNeeded,
            CreatedAt = DateTime.UtcNow,
            GarageId = garageId
        };

        var notification = new Notification
        {
            ServiceJob = job,
            Message = "Additional repair work needs customer approval.",
            Status = NotificationStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            GarageId = garageId
        };

        db.Notifications.Add(notification);
        db.SaveChanges();

        return notification.Id;
    }
}
