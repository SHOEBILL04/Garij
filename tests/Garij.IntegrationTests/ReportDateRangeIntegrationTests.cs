using System.Net;
using System.Text.RegularExpressions;
using Garij.Domain.Entities;
using Garij.Domain.Enums;
using Garij.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Garij.IntegrationTests;

/// <summary>
/// Covers defect D-11 (Report 05) through the running app. A reversed range (start after end) used to
/// be swapped silently on Revenue and Part Consumption, and not swapped at all on Mechanic Workload,
/// which then showed all-zero rows. Every date-driven report now swaps it and says so on the page.
///
/// Each test seeds data inside its own 2019 window - well clear of the demo data, which is dated
/// relative to today - so "the report ran with the corrected range" is checked against real figures
/// on the page rather than inferred from the message alone.
///
/// Covers TC-RPT-05, TC-RPT-06 and TC-RPT-07.
/// </summary>
public class ReportDateRangeIntegrationTests : IClassFixture<AuthorizationTestFactory>
{
    private const string GarageId = "default-garij-master";
    private const string CorrectionBanner = "id=\"date-range-corrected\"";

    private static readonly Regex AntiForgeryTokenPattern = new(
        "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
        RegexOptions.Compiled);

    private readonly AuthorizationTestFactory _factory;

    public ReportDateRangeIntegrationTests(AuthorizationTestFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> LoginAsAdminAsync()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var loginPage = await client.GetAsync("/Account/Login");
        var token = AntiForgeryTokenPattern.Match(await loginPage.Content.ReadAsStringAsync()).Groups[1].Value;

        var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "admin@garij.com",
            ["Password"] = "Admin@12345",
            ["__RequestVerificationToken"] = token,
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        return client;
    }

    private static async Task<string> GetPageAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static string Suffix() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    /// <summary>A completed job in the garage, with its customer and vehicle, finished on the given date.</summary>
    private static ServiceJob NewCompletedJob(DateTime completedAt)
    {
        var suffix = Suffix();
        var customer = new Customer
        {
            FullName = "Date Range Customer",
            Email = $"dr-{suffix}@test.local",
            PhoneNumber = $"+88017{Random.Shared.Next(10_000_000, 99_999_999)}",
            Address = "Dhaka",
            CreatedAt = completedAt,
            GarageId = GarageId
        };

        return new ServiceJob
        {
            Customer = customer,
            Vehicle = new Vehicle
            {
                Customer = customer,
                LicensePlateNumber = $"DR-{suffix}",
                Make = "Toyota",
                Model = "Allion",
                Year = 2018,
                Vin = $"VIN-DR-{suffix}",
                Color = "Silver",
                GarageId = GarageId
            },
            BookingReference = $"DR-{suffix}",
            JobType = JobType.RoutineService,
            Status = JobStatus.Completed,
            CreatedAt = completedAt,
            CompletedAt = completedAt,
            GarageId = GarageId
        };
    }

    private void Seed(Action<GarijDbContext> add)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GarijDbContext>();
        add(db);
        db.SaveChanges();
    }

    /// <summary>An invoice issued and fully paid on the given date.</summary>
    private void SeedPaidInvoice(DateTime date, decimal amount) => Seed(db =>
    {
        var invoice = new Invoice
        {
            ServiceJob = NewCompletedJob(date),
            InvoiceNumber = $"INV-DR-{Suffix()}",
            SubTotal = amount,
            TaxAmount = 0m,
            TotalAmount = amount,
            PaymentStatus = PaymentStatus.Paid,
            IssuedAt = date,
            GarageId = GarageId
        };
        invoice.PaymentTransactions.Add(new PaymentTransaction { Amount = amount, PaymentMethod = PaymentMethod.Cash, PaidAt = date });
        db.Invoices.Add(invoice);
    });

    /// <summary>A part used on a job completed on the given date; returns the part number.</summary>
    private string SeedPartUsage(DateTime date)
    {
        var partNumber = $"DR-PART-{Suffix()}";
        Seed(db =>
        {
            var job = NewCompletedJob(date);
            job.JobPartsUsed.Add(new JobPartUsed
            {
                ServiceJob = job,
                Part = new Part { Name = $"Range Part {partNumber}", PartNumber = partNumber, UnitPrice = 10m, QuantityInStock = 50, ReorderLevel = 5, GarageId = GarageId },
                QuantityUsed = 3,
                PriceAtUsage = 10m
            });
            db.ServiceJobs.Add(job);
        });
        return partNumber;
    }

    /// <summary>A mechanic who led one job, assigned on the given date; returns the mechanic's name.</summary>
    private string SeedMechanicAssignment(DateTime assignedAt)
    {
        var name = $"Range Mechanic {Suffix()}";
        var email = $"{Suffix().ToLowerInvariant()}@mechanic.test";
        Seed(db =>
        {
            // StaffUsers.IdentityUserId is a foreign key to AspNetUsers, so the login account comes first.
            var identityUser = new IdentityUser { Id = Guid.NewGuid().ToString(), UserName = email, Email = email };
            db.Users.Add(identityUser);

            var mechanic = new User
            {
                IdentityUserId = identityUser.Id,
                FullName = name,
                Email = email,
                PhoneNumber = "+8801711000021",
                Role = UserRole.Mechanic,
                CreatedAt = assignedAt,
                GarageId = GarageId
            };
            var job = NewCompletedJob(assignedAt);
            job.MechanicAssignments.Add(new MechanicAssignment { ServiceJob = job, User = mechanic, RoleInJob = RoleInJob.Lead, AssignedAt = assignedAt });
            db.ServiceJobs.Add(job);
        });
        return name;
    }

    private static string CollectedOnPage(string html) =>
        Regex.Match(html, "id=\"total-collected\">[^0-9]*([0-9.,]+)<").Groups[1].Value;

    /// <summary>The "Completed (Lead)" figure in the named mechanic's workload row.</summary>
    private static string LeadCompletedFor(string html, string mechanicName)
    {
        var row = Regex.Match(html,
            $"{Regex.Escape(mechanicName)}\\s*</td>\\s*<td[^>]*>(\\d+)</td>\\s*<td[^>]*>(\\d+)</td>\\s*<td[^>]*>(\\d+)</td>");
        Assert.True(row.Success, $"No workload row for {mechanicName}.");
        return row.Groups[3].Value;
    }

    private static void AssertCorrectionShown(string html, string enteredStart, string enteredEnd)
    {
        Assert.Contains(CorrectionBanner, html);
        Assert.Contains(
            $"The start date ({enteredStart}) is after the end date ({enteredEnd}), so the two dates were swapped and this report was run from {enteredEnd} to {enteredStart}.",
            html);
    }

    private static void AssertFilterShowsRange(string html, string start, string end)
    {
        Assert.Contains($"name=\"start\" value=\"{start}\"", html);
        Assert.Contains($"name=\"end\" value=\"{end}\"", html);
    }

    // =====================================================================================
    // (a) Revenue.
    // =====================================================================================

    [Fact]
    public async Task Revenue_WithAReversedRange_SaysSo_AndRunsWithTheCorrectedRange()
    {
        var client = await LoginAsAdminAsync();
        SeedPaidInvoice(new DateTime(2019, 3, 10), 432.10m);

        var html = await GetPageAsync(client, "/Report/Revenue?start=2019-03-15&end=2019-03-01");

        AssertCorrectionShown(html, "15 Mar 2019", "1 Mar 2019");
        Assert.Equal(432.10m.ToString("N2"), CollectedOnPage(html));
        Assert.Contains(new DateTime(2019, 3, 1).ToShortDateString(), html);
        AssertFilterShowsRange(html, "2019-03-01", "2019-03-15");
    }

    // =====================================================================================
    // (b) Part Consumption.
    // =====================================================================================

    [Fact]
    public async Task PartConsumption_WithAReversedRange_SaysSo_AndRunsWithTheCorrectedRange()
    {
        var client = await LoginAsAdminAsync();
        var partNumber = SeedPartUsage(new DateTime(2019, 4, 10));

        var html = await GetPageAsync(client, "/Report/PartConsumption?start=2019-04-15&end=2019-04-01");

        AssertCorrectionShown(html, "15 Apr 2019", "1 Apr 2019");
        Assert.Contains(partNumber, html);
        AssertFilterShowsRange(html, "2019-04-01", "2019-04-15");
    }

    // =====================================================================================
    // (c) Mechanic Workload - TC-RPT-06.
    // =====================================================================================

    [Fact]
    public async Task MechanicWorkload_WithAReversedRange_SaysSo_AndCountsTheWorkInTheCorrectedRange()
    {
        var client = await LoginAsAdminAsync();
        var mechanic = SeedMechanicAssignment(new DateTime(2019, 5, 10));

        var html = await GetPageAsync(client, "/Report/MechanicWorkload?start=2019-05-15&end=2019-05-01");

        AssertCorrectionShown(html, "15 May 2019", "1 May 2019");
        // Previously 0: the unswapped range matched no assignment at all.
        Assert.Equal("1", LeadCompletedFor(html, mechanic));
        AssertFilterShowsRange(html, "2019-05-01", "2019-05-15");
    }

    // =====================================================================================
    // (d) An in-order range, or no range, runs without any message.
    // =====================================================================================

    [Fact]
    public async Task EveryDateDrivenReport_WithAnInOrderRange_ShowsNoMessage()
    {
        var client = await LoginAsAdminAsync();
        SeedPaidInvoice(new DateTime(2019, 6, 10), 55.50m);
        var partNumber = SeedPartUsage(new DateTime(2019, 7, 10));
        var mechanic = SeedMechanicAssignment(new DateTime(2019, 8, 10));

        var revenue = await GetPageAsync(client, "/Report/Revenue?start=2019-06-01&end=2019-06-15");
        var parts = await GetPageAsync(client, "/Report/PartConsumption?start=2019-07-01&end=2019-07-15");
        var workload = await GetPageAsync(client, "/Report/MechanicWorkload?start=2019-08-01&end=2019-08-15");

        Assert.DoesNotContain(CorrectionBanner, revenue);
        Assert.DoesNotContain(CorrectionBanner, parts);
        Assert.DoesNotContain(CorrectionBanner, workload);

        // And each still ran with the range as given.
        Assert.Equal(55.50m.ToString("N2"), CollectedOnPage(revenue));
        Assert.Contains(partNumber, parts);
        Assert.Equal("1", LeadCompletedFor(workload, mechanic));
    }

    [Theory]
    [InlineData("/Report/Revenue")]
    [InlineData("/Report/PartConsumption")]
    [InlineData("/Report/MechanicWorkload")]
    public async Task EveryDateDrivenReport_WithNoDates_ShowsNoMessage(string url)
    {
        var client = await LoginAsAdminAsync();

        var html = await GetPageAsync(client, url);

        Assert.DoesNotContain(CorrectionBanner, html);
    }

    [Fact]
    public async Task LowStockReport_HasNoDateFilter()
    {
        var client = await LoginAsAdminAsync();

        var html = await GetPageAsync(client, "/Report/LowStock?start=2019-03-15&end=2019-03-01");

        Assert.DoesNotContain("id=\"dateFilterForm\"", html);
        Assert.DoesNotContain(CorrectionBanner, html);
    }

    // =====================================================================================
    // (e) TC-RPT-07: an unparseable date is still ignored, with the default range and no error.
    // =====================================================================================

    [Theory]
    [InlineData("/Report/Revenue?start=banana")]
    [InlineData("/Report/Revenue?start=banana&end=banana")]
    [InlineData("/Report/PartConsumption?start=banana")]
    [InlineData("/Report/MechanicWorkload?end=banana")]
    public async Task EveryDateDrivenReport_WithAnUnparseableDate_IgnoresIt_WithoutAnyMessage(string url)
    {
        var client = await LoginAsAdminAsync();

        var html = await GetPageAsync(client, url);

        Assert.DoesNotContain(CorrectionBanner, html);
        // The filter still echoes what was typed, exactly as before.
        Assert.Contains("value=\"banana\"", html);
    }

    [Fact]
    public async Task Revenue_WithAnUnparseableStartDate_UsesTheDefaultRange()
    {
        var client = await LoginAsAdminAsync();

        var html = await GetPageAsync(client, "/Report/Revenue?start=banana");

        // The print header states the period the report ran for: 1 January this year to today.
        Assert.Contains($"Period: {new DateTime(DateTime.UtcNow.Year, 1, 1).ToShortDateString()}", html);
        Assert.Contains(DateTime.UtcNow.Date.ToShortDateString(), html);
    }
}
