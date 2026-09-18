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
/// TC-BIL-08 at the HTTP level. The defect was that no screen existed to attach a
/// catalogue service to a job, leaving the ten seeded services unreachable from the UI.
/// These tests drive the real app over HTTP: the screen must answer, list the seeded
/// catalogue, and a post to it must write a labour line onto the job.
/// </summary>
public class LabourIntakeRouteTests : IClassFixture<AuthorizationTestFactory>
{
    private static readonly Regex AntiForgeryTokenPattern = new(
        "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
        RegexOptions.Compiled);

    private readonly AuthorizationTestFactory _factory;

    public LabourIntakeRouteTests(AuthorizationTestFactory factory)
    {
        _factory = factory;
    }

    private static async Task LoginAsFrontDeskAsync(HttpClient client)
    {
        var loginPage = await client.GetAsync("/Account/Login");
        var token = AntiForgeryTokenPattern.Match(await loginPage.Content.ReadAsStringAsync()).Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(token), "Could not find __RequestVerificationToken on the login page.");

        var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "frontdesk@garij.com",
            ["Password"] = "Staff@12345",
            ["__RequestVerificationToken"] = token,
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    /// <summary>Picks a seeded job that is not already finished, so labour may still be attached.</summary>
    private async Task<int> GetOpenSeededJobIdAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<GarijDbContext>();

        return await context.ServiceJobs
            .Where(j => j.Status != JobStatus.Completed && j.Status != JobStatus.Cancelled)
            .Select(j => j.Id)
            .FirstAsync();
    }

    [Fact]
    public async Task LogServiceScreen_Exists_AndListsTheSeededCatalogue()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await LoginAsFrontDeskAsync(client);
        var jobId = await GetOpenSeededJobIdAsync();

        var response = await client.GetAsync($"/ServiceJob/LogService?serviceJobId={jobId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Log Service", html);
        // A seeded catalogue service must be selectable - these were unreachable before D-03.
        Assert.Contains("Engine Computer Diagnostic", html);
    }

    [Fact]
    public async Task JobDetailScreen_OffersTheLogServiceAction()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await LoginAsFrontDeskAsync(client);
        var jobId = await GetOpenSeededJobIdAsync();

        var response = await client.GetAsync($"/ServiceJob/Details/{jobId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains($"/ServiceJob/LogService?serviceJobId={jobId}", html);
        Assert.Contains("Services &amp; Labour", html);
    }

    [Fact]
    public async Task PostingTheLogServiceForm_AttachesLabourToTheJob()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await LoginAsFrontDeskAsync(client);
        var jobId = await GetOpenSeededJobIdAsync();

        int catalogueId;
        decimal basePrice;
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<GarijDbContext>();
            var catalogService = await context.ServiceCatalogs.FirstAsync(c => c.Name == "Wheel Alignment");
            catalogueId = catalogService.Id;
            basePrice = catalogService.BasePrice;
        }

        var formPage = await client.GetAsync($"/ServiceJob/LogService?serviceJobId={jobId}");
        var token = AntiForgeryTokenPattern.Match(await formPage.Content.ReadAsStringAsync()).Groups[1].Value;

        // Act
        var response = await client.PostAsync("/ServiceJob/LogService", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["ServiceJobId"] = jobId.ToString(),
            ["ServiceCatalogId"] = catalogueId.ToString(),
            ["Quantity"] = "2",
            ["__RequestVerificationToken"] = token,
        }));

        // Assert: redirected back to the job, with the labour line written and priced.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains($"/ServiceJob/Details/{jobId}", response.Headers.Location!.ToString());

        using var verifyScope = _factory.Services.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<GarijDbContext>();
        var line = await verifyContext.JobServiceDetails
            .SingleAsync(d => d.ServiceJobId == jobId && d.ServiceCatalogId == catalogueId);

        Assert.Equal(2, line.Quantity);
        Assert.Equal(basePrice, line.PriceAtBooking);
    }
}
