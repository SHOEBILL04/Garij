using Garij.Application.Services;
using Garij.Domain.Entities;
using Garij.Domain.Enums;
using Garij.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Garij.UnitTests;

public class ReportingServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly GarijDbContext _context;
    private readonly ReportingService _reportingService;

    public ReportingServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<GarijDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new GarijDbContext(options);
        _context.Database.EnsureCreated();

        _reportingService = new ReportingService(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task GetRevenueReportAsync_CalculatesBilledAndCollectedSideBySide_ExcludingRefundedInvoices()
    {
        // Arrange
        var customer = new Customer { FullName = "John Doe", PhoneNumber = "1234567890", Email = "john@example.com", Address = "123 Main St" };
        var vehicle = new Vehicle { Customer = customer, LicensePlateNumber = "DHK-1234", Make = "Toyota", Model = "Corolla", Year = 2020 };

        _context.Customers.Add(customer);
        _context.Vehicles.Add(vehicle);

        var job1 = new ServiceJob { Customer = customer, Vehicle = vehicle, BookingReference = "GRJ-2026-0001", Status = JobStatus.Completed, CreatedAt = new DateTime(2026, 1, 5) };
        var job2 = new ServiceJob { Customer = customer, Vehicle = vehicle, BookingReference = "GRJ-2026-0002", Status = JobStatus.Completed, CreatedAt = new DateTime(2026, 1, 10) };
        var job3 = new ServiceJob { Customer = customer, Vehicle = vehicle, BookingReference = "GRJ-2026-0003", Status = JobStatus.Completed, CreatedAt = new DateTime(2026, 1, 15) };
        _context.ServiceJobs.AddRange(job1, job2, job3);
        await _context.SaveChangesAsync();

        // Active billed invoice 1: Net = 100, Gross = 115, Issued Jan 10
        var inv1 = new Invoice
        {
            ServiceJobId = job1.Id,
            InvoiceNumber = "INV-2026-0001",
            SubTotal = 100m,
            TaxAmount = 15m,
            TotalAmount = 115m,
            PaymentStatus = PaymentStatus.Paid,
            IssuedAt = new DateTime(2026, 1, 10)
        };

        // Active billed invoice 2: Net = 200, Gross = 230, Issued Jan 12 (Partially Paid)
        var inv2 = new Invoice
        {
            ServiceJobId = job2.Id,
            InvoiceNumber = "INV-2026-0002",
            SubTotal = 200m,
            TaxAmount = 30m,
            TotalAmount = 230m,
            PaymentStatus = PaymentStatus.PartiallyPaid,
            IssuedAt = new DateTime(2026, 1, 12)
        };

        // Refunded/Void invoice: Should be excluded from billed revenue
        var invRefunded = new Invoice
        {
            ServiceJobId = job3.Id,
            InvoiceNumber = "INV-2026-0003",
            SubTotal = 500m,
            TaxAmount = 75m,
            TotalAmount = 575m,
            PaymentStatus = PaymentStatus.Refunded,
            IssuedAt = new DateTime(2026, 1, 15)
        };

        _context.Invoices.AddRange(inv1, inv2, invRefunded);
        await _context.SaveChangesAsync();

        // Payments: Payment 1 on Inv 1 for 115 in Jan. Payment 2 on Inv 2 for 100 in Jan.
        var p1 = new PaymentTransaction { InvoiceId = inv1.Id, Amount = 115m, PaymentMethod = PaymentMethod.Card, PaidAt = new DateTime(2026, 1, 10) };
        var p2 = new PaymentTransaction { InvoiceId = inv2.Id, Amount = 100m, PaymentMethod = PaymentMethod.Cash, PaidAt = new DateTime(2026, 1, 14) };

        _context.PaymentTransactions.AddRange(p1, p2);
        await _context.SaveChangesAsync();

        // Act
        var report = await _reportingService.GetRevenueReportAsync(new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));

        // Assert
        // Net Billed: 100 + 200 = 300 (invRefunded excluded)
        Assert.Equal(300m, report.TotalBilledNet);
        // Gross Billed: 115 + 230 = 345
        Assert.Equal(345m, report.TotalBilledGross);
        // Cash Collected: 115 + 100 = 215
        Assert.Equal(215m, report.TotalCollected);
        // Active Invoices: 2
        Assert.Equal(2, report.TotalInvoices);
        // Average ticket: 345 / 2 = 172.50
        Assert.Equal(172.50m, report.AverageInvoiceValue);

        Assert.Single(report.MonthlyBilledBreakdown);
        Assert.Equal(300m, report.MonthlyBilledBreakdown[0].NetRevenue);
        Assert.Equal(345m, report.MonthlyBilledBreakdown[0].GrossRevenue);

        Assert.Single(report.MonthlyCollectedBreakdown);
        Assert.Equal(215m, report.MonthlyCollectedBreakdown[0].CollectedAmount);
        Assert.Equal(2, report.MonthlyCollectedBreakdown[0].TransactionCount);
    }

    [Fact]
    public async Task GetRevenueReportAsync_PendingInvoice_AppearsInBilledRevenue_ButNotInCollectedRevenue()
    {
        // Arrange
        var customer = new Customer { FullName = "Pending Customer", PhoneNumber = "111222333", Email = "pending@example.com", Address = "Pending Lane" };
        var vehicle = new Vehicle { Customer = customer, LicensePlateNumber = "DHK-9999", Make = "Honda", Model = "Civic", Year = 2021 };
        _context.Customers.Add(customer);
        _context.Vehicles.Add(vehicle);

        var job = new ServiceJob { Customer = customer, Vehicle = vehicle, BookingReference = "GRJ-2026-9999", Status = JobStatus.InProgress, CreatedAt = new DateTime(2026, 2, 1) };
        _context.ServiceJobs.Add(job);
        await _context.SaveChangesAsync();

        var pendingInvoice = new Invoice
        {
            ServiceJobId = job.Id,
            InvoiceNumber = "INV-2026-PENDING",
            SubTotal = 400m,
            TaxAmount = 60m,
            TotalAmount = 460m,
            PaymentStatus = PaymentStatus.Pending,
            IssuedAt = new DateTime(2026, 2, 5)
        };
        _context.Invoices.Add(pendingInvoice);
        await _context.SaveChangesAsync();
        // Note: Zero payment transactions recorded for this invoice

        // Act
        var report = await _reportingService.GetRevenueReportAsync(new DateTime(2026, 2, 1), new DateTime(2026, 2, 28));

        // Assert: Billed revenue reflects the pending invoice
        Assert.Equal(400m, report.TotalBilledNet);
        Assert.Equal(460m, report.TotalBilledGross);
        Assert.Equal(1, report.TotalInvoices);

        // Assert: Collected revenue is strictly 0 because no payment transactions were made
        Assert.Equal(0m, report.TotalCollected);
        Assert.Empty(report.MonthlyCollectedBreakdown);
    }

    [Fact]
    public async Task GetRevenueReportAsync_InvoicePaidThenRefunded_ExcludedFromBilled_IncludedInCollected_AppearsInRefundedGross()
    {
        // Arrange
        var customer = new Customer { FullName = "Refunded Customer", PhoneNumber = "777888999", Email = "refunded@example.com", Address = "Refund Road" };
        var vehicle = new Vehicle { Customer = customer, LicensePlateNumber = "DHK-7777", Make = "Toyota", Model = "Yaris", Year = 2022 };
        _context.Customers.Add(customer);
        _context.Vehicles.Add(vehicle);

        var job = new ServiceJob { Customer = customer, Vehicle = vehicle, BookingReference = "GRJ-2026-REF1", Status = JobStatus.Completed, CreatedAt = new DateTime(2026, 4, 1) };
        _context.ServiceJobs.Add(job);
        await _context.SaveChangesAsync();

        // Invoice issued inside the period with Net 300, Gross 345, status Refunded
        var refundedInvoice = new Invoice
        {
            ServiceJobId = job.Id,
            InvoiceNumber = "INV-2026-REFUNDED",
            SubTotal = 300m,
            TaxAmount = 45m,
            TotalAmount = 345m,
            PaymentStatus = PaymentStatus.Refunded,
            IssuedAt = new DateTime(2026, 4, 10)
        };
        _context.Invoices.Add(refundedInvoice);
        await _context.SaveChangesAsync();

        // Payment physically received inside the period for 345
        var payment = new PaymentTransaction
        {
            InvoiceId = refundedInvoice.Id,
            Amount = 345m,
            PaymentMethod = PaymentMethod.Card,
            PaidAt = new DateTime(2026, 4, 11)
        };
        _context.PaymentTransactions.Add(payment);
        await _context.SaveChangesAsync();

        // Act: Run report for April 2026
        var report = await _reportingService.GetRevenueReportAsync(new DateTime(2026, 4, 1), new DateTime(2026, 4, 30));

        // Assert: Excluded from TotalBilledGross and TotalBilledNet
        Assert.Equal(0m, report.TotalBilledGross);
        Assert.Equal(0m, report.TotalBilledNet);
        Assert.Equal(0, report.TotalInvoices);

        // Assert: Still included in TotalCollected (physical cash intake ledger)
        Assert.Equal(345m, report.TotalCollected);
        Assert.Single(report.MonthlyCollectedBreakdown);
        Assert.Equal(345m, report.MonthlyCollectedBreakdown[0].CollectedAmount);

        // Assert: Surfaces in Refunded metrics
        Assert.Equal(1, report.RefundedInvoiceCount);
        Assert.Equal(345m, report.RefundedGrossAmount);
    }

    [Fact]
    public async Task GetRevenueReportAsync_EmptyRange_ReturnsZeroTotalsGracefully()
    {
        // Act
        var report = await _reportingService.GetRevenueReportAsync(new DateTime(2025, 1, 1), new DateTime(2025, 1, 31));

        // Assert
        Assert.Equal(0m, report.TotalBilledNet);
        Assert.Equal(0m, report.TotalBilledGross);
        Assert.Equal(0m, report.TotalCollected);
        Assert.Equal(0, report.TotalInvoices);
        Assert.Equal(0m, report.AverageInvoiceValue);
        Assert.Empty(report.MonthlyBilledBreakdown);
        Assert.Empty(report.MonthlyCollectedBreakdown);
    }

    [Fact]
    public async Task GetPartsConsumptionReportAsync_RanksByTotalCost_AndFallsBackToCreatedAtWhenCompletedAtIsNull()
    {
        // Arrange
        var customer = new Customer { FullName = "Alice", PhoneNumber = "5551234567", Email = "alice@example.com", Address = "456 Oak St" };
        var vehicle = new Vehicle { Customer = customer, LicensePlateNumber = "DHK-9999", Make = "Honda", Model = "Civic", Year = 2019 };

        _context.Customers.Add(customer);
        _context.Vehicles.Add(vehicle);

        // Job 1: Completed in February
        var jobCompleted = new ServiceJob
        {
            Customer = customer,
            Vehicle = vehicle,
            BookingReference = "GRJ-2026-COMP",
            Status = JobStatus.Completed,
            CreatedAt = new DateTime(2026, 1, 20),
            CompletedAt = new DateTime(2026, 2, 5)
        };

        // Job 2: InProgress, CompletedAt is null, CreatedAt is in February
        var jobInProgress = new ServiceJob
        {
            Customer = customer,
            Vehicle = vehicle,
            BookingReference = "GRJ-2026-PROG",
            Status = JobStatus.InProgress,
            CreatedAt = new DateTime(2026, 2, 10),
            CompletedAt = null
        };

        _context.ServiceJobs.AddRange(jobCompleted, jobInProgress);

        var partA = new Part { Name = "Oil Filter", PartNumber = "FLT-01", UnitPrice = 15m, QuantityInStock = 4, ReorderLevel = 5 }; // Low stock: 4 <= 5
        var partB = new Part { Name = "Brake Pads", PartNumber = "BRK-02", UnitPrice = 80m, QuantityInStock = 20, ReorderLevel = 5 }; // Adequate stock

        _context.Parts.AddRange(partA, partB);
        await _context.SaveChangesAsync();

        // 4 units of Part A on jobCompleted at $15 = $60
        var usage1 = new JobPartUsed { ServiceJobId = jobCompleted.Id, PartId = partA.Id, QuantityUsed = 4, PriceAtUsage = 15m };
        // 2 units of Part B on jobInProgress at $80 = $160
        var usage2 = new JobPartUsed { ServiceJobId = jobInProgress.Id, PartId = partB.Id, QuantityUsed = 2, PriceAtUsage = 80m };

        _context.JobPartsUsed.AddRange(usage1, usage2);
        await _context.SaveChangesAsync();

        // Act: Filter for February 2026
        var report = (await _reportingService.GetPartsConsumptionReportAsync(new DateTime(2026, 2, 1), new DateTime(2026, 2, 28))).ToList();

        // Assert
        Assert.Equal(2, report.Count);

        // Ranked by TotalCost descending: Part B ($160) should be first, Part A ($60) second
        Assert.Equal("Brake Pads", report[0].PartName);
        Assert.Equal(160m, report[0].TotalCost);
        Assert.Equal(2, report[0].TotalQuantityUsed);
        Assert.False(report[0].IsLowStock);

        Assert.Equal("Oil Filter", report[1].PartName);
        Assert.Equal(60m, report[1].TotalCost);
        Assert.Equal(4, report[1].TotalQuantityUsed);
        Assert.True(report[1].IsLowStock); // 4 in stock <= 5 reorder level
    }

    [Fact]
    public async Task GetMechanicWorkloadReportAsync_CalculatesLeadAndAssistantCounts_AndCompletedSharePercentage()
    {
        // Arrange
        var idUser1 = new Microsoft.AspNetCore.Identity.IdentityUser { Id = "id-m1", UserName = "karim@garij.com", Email = "karim@garij.com" };
        var idUser2 = new Microsoft.AspNetCore.Identity.IdentityUser { Id = "id-m2", UserName = "rahim@garij.com", Email = "rahim@garij.com" };
        _context.Users.AddRange(idUser1, idUser2);
        await _context.SaveChangesAsync();

        var mech1 = new User { IdentityUserId = "id-m1", FullName = "Karim Mechanic", Email = "karim@garij.com", Role = UserRole.Mechanic, CreatedAt = DateTime.UtcNow };
        var mech2 = new User { IdentityUserId = "id-m2", FullName = "Rahim Mechanic", Email = "rahim@garij.com", Role = UserRole.Mechanic, CreatedAt = DateTime.UtcNow };
        _context.StaffUsers.AddRange(mech1, mech2);


        var customer = new Customer { FullName = "Customer", PhoneNumber = "1112223333", Email = "c@example.com", Address = "Road 1" };
        var vehicle = new Vehicle { Customer = customer, LicensePlateNumber = "DHK-5555", Make = "Nissan", Model = "Sunny", Year = 2018 };

        _context.Customers.Add(customer);
        _context.Vehicles.Add(vehicle);

        var jobCompleted1 = new ServiceJob { Customer = customer, Vehicle = vehicle, BookingReference = "GRJ-JOB-01", Status = JobStatus.Completed, CreatedAt = DateTime.UtcNow };
        var jobCompleted2 = new ServiceJob { Customer = customer, Vehicle = vehicle, BookingReference = "GRJ-JOB-02", Status = JobStatus.Completed, CreatedAt = DateTime.UtcNow };
        var jobActive = new ServiceJob { Customer = customer, Vehicle = vehicle, BookingReference = "GRJ-JOB-03", Status = JobStatus.InProgress, CreatedAt = DateTime.UtcNow };

        _context.ServiceJobs.AddRange(jobCompleted1, jobCompleted2, jobActive);
        await _context.SaveChangesAsync();

        // Mech1 is Lead on jobCompleted1, and Assistant on jobCompleted2 (CompletedJobCount = 2)
        var ma1 = new MechanicAssignment { ServiceJobId = jobCompleted1.Id, UserId = mech1.Id, RoleInJob = RoleInJob.Lead, AssignedAt = new DateTime(2026, 3, 1) };
        var ma2 = new MechanicAssignment { ServiceJobId = jobCompleted2.Id, UserId = mech1.Id, RoleInJob = RoleInJob.Assistant, AssignedAt = new DateTime(2026, 3, 2) };

        // Mech2 is Lead on jobActive (ActiveJobCount = 1)
        var ma3 = new MechanicAssignment { ServiceJobId = jobActive.Id, UserId = mech2.Id, RoleInJob = RoleInJob.Lead, AssignedAt = new DateTime(2026, 3, 3) };

        _context.MechanicAssignments.AddRange(ma1, ma2, ma3);
        await _context.SaveChangesAsync();

        // Act
        var report = (await _reportingService.GetMechanicWorkloadReportAsync(new DateTime(2026, 3, 1), new DateTime(2026, 3, 31))).ToList();

        // Assert
        Assert.Equal(2, report.Count);

        var m1Workload = report.First(r => r.UserId == mech1.Id);
        Assert.Equal(2, m1Workload.CompletedJobCount);
        Assert.Equal(1, m1Workload.LeadCompletedJobCount);
        Assert.Equal(1, m1Workload.AssistingCompletedJobCount);
        Assert.Equal(0, m1Workload.ActiveJobCount);
        // Total completed across all mechanics = 2. Mech1 has 2/2 = 100%
        Assert.Equal(100.0m, m1Workload.CompletedSharePercentage);

        var m2Workload = report.First(r => r.UserId == mech2.Id);
        Assert.Equal(0, m2Workload.CompletedJobCount);
        Assert.Equal(1, m2Workload.ActiveJobCount);
        Assert.Equal(1, m2Workload.LeadActiveJobCount);
        Assert.Equal(0m, m2Workload.CompletedSharePercentage);
    }

    [Fact]
    public async Task GetPartsConsumptionReportAsync_EmptyRange_ReturnsEmptyCollection()
    {
        // Act
        var report = await _reportingService.GetPartsConsumptionReportAsync(new DateTime(2025, 1, 1), new DateTime(2025, 1, 31));

        // Assert
        Assert.NotNull(report);
        Assert.Empty(report);
    }

    [Fact]
    public async Task GetMechanicWorkloadReportAsync_EmptyRange_ReturnsWorkloadWithZeroCounts()
    {
        // Arrange
        var idUser = new Microsoft.AspNetCore.Identity.IdentityUser { Id = "id-empty-mech", UserName = "empty@garij.com", Email = "empty@garij.com" };
        _context.Users.Add(idUser);
        var mech = new User { IdentityUserId = "id-empty-mech", FullName = "Empty Mech", Email = "empty@garij.com", Role = UserRole.Mechanic, CreatedAt = DateTime.UtcNow };
        _context.StaffUsers.Add(mech);
        await _context.SaveChangesAsync();

        // Act: Filter for date range with zero assignments
        var report = (await _reportingService.GetMechanicWorkloadReportAsync(new DateTime(2025, 1, 1), new DateTime(2025, 1, 31))).ToList();

        // Assert
        Assert.NotEmpty(report);
        var mechReport = report.First(r => r.UserId == mech.Id);
        Assert.Equal(0, mechReport.ActiveJobCount);
        Assert.Equal(0, mechReport.CompletedJobCount);
        Assert.Equal(0, mechReport.TotalJobsAssigned);
        Assert.Equal(0m, mechReport.CompletedSharePercentage);
    }
}
