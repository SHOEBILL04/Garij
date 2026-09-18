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
/// Covers defect D-09 (Report 05) through the running app, for both missing features:
/// the Low Stock report page (TC-RPT-08, TC-ACC-05) and refunding a recorded payment (TC-PAY-10).
/// </summary>
public class MissingFeaturesIntegrationTests : IClassFixture<AuthorizationTestFactory>
{
    private const string GarageId = "default-garij-master";

    private static readonly Regex AntiForgeryTokenPattern = new(
        "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
        RegexOptions.Compiled);

    /// <summary>A row the Parts Inventory screen badges as low stock, capturing its part number cell.</summary>
    private static readonly Regex BadgedInventoryRowPattern = new(
        "<tr class=\"row-warning\">\\s*<td[^>]*>[^<]*</td>\\s*<td class=\"font-monospace\">([^<]+)</td>",
        RegexOptions.Compiled);

    private static readonly Regex LowStockReportRowPattern = new(
        "<tr data-part-number=\"([^\"]+)\">",
        RegexOptions.Compiled);

    private readonly AuthorizationTestFactory _factory;

    public MissingFeaturesIntegrationTests(AuthorizationTestFactory factory)
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

    private static string Suffix() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    // =====================================================================================
    // Sub-task A: the Low Stock report.
    // =====================================================================================

    private string SeedPart(string prefix, int quantityInStock, int reorderLevel)
    {
        var partNumber = $"{prefix}-{Suffix()}";
        WithDb(db =>
        {
            db.Parts.Add(new Part
            {
                Name = $"Report Test {partNumber}",
                PartNumber = partNumber,
                UnitPrice = 9.99m,
                QuantityInStock = quantityInStock,
                ReorderLevel = reorderLevel,
                GarageId = GarageId
            });
            return db.SaveChanges();
        });
        return partNumber;
    }

    [Fact]
    public async Task LowStockReport_IsBuilt_AndListsTheSamePartsTheInventoryBadgeFlags()
    {
        var client = await LoginAsAdminAsync();
        var below = SeedPart("LSR-BELOW", quantityInStock: 1, reorderLevel: 6);
        var atLevel = SeedPart("LSR-EQUAL", quantityInStock: 4, reorderLevel: 4);
        var empty = SeedPart("LSR-EMPTY", quantityInStock: 0, reorderLevel: 2);
        var above = SeedPart("LSR-ABOVE", quantityInStock: 5, reorderLevel: 4);

        var reportResponse = await client.GetAsync("/Report/LowStock");
        var reportHtml = await reportResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, reportResponse.StatusCode);
        Assert.DoesNotContain("isn't built yet", reportHtml);

        var reported = LowStockReportRowPattern.Matches(reportHtml).Select(m => m.Groups[1].Value).ToHashSet();
        Assert.Contains(below, reported);
        Assert.Contains(atLevel, reported);
        Assert.Contains(empty, reported);
        Assert.DoesNotContain(above, reported);

        // The rendered report and the rendered inventory badges must name exactly the same parts.
        var inventoryHtml = await (await client.GetAsync("/Parts")).Content.ReadAsStringAsync();
        var badged = BadgedInventoryRowPattern.Matches(inventoryHtml).Select(m => m.Groups[1].Value).ToHashSet();

        Assert.NotEmpty(badged);
        Assert.True(badged.SetEquals(reported),
            $"Badged on inventory: [{string.Join(", ", badged.Order())}]; in report: [{string.Join(", ", reported.Order())}]");
    }

    [Fact]
    public async Task ReportsIndex_LinksToTheLowStockReport()
    {
        var client = await LoginAsAdminAsync();

        var html = await (await client.GetAsync("/Report")).Content.ReadAsStringAsync();

        Assert.Contains("href=\"/Report/LowStock\"", html);
    }

    // =====================================================================================
    // Sub-task B: refunding a payment.
    // =====================================================================================

    private int SeedUnpaidInvoice(decimal total)
    {
        var suffix = Suffix();
        return WithDb(db =>
        {
            var customer = new Customer
            {
                FullName = "Refund Flow Customer",
                Email = $"rff-{suffix}@test.local",
                PhoneNumber = "+8801711000020",
                Address = "Dhaka",
                CreatedAt = DateTime.UtcNow,
                GarageId = GarageId
            };

            var invoice = new Invoice
            {
                ServiceJob = new ServiceJob
                {
                    Customer = customer,
                    Vehicle = new Vehicle
                    {
                        Customer = customer,
                        LicensePlateNumber = $"RFF-{suffix}",
                        Make = "Toyota",
                        Model = "Allion",
                        Year = 2020,
                        Vin = $"VIN-RFF-{suffix}",
                        Color = "Silver",
                        GarageId = GarageId
                    },
                    BookingReference = $"RFF-{suffix}",
                    JobType = JobType.RoutineService,
                    Status = JobStatus.Completed,
                    CreatedAt = DateTime.UtcNow,
                    CompletedAt = DateTime.UtcNow,
                    GarageId = GarageId
                },
                InvoiceNumber = $"INV-RFF-{suffix}",
                SubTotal = total,
                TaxAmount = 0m,
                TotalAmount = total,
                PaymentStatus = PaymentStatus.Pending,
                IssuedAt = DateTime.UtcNow,
                GarageId = GarageId
            };

            db.Invoices.Add(invoice);
            db.SaveChanges();
            return invoice.Id;
        });
    }

    private async Task RecordPaymentThroughTheFormAsync(HttpClient client, int invoiceId, decimal amount, string reference)
    {
        var page = await client.GetAsync($"/Billing/RecordPayment?invoiceId={invoiceId}");
        var token = await ExtractAntiForgeryTokenAsync(page);

        var response = await client.PostAsync($"/Billing/RecordPayment?invoiceId={invoiceId}", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Payment.Amount"] = amount.ToString("0.00"),
            ["Payment.PaymentMethod"] = nameof(PaymentMethod.Card),
            ["Payment.TransactionReference"] = reference,
            ["__RequestVerificationToken"] = token,
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private int PaymentIdByReference(string reference) =>
        WithDb(db => db.PaymentTransactions.AsNoTracking().Single(p => p.TransactionReference == reference).Id);

    [Fact]
    public async Task RefundingAPayment_FromTheInvoicePage_ReopensTheBalanceAndMarksThePayment()
    {
        var client = await LoginAsAdminAsync();
        var invoiceId = SeedUnpaidInvoice(500m);
        var keptReference = $"KEEP-{Suffix()}";
        var refundReference = $"REFUND-{Suffix()}";
        await RecordPaymentThroughTheFormAsync(client, invoiceId, 200m, keptReference);
        await RecordPaymentThroughTheFormAsync(client, invoiceId, 300m, refundReference);
        var refundPaymentId = PaymentIdByReference(refundReference);

        // Refund access is on the invoice's payment history.
        var detailsBefore = await (await client.GetAsync($"/Billing/Details/{invoiceId}")).Content.ReadAsStringAsync();
        Assert.Contains($"/Billing/RefundPayment?invoiceId={invoiceId}&amp;paymentId={refundPaymentId}", detailsBefore);

        var confirmPage = await client.GetAsync($"/Billing/RefundPayment?invoiceId={invoiceId}&paymentId={refundPaymentId}");
        Assert.Equal(HttpStatusCode.OK, confirmPage.StatusCode);
        var token = await ExtractAntiForgeryTokenAsync(confirmPage);
        Assert.False(string.IsNullOrEmpty(token), "Could not find __RequestVerificationToken on the refund page.");

        var refundResponse = await client.PostAsync(
            $"/Billing/RefundPayment?invoiceId={invoiceId}&paymentId={refundPaymentId}",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));

        Assert.Equal(HttpStatusCode.Redirect, refundResponse.StatusCode);

        // Stored: the payment is kept and stamped, and the invoice is no longer fully paid.
        var (refundedAt, invoiceStatus) = WithDb(db => (
            db.PaymentTransactions.AsNoTracking().Single(p => p.Id == refundPaymentId).RefundedAt,
            db.Invoices.AsNoTracking().Single(i => i.Id == invoiceId).PaymentStatus));
        Assert.NotNull(refundedAt);
        Assert.Equal(PaymentStatus.PartiallyPaid, invoiceStatus);

        // Shown: the refunded payment is still listed, marked, and the balance has reopened.
        var detailsAfter = await (await client.GetAsync($"/Billing/Details/{invoiceId}")).Content.ReadAsStringAsync();

        var refundedRow = Regex.Match(detailsAfter, $"<tr data-payment-id=\"{refundPaymentId}\" data-payment-status=\"(\\w+)\">(.*?)</tr>", RegexOptions.Singleline);
        Assert.True(refundedRow.Success, "The refunded payment disappeared from the payment history.");
        Assert.Equal("refunded", refundedRow.Groups[1].Value);
        Assert.Contains(">Refunded</span>", refundedRow.Groups[2].Value);
        Assert.Contains(refundReference, refundedRow.Groups[2].Value);
        Assert.DoesNotContain("RefundPayment", refundedRow.Groups[2].Value);

        Assert.Contains($"data-payment-id=\"{PaymentIdByReference(keptReference)}\" data-payment-status=\"received\"", detailsAfter);
        // Currency symbol and separator follow the server culture; the digits do not.
        Assert.Matches("id=\"amount-refunded\">[^<]*300[.,]00<", detailsAfter);
        Assert.Contains($"/Billing/RecordPayment?invoiceId={invoiceId}", detailsAfter);
    }

    [Fact]
    public async Task RefundingAnAlreadyRefundedPayment_IsRefusedOnThePage()
    {
        var client = await LoginAsAdminAsync();
        var invoiceId = SeedUnpaidInvoice(80m);
        var reference = $"TWICE-{Suffix()}";
        await RecordPaymentThroughTheFormAsync(client, invoiceId, 80m, reference);
        var paymentId = PaymentIdByReference(reference);

        var page = await client.GetAsync($"/Billing/RefundPayment?invoiceId={invoiceId}&paymentId={paymentId}");
        var token = await ExtractAntiForgeryTokenAsync(page);
        var url = $"/Billing/RefundPayment?invoiceId={invoiceId}&paymentId={paymentId}";

        var first = await client.PostAsync(url, new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);

        var second = await client.PostAsync(url, new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        var body = await second.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Contains("already refunded", body);
    }

    [Fact]
    public async Task RevenueReport_ShowsTheRefundAsExcludedFromCollected()
    {
        var client = await LoginAsAdminAsync();
        var invoiceId = SeedUnpaidInvoice(123.45m);
        var reference = $"REV-{Suffix()}";
        await RecordPaymentThroughTheFormAsync(client, invoiceId, 123.45m, reference);
        var paymentId = PaymentIdByReference(reference);

        var page = await client.GetAsync($"/Billing/RefundPayment?invoiceId={invoiceId}&paymentId={paymentId}");
        var token = await ExtractAntiForgeryTokenAsync(page);
        await client.PostAsync(
            $"/Billing/RefundPayment?invoiceId={invoiceId}&paymentId={paymentId}",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));

        var today = DateTime.UtcNow.Date;
        var start = today.AddDays(-1);
        var end = today.AddDays(1);
        var html = await (await client.GetAsync($"/Report/Revenue?start={start:yyyy-MM-dd}&end={end:yyyy-MM-dd}")).Content.ReadAsStringAsync();

        Assert.Matches("id=\"refunded-payment-amount\">[^<]*123[.,]45<", html);
        Assert.Contains("excluded from collected", html);

        // Cash Collected is what was received in the period minus what was refunded. Other tests in
        // this class also take payments today, so the expected figure is read from the database.
        var rangeEnd = end.TimeOfDay == TimeSpan.Zero ? end.Date.AddDays(1).AddTicks(-1) : end;
        var expectedCollected = WithDb(db => db.PaymentTransactions.AsNoTracking()
            .Where(p => (p.Invoice.GarageId ?? "default-garij-master") == GarageId &&
                        p.PaidAt >= start && p.PaidAt <= rangeEnd &&
                        p.RefundedAt == null)
            .AsEnumerable()
            .Sum(p => p.Amount));
        var shownCollected = Regex.Match(html, "id=\"total-collected\">[^0-9]*([0-9.,]+)<").Groups[1].Value;

        Assert.Equal(expectedCollected.ToString("N2"), shownCollected);
    }
}
