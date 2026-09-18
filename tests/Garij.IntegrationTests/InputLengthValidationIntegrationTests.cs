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
/// Covers defect D-05 (Report 05) end to end: the four over-length fields are posted through
/// the real forms, and the assertions check both that the request is refused with a readable
/// message and - the part that actually matters - that nothing oversized reaches the database.
///
/// The test host runs on SQLite, which does not enforce text column length, so a value that
/// slips past application validation is stored silently. Asserting on the stored row is
/// therefore the only check that would have caught the original defect.
///
/// Covers TC-VEH-15, TC-JOB-05, TC-JOB-06, TC-JOB-07 and TC-PAY-09.
/// </summary>
public class InputLengthValidationIntegrationTests : IClassFixture<AuthorizationTestFactory>
{
    private const string GarageId = "default-garij-master";

    private static readonly Regex AntiForgeryTokenPattern = new(
        "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
        RegexOptions.Compiled);

    private readonly AuthorizationTestFactory _factory;

    public InputLengthValidationIntegrationTests(AuthorizationTestFactory factory)
    {
        _factory = factory;
    }

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

    private IServiceScope CreateScope() => _factory.Services.CreateScope();

    private static GarijDbContext Db(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<GarijDbContext>();

    private static string Suffix() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    // =====================================================================================
    // TC-VEH-15: vehicle colour, column length 50.
    // =====================================================================================

    private int SeedCustomer()
    {
        using var scope = CreateScope();
        var db = Db(scope);

        var customer = new Customer
        {
            FullName = "Length Test Customer",
            Email = $"len-{Suffix()}@test.local",
            PhoneNumber = "+8801711000011",
            Address = "Dhaka",
            CreatedAt = DateTime.UtcNow,
            GarageId = GarageId
        };

        db.Customers.Add(customer);
        db.SaveChanges();
        return customer.Id;
    }

    private async Task<HttpResponseMessage> PostVehicleAsync(HttpClient client, int customerId, string plate, string color)
    {
        var page = await client.GetAsync($"/Vehicle/Create?customerId={customerId}");
        var token = await ExtractAntiForgeryTokenAsync(page);
        Assert.False(string.IsNullOrEmpty(token), "Could not find __RequestVerificationToken on the vehicle form.");

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["CustomerId"] = customerId.ToString(),
            ["LicensePlateNumber"] = plate,
            ["Make"] = "Toyota",
            ["Model"] = "Allion",
            ["Year"] = "2020",
            ["Vin"] = $"VIN-{plate}",
            ["Color"] = color,
            ["__RequestVerificationToken"] = token,
        });

        return await client.PostAsync("/Vehicle/Create", form);
    }

    [Fact]
    public async Task VehicleColor_AtFiftyCharacters_IsAcceptedAndStored()
    {
        var client = await LoginAsAdminAsync();
        var customerId = SeedCustomer();
        var plate = $"AB-{Random.Shared.Next(1000, 9999)}";
        var color = new string('a', 50);

        var response = await PostVehicleAsync(client, customerId, plate, color);

        // A successful create redirects; a rejected one redisplays the form.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var scope = CreateScope();
        var stored = Db(scope).Vehicles.Single(v => v.LicensePlateNumber == plate);
        Assert.Equal(50, stored.Color.Length);
    }

    [Fact]
    public async Task VehicleColor_AtFiftyOneCharacters_IsRejectedAndNotStored()
    {
        var client = await LoginAsAdminAsync();
        var customerId = SeedCustomer();
        var plate = $"AC-{Random.Shared.Next(1000, 9999)}";

        var response = await PostVehicleAsync(client, customerId, plate, new string('a', 51));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Colour cannot exceed 50 characters.", body);

        using var scope = CreateScope();
        Assert.False(Db(scope).Vehicles.Any(v => v.LicensePlateNumber == plate),
            "A vehicle with an over-length colour reached the database.");
    }

    [Fact]
    public async Task VehicleColor_AtOneHundredCharacters_IsRejectedAndNotStored()
    {
        // The exact value from TC-VEH-15.
        var client = await LoginAsAdminAsync();
        var customerId = SeedCustomer();
        var plate = $"AD-{Random.Shared.Next(1000, 9999)}";

        var response = await PostVehicleAsync(client, customerId, plate, new string('a', 100));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Colour cannot exceed 50 characters.", body);

        using var scope = CreateScope();
        Assert.False(Db(scope).Vehicles.Any(v => v.LicensePlateNumber == plate));
    }

    // =====================================================================================
    // TC-JOB-05 / TC-JOB-06 / TC-JOB-07: booking reference (20) and diagnostic notes (2000).
    // =====================================================================================

    private int SeedVehicle()
    {
        using var scope = CreateScope();
        var db = Db(scope);
        var suffix = Suffix();

        var customer = new Customer
        {
            FullName = "Length Test Owner",
            Email = $"len-{suffix}@test.local",
            PhoneNumber = "+8801711000012",
            Address = "Dhaka",
            CreatedAt = DateTime.UtcNow,
            GarageId = GarageId
        };

        var vehicle = new Vehicle
        {
            Customer = customer,
            LicensePlateNumber = $"LN-{suffix}",
            Make = "Toyota",
            Model = "Allion",
            Year = 2020,
            Vin = $"VIN-{suffix}",
            Color = "Silver",
            GarageId = GarageId
        };

        db.Vehicles.Add(vehicle);
        db.SaveChanges();
        return vehicle.Id;
    }

    private async Task<HttpResponseMessage> PostServiceJobAsync(
        HttpClient client, int vehicleId, string bookingReference, string notes)
    {
        var page = await client.GetAsync($"/ServiceJob/Create?vehicleId={vehicleId}");
        var token = await ExtractAntiForgeryTokenAsync(page);
        Assert.False(string.IsNullOrEmpty(token), "Could not find __RequestVerificationToken on the service job form.");

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["VehicleId"] = vehicleId.ToString(),
            ["JobType"] = nameof(JobType.RoutineService),
            ["Status"] = nameof(JobStatus.Requested),
            ["BookingReference"] = bookingReference,
            ["DiagnosticNotes"] = notes,
            ["__RequestVerificationToken"] = token,
        });

        return await client.PostAsync("/ServiceJob/Create", form);
    }

    [Fact]
    public async Task BookingReference_AtTwentyCharacters_IsAcceptedAndStored()
    {
        var client = await LoginAsAdminAsync();
        var vehicleId = SeedVehicle();
        var reference = $"REF-{Suffix()}"[..12].PadRight(20, 'X');

        var response = await PostServiceJobAsync(client, vehicleId, reference, "Routine service.");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var scope = CreateScope();
        var stored = Db(scope).ServiceJobs.Single(j => j.VehicleId == vehicleId);
        Assert.Equal(20, stored.BookingReference.Length);
    }

    [Fact]
    public async Task BookingReference_AtTwentyOneCharacters_IsRejectedAndNotStored()
    {
        var client = await LoginAsAdminAsync();
        var vehicleId = SeedVehicle();

        var response = await PostServiceJobAsync(client, vehicleId, new string('R', 21), "Routine service.");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Booking reference cannot exceed 20 characters.", body);

        using var scope = CreateScope();
        Assert.False(Db(scope).ServiceJobs.Any(j => j.VehicleId == vehicleId),
            "A job with an over-length booking reference reached the database.");
    }

    [Fact]
    public async Task BookingReference_AtFiftyCharacters_IsRejectedAndNotStored()
    {
        // The exact value from TC-JOB-07.
        var client = await LoginAsAdminAsync();
        var vehicleId = SeedVehicle();

        var response = await PostServiceJobAsync(client, vehicleId, new string('R', 50), "Routine service.");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Booking reference cannot exceed 20 characters.", body);

        using var scope = CreateScope();
        Assert.False(Db(scope).ServiceJobs.Any(j => j.VehicleId == vehicleId));
    }

    [Fact]
    public async Task DiagnosticNotes_AtTwoThousandCharacters_IsAcceptedAndStored()
    {
        // TC-JOB-05: this passed before the change and must keep passing.
        var client = await LoginAsAdminAsync();
        var vehicleId = SeedVehicle();
        var notes = new string('n', 2000);

        var response = await PostServiceJobAsync(client, vehicleId, $"NOTE-{Suffix()}", notes);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var scope = CreateScope();
        var stored = Db(scope).ServiceJobs.Single(j => j.VehicleId == vehicleId);
        Assert.Equal(2000, stored.DiagnosticNotes!.Length);
    }

    [Fact]
    public async Task DiagnosticNotes_AtTwoThousandAndOneCharacters_IsRejectedAndNotStored()
    {
        var client = await LoginAsAdminAsync();
        var vehicleId = SeedVehicle();

        var response = await PostServiceJobAsync(client, vehicleId, $"NOTE-{Suffix()}", new string('n', 2001));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Diagnostic notes cannot exceed 2000 characters.", body);

        using var scope = CreateScope();
        Assert.False(Db(scope).ServiceJobs.Any(j => j.VehicleId == vehicleId),
            "A job with over-length diagnostic notes reached the database.");
    }

    [Fact]
    public async Task DiagnosticNotes_AtFiveThousandCharacters_IsRejectedAndNotStored()
    {
        // The exact value from TC-JOB-06.
        var client = await LoginAsAdminAsync();
        var vehicleId = SeedVehicle();

        var response = await PostServiceJobAsync(client, vehicleId, $"NOTE-{Suffix()}", new string('n', 5000));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Diagnostic notes cannot exceed 2000 characters.", body);

        using var scope = CreateScope();
        Assert.False(Db(scope).ServiceJobs.Any(j => j.VehicleId == vehicleId));
    }

    /// <summary>
    /// The mechanic job board posts notes as a bare action parameter rather than through
    /// ServiceJobDto, so it needs its own limit; without one it is a second route to the
    /// same defect.
    /// </summary>
    [Fact]
    public async Task DiagnosticNotesFromTheJobBoard_OverTheLimit_AreNotStored()
    {
        var client = await LoginAsAdminAsync();
        var jobId = SeedJobWithNotes("Original notes.");

        var board = await client.GetAsync("/Mechanic/JobBoard");
        var token = await ExtractAntiForgeryTokenAsync(board);
        Assert.False(string.IsNullOrEmpty(token), "Could not find __RequestVerificationToken on the job board.");

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["serviceJobId"] = jobId.ToString(),
            ["diagnosticNotes"] = new string('n', 5000),
            ["__RequestVerificationToken"] = token,
        });

        await client.PostAsync("/Mechanic/SaveNotes", form);

        using var scope = CreateScope();
        var stored = Db(scope).ServiceJobs.AsNoTracking().Single(j => j.Id == jobId);
        Assert.Equal("Original notes.", stored.DiagnosticNotes);
    }

    private int SeedJobWithNotes(string notes)
    {
        using var scope = CreateScope();
        var db = Db(scope);
        var suffix = Suffix();

        var customer = new Customer
        {
            FullName = "Job Board Test Owner",
            Email = $"jb-{suffix}@test.local",
            PhoneNumber = "+8801711000013",
            Address = "Dhaka",
            CreatedAt = DateTime.UtcNow,
            GarageId = GarageId
        };

        var vehicle = new Vehicle
        {
            Customer = customer,
            LicensePlateNumber = $"JB-{suffix}",
            Make = "Toyota",
            Model = "Allion",
            Year = 2020,
            Vin = $"VIN-JB-{suffix}",
            Color = "Silver",
            GarageId = GarageId
        };

        var job = new ServiceJob
        {
            Customer = customer,
            Vehicle = vehicle,
            BookingReference = $"JB-{suffix}",
            JobType = JobType.RoutineService,
            Status = JobStatus.InProgress,
            DiagnosticNotes = notes,
            CreatedAt = DateTime.UtcNow,
            GarageId = GarageId
        };

        db.ServiceJobs.Add(job);
        db.SaveChanges();
        return job.Id;
    }

    // =====================================================================================
    // TC-PAY-09: payment transaction reference, column length 100.
    // =====================================================================================

    private int SeedUnpaidInvoice()
    {
        using var scope = CreateScope();
        var db = Db(scope);
        var suffix = Suffix();

        var customer = new Customer
        {
            FullName = "Payment Test Customer",
            Email = $"pay-{suffix}@test.local",
            PhoneNumber = "+8801711000014",
            Address = "Dhaka",
            CreatedAt = DateTime.UtcNow,
            GarageId = GarageId
        };

        var vehicle = new Vehicle
        {
            Customer = customer,
            LicensePlateNumber = $"PY-{suffix}",
            Make = "Toyota",
            Model = "Allion",
            Year = 2020,
            Vin = $"VIN-PY-{suffix}",
            Color = "Silver",
            GarageId = GarageId
        };

        var job = new ServiceJob
        {
            Customer = customer,
            Vehicle = vehicle,
            BookingReference = $"PY-{suffix}",
            JobType = JobType.RoutineService,
            Status = JobStatus.Completed,
            CreatedAt = DateTime.UtcNow,
            GarageId = GarageId
        };

        var invoice = new Invoice
        {
            ServiceJob = job,
            InvoiceNumber = $"INV-{suffix}",
            SubTotal = 100.00m,
            TaxAmount = 0.00m,
            TotalAmount = 100.00m,
            PaymentStatus = PaymentStatus.Pending,
            IssuedAt = DateTime.UtcNow,
            GarageId = GarageId
        };

        db.Invoices.Add(invoice);
        db.SaveChanges();
        return invoice.Id;
    }

    private async Task<HttpResponseMessage> PostPaymentAsync(HttpClient client, int invoiceId, string reference)
    {
        var page = await client.GetAsync($"/Billing/RecordPayment?invoiceId={invoiceId}");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var token = await ExtractAntiForgeryTokenAsync(page);
        Assert.False(string.IsNullOrEmpty(token), "Could not find __RequestVerificationToken on the payment form.");

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Payment.Amount"] = "10.00",
            ["Payment.PaymentMethod"] = nameof(PaymentMethod.Cash),
            ["Payment.TransactionReference"] = reference,
            ["__RequestVerificationToken"] = token,
        });

        return await client.PostAsync($"/Billing/RecordPayment?invoiceId={invoiceId}", form);
    }

    [Fact]
    public async Task TransactionReference_AtOneHundredCharacters_IsAcceptedAndStored()
    {
        var client = await LoginAsAdminAsync();
        var invoiceId = SeedUnpaidInvoice();
        var reference = new string('T', 100);

        var response = await PostPaymentAsync(client, invoiceId, reference);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var scope = CreateScope();
        var stored = Db(scope).PaymentTransactions.Single(p => p.InvoiceId == invoiceId);
        Assert.Equal(100, stored.TransactionReference.Length);
    }

    [Fact]
    public async Task TransactionReference_AtOneHundredAndOneCharacters_IsRejectedAndNotStored()
    {
        var client = await LoginAsAdminAsync();
        var invoiceId = SeedUnpaidInvoice();

        var response = await PostPaymentAsync(client, invoiceId, new string('T', 101));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Transaction reference cannot exceed 100 characters.", body);

        using var scope = CreateScope();
        Assert.False(Db(scope).PaymentTransactions.Any(p => p.InvoiceId == invoiceId),
            "A payment with an over-length transaction reference reached the database.");
    }

    [Fact]
    public async Task TransactionReference_AtTwoHundredCharacters_IsRejectedAndNotStored()
    {
        // The exact value from TC-PAY-09.
        var client = await LoginAsAdminAsync();
        var invoiceId = SeedUnpaidInvoice();

        var response = await PostPaymentAsync(client, invoiceId, new string('T', 200));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Transaction reference cannot exceed 100 characters.", body);

        using var scope = CreateScope();
        Assert.False(Db(scope).PaymentTransactions.Any(p => p.InvoiceId == invoiceId));
    }

    // =====================================================================================
    // Requirement 3: the limits must be emitted for unobtrusive client-side validation too,
    // not only enforced on the server.
    // =====================================================================================

    [Fact]
    public async Task VehicleForm_EmitsClientSideLengthValidationForColour()
    {
        var client = await LoginAsAdminAsync();
        var customerId = SeedCustomer();

        var page = await client.GetAsync($"/Vehicle/Create?customerId={customerId}");
        var html = await page.Content.ReadAsStringAsync();

        Assert.Contains("data-val-length-max=\"50\"", html);
        Assert.Contains("Colour cannot exceed 50 characters.", html);
        Assert.Contains("jquery.validate.unobtrusive", html);
    }

    [Fact]
    public async Task ServiceJobForm_EmitsClientSideLengthValidationForBothFields()
    {
        var client = await LoginAsAdminAsync();
        var vehicleId = SeedVehicle();

        var page = await client.GetAsync($"/ServiceJob/Create?vehicleId={vehicleId}");
        var html = await page.Content.ReadAsStringAsync();

        Assert.Contains("Booking reference cannot exceed 20 characters.", html);
        Assert.Contains("Diagnostic notes cannot exceed 2000 characters.", html);
        Assert.Contains("data-val-length-max=\"20\"", html);
        Assert.Contains("data-val-length-max=\"2000\"", html);
        Assert.Contains("jquery.validate.unobtrusive", html);
    }

    [Fact]
    public async Task PaymentForm_EmitsClientSideLengthValidationForTransactionReference()
    {
        var client = await LoginAsAdminAsync();
        var invoiceId = SeedUnpaidInvoice();

        var page = await client.GetAsync($"/Billing/RecordPayment?invoiceId={invoiceId}");
        var html = await page.Content.ReadAsStringAsync();

        Assert.Contains("data-val-length-max=\"100\"", html);
        Assert.Contains("Transaction reference cannot exceed 100 characters.", html);
        Assert.Contains("jquery.validate.unobtrusive", html);
    }
}
