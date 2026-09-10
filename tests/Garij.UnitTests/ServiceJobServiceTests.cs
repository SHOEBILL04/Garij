using Garij.Application.DTOs;
using Garij.Application.Interfaces;
using Garij.Application.Services;
using Garij.Domain.Entities;
using Garij.Domain.Enums;
using Garij.Domain.Exceptions;
using Garij.Infrastructure.Persistence;
using Garij.Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Garij.UnitTests;

public class ServiceJobServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly GarijDbContext _context;
    private readonly ServiceJobService _serviceJobService;

    public ServiceJobServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<GarijDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new GarijDbContext(options);
        _context.Database.EnsureCreated();

        var customerRepo = new CustomerRepository(_context);
        var vehicleRepo = new VehicleRepository(_context);
        var userRepo = new UserRepository(_context);
        var jobRepo = new ServiceJobRepository(_context);
        var assignmentRepo = new MechanicAssignmentRepository(_context);

        _serviceJobService = new ServiceJobService(jobRepo, vehicleRepo, userRepo, assignmentRepo, new FakeNotificationService());
    }

    [Fact]
    public async Task CreateServiceJobAsync_GeneratesUniqueBookingReference_WhenBlank()
    {
        // Arrange
        var customer = new Customer { FullName = "John Doe", Email = "john@example.com", PhoneNumber = "1234567890", Address = "123 St" };
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync();

        var vehicle = new Vehicle { CustomerId = customer.Id, LicensePlateNumber = "DHA-2026", Make = "Toyota", Model = "Corolla", Year = 2022, Vin = "VIN123", Color = "White" };
        _context.Vehicles.Add(vehicle);
        await _context.SaveChangesAsync();

        var jobDto = new ServiceJobDto
        {
            VehicleId = vehicle.Id,
            JobType = JobType.RoutineService,
            DiagnosticNotes = "Regular oil change requested."
        };

        // Act
        var result = await _serviceJobService.CreateServiceJobAsync(jobDto);

        // Assert
        Assert.NotNull(result);
        Assert.StartsWith($"GRJ-{DateTime.UtcNow.Year}-", result.BookingReference);
        Assert.Equal(JobStatus.Requested, result.Status);
        Assert.Equal("DHA-2026", result.VehiclePlateNumber);
    }

    [Fact]
    public async Task AssignMechanicAsync_CreatesMechanicAssignmentSuccessfully()
    {
        // Arrange
        var customer = new Customer { FullName = "Jane Smith", Email = "jane@example.com", PhoneNumber = "0987654321", Address = "456 Ave" };
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync();

        var vehicle = new Vehicle { CustomerId = customer.Id, LicensePlateNumber = "CTG-5005", Make = "Honda", Model = "Civic", Year = 2021, Vin = "VIN456", Color = "Black" };
        _context.Vehicles.Add(vehicle);
        await _context.SaveChangesAsync();

        var identityUser = new Microsoft.AspNetCore.Identity.IdentityUser { Id = "mech-01", UserName = "sam@garij.com", Email = "sam@garij.com" };
        _context.Users.Add(identityUser);
        await _context.SaveChangesAsync();

        var mechanic = new User { IdentityUserId = identityUser.Id, FullName = "Lead Mech Sam", Email = "sam@garij.com", PhoneNumber = "1112223333", Role = UserRole.Mechanic };
        _context.StaffUsers.Add(mechanic);
        await _context.SaveChangesAsync();

        var jobDto = await _serviceJobService.CreateServiceJobAsync(new ServiceJobDto
        {
            VehicleId = vehicle.Id,
            JobType = JobType.Repair
        });

        // Act
        var assignment = await _serviceJobService.AssignMechanicAsync(jobDto.Id, mechanic.Id, RoleInJob.Lead);

        // Assert
        Assert.NotNull(assignment);
        Assert.Equal(jobDto.Id, assignment.ServiceJobId);
        Assert.Equal(mechanic.Id, assignment.UserId);
        Assert.Equal("Lead Mech Sam", assignment.MechanicName);
        Assert.Equal(RoleInJob.Lead, assignment.RoleInJob);
    }

    [Fact]
    public async Task GetServiceJobsByStatusAsync_FiltersByStatusCorrectly()
    {
        // Arrange
        var customer = new Customer { FullName = "Test Customer", Email = "test@example.com", PhoneNumber = "123", Address = "Test" };
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync();

        var vehicle = new Vehicle { CustomerId = customer.Id, LicensePlateNumber = "KHL-1234", Make = "Mazda", Model = "CX-5", Year = 2023, Vin = "VIN789", Color = "Red" };
        _context.Vehicles.Add(vehicle);
        await _context.SaveChangesAsync();

        var job1 = await _serviceJobService.CreateServiceJobAsync(new ServiceJobDto { VehicleId = vehicle.Id, JobType = JobType.RoutineService, Status = JobStatus.Requested });
        var job2 = await _serviceJobService.CreateServiceJobAsync(new ServiceJobDto { VehicleId = vehicle.Id, JobType = JobType.Repair, Status = JobStatus.InProgress });

        // Act
        var requestedJobs = await _serviceJobService.GetServiceJobsByStatusAsync(JobStatus.Requested);
        var inProgressJobs = await _serviceJobService.GetServiceJobsByStatusAsync(JobStatus.InProgress);

        // Assert
        Assert.Single(requestedJobs);
        Assert.Equal(job1.Id, requestedJobs.First().Id);

        Assert.Single(inProgressJobs);
        Assert.Equal(job2.Id, inProgressJobs.First().Id);
    }

    [Fact]
    public async Task UpdateServiceJobStatusAsync_AllowsLegalSequentialTransitions()
    {
        // Arrange
        var customer = new Customer { FullName = "Test", Email = "legal@test.com", PhoneNumber = "123", Address = "Test" };
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync();

        var vehicle = new Vehicle { CustomerId = customer.Id, LicensePlateNumber = "DHA-1010", Make = "Toyota", Model = "Axio", Year = 2020, Vin = "VIN101", Color = "Silver" };
        _context.Vehicles.Add(vehicle);
        await _context.SaveChangesAsync();

        var job = await _serviceJobService.CreateServiceJobAsync(new ServiceJobDto { VehicleId = vehicle.Id, JobType = JobType.RoutineService });

        // Act & Assert: Requested -> InspectionPending
        var step1 = await _serviceJobService.UpdateServiceJobStatusAsync(job.Id, JobStatus.InspectionPending);
        Assert.Equal(JobStatus.InspectionPending, step1.Status);

        // InspectionPending -> CustomerApprovalNeeded
        var step2 = await _serviceJobService.UpdateServiceJobStatusAsync(job.Id, JobStatus.CustomerApprovalNeeded);
        Assert.Equal(JobStatus.CustomerApprovalNeeded, step2.Status);

        // CustomerApprovalNeeded -> InProgress
        var step3 = await _serviceJobService.UpdateServiceJobStatusAsync(job.Id, JobStatus.InProgress);
        Assert.Equal(JobStatus.InProgress, step3.Status);
    }

    [Fact]
    public async Task UpdateServiceJobStatusAsync_ThrowsBusinessRuleException_WhenMovingBackwards()
    {
        // Arrange
        var customer = new Customer { FullName = "Test", Email = "invalid@test.com", PhoneNumber = "123", Address = "Test" };
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync();

        var vehicle = new Vehicle { CustomerId = customer.Id, LicensePlateNumber = "DHA-2020", Make = "Honda", Model = "Fit", Year = 2019, Vin = "VIN202", Color = "Blue" };
        _context.Vehicles.Add(vehicle);
        await _context.SaveChangesAsync();

        var job = await _serviceJobService.CreateServiceJobAsync(new ServiceJobDto { VehicleId = vehicle.Id, JobType = JobType.RoutineService });
        await _serviceJobService.UpdateServiceJobStatusAsync(job.Id, JobStatus.InProgress);

        // Act & Assert: stages may be skipped going forward, but never re-entered.
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _serviceJobService.UpdateServiceJobStatusAsync(job.Id, JobStatus.InspectionPending));

        Assert.Equal("BR-007", ex.RuleCode);
    }

    [Theory]
    [InlineData(JobStatus.CustomerApprovalNeeded)]
    [InlineData(JobStatus.InProgress)]
    [InlineData(JobStatus.Completed)]
    public async Task UpdateServiceJobStatusAsync_AllowsSkippingStages(JobStatus target)
    {
        // Arrange
        var customer = new Customer { FullName = "Test", Email = $"skip-{target}@test.com", PhoneNumber = "123", Address = "Test" };
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync();

        var vehicle = new Vehicle { CustomerId = customer.Id, LicensePlateNumber = $"SKIP-{(int)target}", Make = "Honda", Model = "Fit", Year = 2019, Vin = $"VINSKIP{(int)target}", Color = "Blue" };
        _context.Vehicles.Add(vehicle);
        await _context.SaveChangesAsync();

        var job = await _serviceJobService.CreateServiceJobAsync(new ServiceJobDto { VehicleId = vehicle.Id, JobType = JobType.RoutineService });

        // Act: jump straight from Requested to a later stage, skipping the ones between.
        var updated = await _serviceJobService.UpdateServiceJobStatusAsync(job.Id, target);

        // Assert
        Assert.Equal(target, updated.Status);
    }

    [Fact]
    public async Task UpdateServiceJobStatusAsync_StampsCompletedAt_WhenSkippingStraightToCompleted()
    {
        // Arrange
        var customer = new Customer { FullName = "Test", Email = "straight-to-done@test.com", PhoneNumber = "123", Address = "Test" };
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync();

        var vehicle = new Vehicle { CustomerId = customer.Id, LicensePlateNumber = "DHA-9090", Make = "Honda", Model = "Fit", Year = 2019, Vin = "VIN909", Color = "Blue" };
        _context.Vehicles.Add(vehicle);
        await _context.SaveChangesAsync();

        var job = await _serviceJobService.CreateServiceJobAsync(new ServiceJobDto { VehicleId = vehicle.Id, JobType = JobType.RoutineService });

        // Act
        var updated = await _serviceJobService.UpdateServiceJobStatusAsync(job.Id, JobStatus.Completed);

        // Assert
        Assert.Equal(JobStatus.Completed, updated.Status);
        Assert.NotNull(updated.CompletedAt);
    }

    [Fact]
    public async Task UpdateServiceJobStatusAsync_SucceedsCompletion_WhenNoPartsLogged()
    {
        // Arrange: a labor-only job (e.g. diagnostics/inspection) with nothing logged in JobPartsUsed.
        var customer = new Customer { FullName = "Test", Email = "noparts@test.com", PhoneNumber = "123", Address = "Test" };
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync();

        var vehicle = new Vehicle { CustomerId = customer.Id, LicensePlateNumber = "DHA-3030", Make = "Nissan", Model = "Sunny", Year = 2018, Vin = "VIN303", Color = "Red" };
        _context.Vehicles.Add(vehicle);
        await _context.SaveChangesAsync();

        var job = await _serviceJobService.CreateServiceJobAsync(new ServiceJobDto { VehicleId = vehicle.Id, JobType = JobType.RoutineService });
        await _serviceJobService.UpdateServiceJobStatusAsync(job.Id, JobStatus.InspectionPending);
        await _serviceJobService.UpdateServiceJobStatusAsync(job.Id, JobStatus.CustomerApprovalNeeded);
        await _serviceJobService.UpdateServiceJobStatusAsync(job.Id, JobStatus.InProgress);

        // Act: completing a job with zero parts logged must succeed (labor-only jobs are valid).
        var completedJob = await _serviceJobService.UpdateServiceJobStatusAsync(job.Id, JobStatus.Completed);

        // Assert
        Assert.Equal(JobStatus.Completed, completedJob.Status);
        Assert.NotNull(completedJob.CompletedAt);
    }

    [Fact]
    public async Task UpdateServiceJobStatusAsync_SucceedsCompletion_WhenPartsAreLogged()
    {
        // Arrange
        var customer = new Customer { FullName = "Test", Email = "withparts@test.com", PhoneNumber = "123", Address = "Test" };
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync();

        var vehicle = new Vehicle { CustomerId = customer.Id, LicensePlateNumber = "DHA-4040", Make = "Toyota", Model = "Premio", Year = 2021, Vin = "VIN404", Color = "White" };
        _context.Vehicles.Add(vehicle);
        await _context.SaveChangesAsync();

        var part = new Part { Name = "Brake Oil", PartNumber = "BO-01", UnitPrice = 15.00m, QuantityInStock = 50, ReorderLevel = 10 };
        _context.Parts.Add(part);
        await _context.SaveChangesAsync();

        var job = await _serviceJobService.CreateServiceJobAsync(new ServiceJobDto { VehicleId = vehicle.Id, JobType = JobType.RoutineService });
        await _serviceJobService.UpdateServiceJobStatusAsync(job.Id, JobStatus.InspectionPending);
        await _serviceJobService.UpdateServiceJobStatusAsync(job.Id, JobStatus.CustomerApprovalNeeded);
        await _serviceJobService.UpdateServiceJobStatusAsync(job.Id, JobStatus.InProgress);

        // Log a part
        _context.JobPartsUsed.Add(new JobPartUsed { ServiceJobId = job.Id, PartId = part.Id, QuantityUsed = 1, PriceAtUsage = 15.00m });
        await _context.SaveChangesAsync();

        // Act
        var completedJob = await _serviceJobService.UpdateServiceJobStatusAsync(job.Id, JobStatus.Completed);

        // Assert
        Assert.Equal(JobStatus.Completed, completedJob.Status);
        Assert.NotNull(completedJob.CompletedAt);
    }

    [Fact]
    public async Task AssignMechanicAsync_ThrowsBusinessRuleException_WhenLeadMechanicAlreadyExists()
    {
        // Arrange
        var customer = new Customer { FullName = "Test", Email = "leadmech@test.com", PhoneNumber = "123", Address = "Test" };
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync();

        var vehicle = new Vehicle { CustomerId = customer.Id, LicensePlateNumber = "DHA-5050", Make = "Honda", Model = "Vezel", Year = 2022, Vin = "VIN505", Color = "Black" };
        _context.Vehicles.Add(vehicle);
        await _context.SaveChangesAsync();

        var mech1User = new Microsoft.AspNetCore.Identity.IdentityUser { Id = "m1", UserName = "m1@test.com", Email = "m1@test.com" };
        var mech2User = new Microsoft.AspNetCore.Identity.IdentityUser { Id = "m2", UserName = "m2@test.com", Email = "m2@test.com" };
        _context.Users.AddRange(mech1User, mech2User);
        await _context.SaveChangesAsync();

        var mech1 = new User { IdentityUserId = mech1User.Id, FullName = "Mechanic One", Email = "m1@test.com", Role = UserRole.Mechanic };
        var mech2 = new User { IdentityUserId = mech2User.Id, FullName = "Mechanic Two", Email = "m2@test.com", Role = UserRole.Mechanic };
        _context.StaffUsers.AddRange(mech1, mech2);
        await _context.SaveChangesAsync();

        var job = await _serviceJobService.CreateServiceJobAsync(new ServiceJobDto { VehicleId = vehicle.Id, JobType = JobType.Repair });

        // Assign mech1 as Lead
        await _serviceJobService.AssignMechanicAsync(job.Id, mech1.Id, RoleInJob.Lead);

        // Act & Assert: Assigning mech2 as Lead must fail with BR-003
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _serviceJobService.AssignMechanicAsync(job.Id, mech2.Id, RoleInJob.Lead));

        Assert.Equal("BR-003", ex.RuleCode);
    }

    [Fact]
    public async Task AssignMechanicAsync_ThrowsBusinessRuleException_WhenUserIsNotMechanic()
    {
        // Arrange
        var customer = new Customer { FullName = "Test", Email = "nonmech@test.com", PhoneNumber = "123", Address = "Test" };
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync();

        var vehicle = new Vehicle { CustomerId = customer.Id, LicensePlateNumber = "DHA-6060", Make = "Toyota", Model = "Corolla", Year = 2023, Vin = "VIN606", Color = "White" };
        _context.Vehicles.Add(vehicle);
        await _context.SaveChangesAsync();

        var adminIdentity = new Microsoft.AspNetCore.Identity.IdentityUser { Id = "admin-user", UserName = "admin@test.com", Email = "admin@test.com" };
        var frontDeskIdentity = new Microsoft.AspNetCore.Identity.IdentityUser { Id = "fd-user", UserName = "fd@test.com", Email = "fd@test.com" };
        _context.Users.AddRange(adminIdentity, frontDeskIdentity);
        await _context.SaveChangesAsync();

        var adminUser = new User { IdentityUserId = adminIdentity.Id, FullName = "Admin Alex", Email = "admin@test.com", Role = UserRole.Admin };
        var frontDeskUser = new User { IdentityUserId = frontDeskIdentity.Id, FullName = "Desk Dana", Email = "fd@test.com", Role = UserRole.FrontDesk };
        _context.StaffUsers.AddRange(adminUser, frontDeskUser);
        await _context.SaveChangesAsync();

        var job = await _serviceJobService.CreateServiceJobAsync(new ServiceJobDto { VehicleId = vehicle.Id, JobType = JobType.RoutineService });

        // Act & Assert for Admin: Must fail with BR-003
        var exAdmin = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _serviceJobService.AssignMechanicAsync(job.Id, adminUser.Id, RoleInJob.Lead));
        Assert.Equal("BR-003", exAdmin.RuleCode);
        Assert.Contains("Only users with the Mechanic role can be assigned", exAdmin.Message);

        // Act & Assert for FrontDesk: Must fail with BR-003
        var exFd = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _serviceJobService.AssignMechanicAsync(job.Id, frontDeskUser.Id, RoleInJob.Assistant));
        Assert.Equal("BR-003", exFd.RuleCode);
        Assert.Contains("Only users with the Mechanic role can be assigned", exFd.Message);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private sealed class FakeNotificationService : INotificationService
    {
        private readonly List<NotificationDto> _notifications = new();
        private int _nextId = 1;

        public Task<IEnumerable<NotificationDto>> GetAllNotificationsAsync() => throw new NotImplementedException();

        public Task<NotificationDto?> GetNotificationByIdAsync(int id) => throw new NotImplementedException();

        public Task<IEnumerable<NotificationDto>> GetNotificationsByServiceJobAsync(int serviceJobId) => throw new NotImplementedException();

        public Task<IEnumerable<NotificationDto>> GetPendingNotificationsAsync() => throw new NotImplementedException();

        public Task<NotificationDto> CreateNotificationAsync(NotificationDto notification)
        {
            notification.Id = _nextId++;
            _notifications.Add(notification);
            return Task.FromResult(notification);
        }

        public Task<NotificationDto> RespondToNotificationAsync(int notificationId, NotificationStatus status) => throw new NotImplementedException();
    }
}
