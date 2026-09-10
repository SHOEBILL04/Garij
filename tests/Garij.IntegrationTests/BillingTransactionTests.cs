using Garij.Application.Configuration;
using Garij.Application.DTOs;
using Garij.Application.Interfaces;
using Garij.Application.Services;
using Garij.Domain.Entities;
using Garij.Domain.Enums;
using Garij.Infrastructure.Persistence;
using Garij.Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Garij.IntegrationTests;

/// <summary>
/// Verifies GenerateInvoiceAsync's transaction actually rolls back both writes on failure —
/// the top risk called out in ROADMAP.md Stage 2's Risk Watch. Uses a real SQLite in-memory
/// connection (same technique as AuthorizationTestFactory) instead of fake repositories,
/// because rollback is a property of a real database transaction — fakes have no such concept,
/// so this could not be verified against them.
/// </summary>
public class BillingTransactionTests
{
    [Fact]
    public async Task GenerateInvoiceAsync_ShouldRollBackBothWrites_WhenStatusUpdateFailsMidTransaction()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<GarijDbContext>().UseSqlite(connection).Options;

        int jobId;
        await using (var seedContext = new GarijDbContext(options))
        {
            await seedContext.Database.EnsureCreatedAsync();

            var customer = new Customer { FullName = "Rollback Test Customer", Email = "rollback@test.local", PhoneNumber = "0000000000", Address = "N/A", CreatedAt = DateTime.UtcNow };
            var vehicle = new Vehicle { Customer = customer, LicensePlateNumber = "RB-0001", Make = "Test", Model = "Car", Year = 2020, Vin = "VIN0001", Color = "Black" };
            var catalog = new ServiceCatalog { Name = "Oil Change", Description = "Test service", BasePrice = 50.00m, EstimatedDurationMinutes = 30 };

            var job = new ServiceJob
            {
                Customer = customer,
                Vehicle = vehicle,
                BookingReference = "GRJ-2026-ROLLBACK",
                JobType = JobType.RoutineService,
                Status = JobStatus.InProgress,
                CreatedAt = DateTime.UtcNow
            };
            job.JobServiceDetails.Add(new JobServiceDetail { ServiceJob = job, ServiceCatalog = catalog, Quantity = 1, PriceAtBooking = 50.00m });

            seedContext.ServiceJobs.Add(job);
            await seedContext.SaveChangesAsync();
            jobId = job.Id;
        }

        await using var context = new GarijDbContext(options);
        var invoiceRepository = new InvoiceRepository(context);
        var serviceJobRepository = new ServiceJobRepository(context);
        var paymentTransactionRepository = new PaymentTransactionRepository(context);
        var billingSettings = Options.Create(new BillingSettings { TaxRatePercent = 15m });
        var throwingServiceJobService = new ThrowingServiceJobService(context);

        var billingService = new BillingService(
            context,
            invoiceRepository,
            paymentTransactionRepository,
            serviceJobRepository,
            throwingServiceJobService,
            billingSettings);

        await Assert.ThrowsAsync<InvalidOperationException>(() => billingService.GenerateInvoiceAsync(jobId));

        await using var verifyContext = new GarijDbContext(options);
        Assert.False(await verifyContext.Invoices.AnyAsync(i => i.ServiceJobId == jobId));

        var reloadedJob = await verifyContext.ServiceJobs.SingleAsync(j => j.Id == jobId);
        Assert.Equal(JobStatus.InProgress, reloadedJob.Status);
    }

    /// <summary>
    /// Performs the status write through the same DbContext GenerateInvoiceAsync's transaction
    /// runs on — exactly what the real ServiceJobService.UpdateServiceJobStatusAsync does — then
    /// throws, simulating a crash after the status update was staged but before the transaction
    /// commits. Proves the transaction covers both writes, not just the invoice insert.
    /// </summary>
    private sealed class ThrowingServiceJobService : IServiceJobService
    {
        private readonly GarijDbContext _context;

        public ThrowingServiceJobService(GarijDbContext context)
        {
            _context = context;
        }

        public async Task<ServiceJobDto> UpdateServiceJobStatusAsync(int id, JobStatus status)
        {
            var job = await _context.ServiceJobs.FindAsync(id) ?? throw new InvalidOperationException("Job not found in test setup.");
            job.Status = status;
            await _context.SaveChangesAsync();

            throw new InvalidOperationException("Simulated mid-transaction failure after the status update was staged.");
        }

        public Task<IEnumerable<ServiceJobDto>> GetAllServiceJobsAsync() => throw new NotImplementedException();

        public Task<IEnumerable<ServiceJobDto>> GetServiceJobsByStatusAsync(JobStatus status) => throw new NotImplementedException();

        public Task<IEnumerable<ServiceJobDto>> GetFilteredServiceJobsAsync(JobStatus? status = null, int? mechanicId = null, string? sortBy = null, string? searchTerm = null) => throw new NotImplementedException();

        public Task<ServiceJobDto?> GetServiceJobByIdAsync(int id) => throw new NotImplementedException();

        public Task<ServiceJobDto?> GetServiceJobByBookingReferenceAsync(string bookingReference) => throw new NotImplementedException();

        public Task<ServiceJobDto> CreateServiceJobAsync(ServiceJobDto serviceJob) => throw new NotImplementedException();

        public Task<ServiceJobDto> UpdateServiceJobAsync(ServiceJobDto serviceJob) => throw new NotImplementedException();

        public Task DeleteServiceJobAsync(int id) => throw new NotImplementedException();

        public Task<MechanicAssignmentDto> AssignMechanicAsync(int serviceJobId, int userId, RoleInJob roleInJob) => throw new NotImplementedException();

        public Task<MechanicAssignmentDto> UpdateMechanicAssignmentRoleAsync(int assignmentId, RoleInJob roleInJob) => throw new NotImplementedException();

        public Task RemoveMechanicAssignmentAsync(int assignmentId) => throw new NotImplementedException();

        public Task<IEnumerable<MechanicAssignmentDto>> GetAssignmentsByServiceJobAsync(int serviceJobId) => throw new NotImplementedException();

        public Task<IEnumerable<ServiceJobDto>> GetJobsByMechanicAsync(int mechanicUserId) => throw new NotImplementedException();

        public Task<ServiceJobDto> SaveDiagnosticNotesAsync(int serviceJobId, string notes) => throw new NotImplementedException();
    }
}
