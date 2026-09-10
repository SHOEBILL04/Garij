using Garij.Application.DTOs;
using Garij.Application.Interfaces;
using Garij.Application.Services;
using Garij.Domain.Entities;
using Garij.Domain.Enums;
using Garij.Domain.Exceptions;
using Garij.Infrastructure.Persistence;
using Garij.Infrastructure.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Garij.IntegrationTests;

/// <summary>
/// Comprehensive integration tests for ServiceJob status transitions, the status state machine,
/// illegal transition rejections (BR-007), job board multi-attribute filtering & sorting,
/// and vehicle maintenance predictions computed from recorded service history.
/// </summary>
public class ServiceJobStatusTransitionIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<GarijDbContext> _options;

    public ServiceJobStatusTransitionIntegrationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<GarijDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new GarijDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private (ServiceJobService service, GarijDbContext context) CreateServiceJobService(GarijDbContext context)
    {
        var jobRepo = new ServiceJobRepository(context);
        var vehicleRepo = new VehicleRepository(context);
        var userRepo = new UserRepository(context);
        var assignmentRepo = new MechanicAssignmentRepository(context);
        var notificationRepo = new NotificationRepository(context);
        var notificationService = new NotificationService(notificationRepo);

        var service = new ServiceJobService(jobRepo, vehicleRepo, userRepo, assignmentRepo, notificationService);
        return (service, context);
    }

    private async Task<(int customerId, int vehicleId, int mechanicId)> SeedBasicEntitiesAsync(GarijDbContext context)
    {
        var customer = new Customer
        {
            FullName = "Rahim Chowdhury",
            Email = "rahim@example.com",
            PhoneNumber = "+8801711000001",
            Address = "Dhaka, Bangladesh",
            CreatedAt = DateTime.UtcNow
        };
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        var vehicle = new Vehicle
        {
            CustomerId = customer.Id,
            LicensePlateNumber = "DHA-4455",
            Make = "Toyota",
            Model = "Allion",
            Year = 2021,
            Vin = "JT1234567890",
            Color = "Silver"
        };
        context.Vehicles.Add(vehicle);

        var identityUser = new IdentityUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = "tariqul@garij.local",
            Email = "tariqul@garij.local"
        };
        context.Users.Add(identityUser);
        await context.SaveChangesAsync();

        var mechanic = new User
        {
            IdentityUserId = identityUser.Id,
            FullName = "Tariqul Mechanic",
            Email = "tariqul@garij.local",
            PhoneNumber = "+8801811000002",
            Role = UserRole.Mechanic,
            CreatedAt = DateTime.UtcNow
        };
        context.StaffUsers.Add(mechanic);
        await context.SaveChangesAsync();

        return (customer.Id, vehicle.Id, mechanic.Id);
    }

    [Fact]
    public async Task StatusStateMachine_LegalLifecycleSequence_AdvancesSuccessfullyThroughAllStages()
    {
        // Arrange
        await using var context = new GarijDbContext(_options);
        var (service, _) = CreateServiceJobService(context);
        var (_, vehicleId, mechanicId) = await SeedBasicEntitiesAsync(context);

        var created = await service.CreateServiceJobAsync(new ServiceJobDto
        {
            VehicleId = vehicleId,
            JobType = JobType.RoutineService,
            Status = JobStatus.Requested
        });

        await service.AssignMechanicAsync(created.Id, mechanicId, RoleInJob.Lead);

        // Step 1: Requested -> InspectionPending
        var step1 = await service.UpdateServiceJobStatusAsync(created.Id, JobStatus.InspectionPending);
        Assert.Equal(JobStatus.InspectionPending, step1.Status);

        // Step 2: InspectionPending -> CustomerApprovalNeeded
        var step2 = await service.UpdateServiceJobStatusAsync(created.Id, JobStatus.CustomerApprovalNeeded);
        Assert.Equal(JobStatus.CustomerApprovalNeeded, step2.Status);

        // Step 3: CustomerApprovalNeeded -> InProgress
        var step3 = await service.UpdateServiceJobStatusAsync(created.Id, JobStatus.InProgress);
        Assert.Equal(JobStatus.InProgress, step3.Status);

        // Step 4: InProgress -> Completed
        var step4 = await service.UpdateServiceJobStatusAsync(created.Id, JobStatus.Completed);
        Assert.Equal(JobStatus.Completed, step4.Status);
        Assert.NotNull(step4.CompletedAt);

        // Verify in database
        var persisted = await context.ServiceJobs.FindAsync(created.Id);
        Assert.NotNull(persisted);
        Assert.Equal(JobStatus.Completed, persisted.Status);
        Assert.NotNull(persisted.CompletedAt);
    }

    [Theory]
    [InlineData(JobStatus.Requested)]
    [InlineData(JobStatus.InspectionPending)]
    [InlineData(JobStatus.CustomerApprovalNeeded)]
    [InlineData(JobStatus.InProgress)]
    public async Task StatusStateMachine_CanCancelFromAnyActiveState(JobStatus startingStatus)
    {
        // Arrange
        await using var context = new GarijDbContext(_options);
        var (service, _) = CreateServiceJobService(context);
        var (_, vehicleId, _) = await SeedBasicEntitiesAsync(context);

        var created = await service.CreateServiceJobAsync(new ServiceJobDto
        {
            VehicleId = vehicleId,
            JobType = JobType.Repair,
            Status = JobStatus.Requested
        });

        // Fast-forward to startingStatus
        if (startingStatus == JobStatus.InspectionPending)
        {
            await service.UpdateServiceJobStatusAsync(created.Id, JobStatus.InspectionPending);
        }
        else if (startingStatus == JobStatus.CustomerApprovalNeeded)
        {
            await service.UpdateServiceJobStatusAsync(created.Id, JobStatus.InspectionPending);
            await service.UpdateServiceJobStatusAsync(created.Id, JobStatus.CustomerApprovalNeeded);
        }
        else if (startingStatus == JobStatus.InProgress)
        {
            await service.UpdateServiceJobStatusAsync(created.Id, JobStatus.InspectionPending);
            await service.UpdateServiceJobStatusAsync(created.Id, JobStatus.CustomerApprovalNeeded);
            await service.UpdateServiceJobStatusAsync(created.Id, JobStatus.InProgress);
        }

        // Act - Cancel
        var result = await service.UpdateServiceJobStatusAsync(created.Id, JobStatus.Cancelled);

        // Assert
        Assert.Equal(JobStatus.Cancelled, result.Status);

        var persisted = await context.ServiceJobs.FindAsync(created.Id);
        Assert.NotNull(persisted);
        Assert.Equal(JobStatus.Cancelled, persisted.Status);
    }

    [Theory]
    [InlineData(JobStatus.Requested, JobStatus.CustomerApprovalNeeded)]
    [InlineData(JobStatus.Requested, JobStatus.InProgress)]
    [InlineData(JobStatus.Requested, JobStatus.Completed)]
    [InlineData(JobStatus.InspectionPending, JobStatus.InProgress)]
    [InlineData(JobStatus.InspectionPending, JobStatus.Completed)]
    [InlineData(JobStatus.CustomerApprovalNeeded, JobStatus.Completed)]
    public async Task StatusStateMachine_CanSkipAheadToLaterStages_AdvancesSuccessfully(
        JobStatus currentStatus, JobStatus forwardTargetStatus)
    {
        // Arrange
        await using var context = new GarijDbContext(_options);
        var (service, _) = CreateServiceJobService(context);
        var (_, vehicleId, _) = await SeedBasicEntitiesAsync(context);

        var created = await service.CreateServiceJobAsync(new ServiceJobDto
        {
            VehicleId = vehicleId,
            JobType = JobType.RoutineService,
            Status = JobStatus.Requested
        });

        // Advance to currentStatus
        if (currentStatus == JobStatus.InspectionPending)
        {
            await service.UpdateServiceJobStatusAsync(created.Id, JobStatus.InspectionPending);
        }
        else if (currentStatus == JobStatus.CustomerApprovalNeeded)
        {
            await service.UpdateServiceJobStatusAsync(created.Id, JobStatus.CustomerApprovalNeeded);
        }

        // Act
        var result = await service.UpdateServiceJobStatusAsync(created.Id, forwardTargetStatus);

        // Assert
        Assert.Equal(forwardTargetStatus, result.Status);

        var persisted = await context.ServiceJobs.FindAsync(created.Id);
        Assert.NotNull(persisted);
        Assert.Equal(forwardTargetStatus, persisted.Status);
    }

    [Theory]
    // Illegal backwards transitions (cannot move backward in pipeline)
    [InlineData(JobStatus.InspectionPending, JobStatus.Requested)]
    [InlineData(JobStatus.CustomerApprovalNeeded, JobStatus.Requested)]
    [InlineData(JobStatus.CustomerApprovalNeeded, JobStatus.InspectionPending)]
    [InlineData(JobStatus.InProgress, JobStatus.Requested)]
    [InlineData(JobStatus.InProgress, JobStatus.InspectionPending)]
    [InlineData(JobStatus.InProgress, JobStatus.CustomerApprovalNeeded)]
    public async Task StatusStateMachine_IllegalTransitions_ThrowsBusinessRuleExceptionAndRetainsState(
        JobStatus currentStatus, JobStatus illegalTargetStatus)
    {
        // Arrange
        await using var context = new GarijDbContext(_options);
        var (service, _) = CreateServiceJobService(context);
        var (_, vehicleId, _) = await SeedBasicEntitiesAsync(context);

        var created = await service.CreateServiceJobAsync(new ServiceJobDto
        {
            VehicleId = vehicleId,
            JobType = JobType.RoutineService,
            Status = JobStatus.Requested
        });

        // Advance to currentStatus legally
        if (currentStatus == JobStatus.InspectionPending)
        {
            await service.UpdateServiceJobStatusAsync(created.Id, JobStatus.InspectionPending);
        }
        else if (currentStatus == JobStatus.CustomerApprovalNeeded)
        {
            await service.UpdateServiceJobStatusAsync(created.Id, JobStatus.CustomerApprovalNeeded);
        }
        else if (currentStatus == JobStatus.InProgress)
        {
            await service.UpdateServiceJobStatusAsync(created.Id, JobStatus.InProgress);
        }

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.UpdateServiceJobStatusAsync(created.Id, illegalTargetStatus));

        Assert.Equal("BR-007", ex.RuleCode);

        // Verify DB unchanged
        var persisted = await context.ServiceJobs.FindAsync(created.Id);
        Assert.NotNull(persisted);
        Assert.Equal(currentStatus, persisted.Status);
    }

    [Theory]
    [InlineData(JobStatus.Requested)]
    [InlineData(JobStatus.InspectionPending)]
    [InlineData(JobStatus.CustomerApprovalNeeded)]
    [InlineData(JobStatus.InProgress)]
    [InlineData(JobStatus.Cancelled)]
    public async Task StatusStateMachine_CompletedJob_IsTerminal_ThrowsBusinessRuleException(JobStatus newStatus)
    {
        // Arrange
        await using var context = new GarijDbContext(_options);
        var (service, _) = CreateServiceJobService(context);
        var (_, vehicleId, _) = await SeedBasicEntitiesAsync(context);

        var created = await service.CreateServiceJobAsync(new ServiceJobDto
        {
            VehicleId = vehicleId,
            JobType = JobType.RoutineService,
            Status = JobStatus.Requested
        });

        await service.UpdateServiceJobStatusAsync(created.Id, JobStatus.InspectionPending);
        await service.UpdateServiceJobStatusAsync(created.Id, JobStatus.CustomerApprovalNeeded);
        await service.UpdateServiceJobStatusAsync(created.Id, JobStatus.InProgress);
        await service.UpdateServiceJobStatusAsync(created.Id, JobStatus.Completed);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.UpdateServiceJobStatusAsync(created.Id, newStatus));

        Assert.Equal("BR-007", ex.RuleCode);
    }

    [Theory]
    [InlineData(JobStatus.Requested)]
    [InlineData(JobStatus.InspectionPending)]
    [InlineData(JobStatus.CustomerApprovalNeeded)]
    [InlineData(JobStatus.InProgress)]
    [InlineData(JobStatus.Completed)]
    public async Task StatusStateMachine_CancelledJob_IsTerminal_ThrowsBusinessRuleException(JobStatus newStatus)
    {
        // Arrange
        await using var context = new GarijDbContext(_options);
        var (service, _) = CreateServiceJobService(context);
        var (_, vehicleId, _) = await SeedBasicEntitiesAsync(context);

        var created = await service.CreateServiceJobAsync(new ServiceJobDto
        {
            VehicleId = vehicleId,
            JobType = JobType.RoutineService,
            Status = JobStatus.Requested
        });

        await service.UpdateServiceJobStatusAsync(created.Id, JobStatus.Cancelled);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.UpdateServiceJobStatusAsync(created.Id, newStatus));

        Assert.Equal("BR-007", ex.RuleCode);
    }

    [Fact]
    public async Task JobBoard_GetFilteredServiceJobsAsync_FiltersByStatusMechanicAndSortsByDate()
    {
        // Arrange
        await using var context = new GarijDbContext(_options);
        var (service, _) = CreateServiceJobService(context);
        var (customerId, vehicleId, mechanicId) = await SeedBasicEntitiesAsync(context);

        var identityUser2 = new IdentityUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = "kabir@garij.local",
            Email = "kabir@garij.local"
        };
        context.Users.Add(identityUser2);
        await context.SaveChangesAsync();

        var mechanic2 = new User
        {
            IdentityUserId = identityUser2.Id,
            FullName = "Kabir Mechanic",
            Email = "kabir@garij.local",
            PhoneNumber = "+8801811000003",
            Role = UserRole.Mechanic,
            CreatedAt = DateTime.UtcNow
        };
        context.StaffUsers.Add(mechanic2);
        await context.SaveChangesAsync();

        // Job 1: Older, Requested, assigned to mechanic 1
        var job1 = new ServiceJob
        {
            VehicleId = vehicleId,
            CustomerId = customerId,
            BookingReference = "GRJ-2026-0001",
            JobType = JobType.RoutineService,
            Status = JobStatus.Requested,
            CreatedAt = DateTime.UtcNow.AddDays(-5)
        };
        job1.MechanicAssignments.Add(new MechanicAssignment { UserId = mechanicId, RoleInJob = RoleInJob.Lead, AssignedAt = DateTime.UtcNow });
        context.ServiceJobs.Add(job1);

        // Job 2: Middle, InProgress, assigned to mechanic 2
        var job2 = new ServiceJob
        {
            VehicleId = vehicleId,
            CustomerId = customerId,
            BookingReference = "GRJ-2026-0002",
            JobType = JobType.Repair,
            Status = JobStatus.InProgress,
            CreatedAt = DateTime.UtcNow.AddDays(-2)
        };
        job2.MechanicAssignments.Add(new MechanicAssignment { UserId = mechanic2.Id, RoleInJob = RoleInJob.Lead, AssignedAt = DateTime.UtcNow });
        context.ServiceJobs.Add(job2);

        // Job 3: Newest, InProgress, assigned to mechanic 1
        var job3 = new ServiceJob
        {
            VehicleId = vehicleId,
            CustomerId = customerId,
            BookingReference = "GRJ-2026-0003",
            JobType = JobType.RoutineService,
            Status = JobStatus.InProgress,
            CreatedAt = DateTime.UtcNow.AddHours(-1)
        };
        job3.MechanicAssignments.Add(new MechanicAssignment { UserId = mechanicId, RoleInJob = RoleInJob.Lead, AssignedAt = DateTime.UtcNow });
        context.ServiceJobs.Add(job3);

        await context.SaveChangesAsync();

        // Test 1: Filter by status InProgress
        var inProgressJobs = await service.GetFilteredServiceJobsAsync(status: JobStatus.InProgress);
        Assert.Equal(2, inProgressJobs.Count());
        Assert.All(inProgressJobs, j => Assert.Equal(JobStatus.InProgress, j.Status));

        // Test 2: Filter by mechanicId
        var mech1Jobs = await service.GetFilteredServiceJobsAsync(mechanicId: mechanicId);
        Assert.Equal(2, mech1Jobs.Count());
        Assert.Contains(mech1Jobs, j => j.BookingReference == "GRJ-2026-0001");
        Assert.Contains(mech1Jobs, j => j.BookingReference == "GRJ-2026-0003");

        // Test 3: Filter by status AND mechanic
        var mech1InProgress = await service.GetFilteredServiceJobsAsync(status: JobStatus.InProgress, mechanicId: mechanicId);
        Assert.Single(mech1InProgress);
        Assert.Equal("GRJ-2026-0003", mech1InProgress.First().BookingReference);

        // Test 4: Sort by date ascending (oldest first)
        var oldestFirst = (await service.GetFilteredServiceJobsAsync(sortBy: "date_asc")).ToList();
        Assert.Equal(3, oldestFirst.Count);
        Assert.Equal("GRJ-2026-0001", oldestFirst[0].BookingReference);
        Assert.Equal("GRJ-2026-0002", oldestFirst[1].BookingReference);
        Assert.Equal("GRJ-2026-0003", oldestFirst[2].BookingReference);

        // Test 5: Sort by date descending (newest first)
        var newestFirst = (await service.GetFilteredServiceJobsAsync(sortBy: "date_desc")).ToList();
        Assert.Equal(3, newestFirst.Count);
        Assert.Equal("GRJ-2026-0003", newestFirst[0].BookingReference);
        Assert.Equal("GRJ-2026-0002", newestFirst[1].BookingReference);
        Assert.Equal("GRJ-2026-0001", newestFirst[2].BookingReference);
    }

    [Fact]
    public async Task IntelligenceService_FlagVehiclesDueForServiceAsync_CalculatesPredictionsFromHistory()
    {
        // Arrange
        await using var context = new GarijDbContext(_options);
        var intelligenceService = new IntelligenceService(context);

        var customer = new Customer
        {
            FullName = "Dr. Shamsul Huda",
            Email = "shamsul@example.com",
            PhoneNumber = "+8801911223344",
            Address = "Uttara, Dhaka",
            CreatedAt = DateTime.UtcNow
        };
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        // Vehicle A: Had 2 services in the past (120 days ago and 60 days ago) -> 60-day interval -> Due now! (Overdue)
        var vehicleA = new Vehicle
        {
            CustomerId = customer.Id,
            LicensePlateNumber = "DHA-1122",
            Make = "Honda",
            Model = "Civic",
            Year = 2020,
            Vin = "HND112233",
            Color = "Blue"
        };
        context.Vehicles.Add(vehicleA);

        // Vehicle B: Had service 10 days ago (90-day standard cycle) -> Upcoming (not due soon or overdue)
        var vehicleB = new Vehicle
        {
            CustomerId = customer.Id,
            LicensePlateNumber = "DHA-3344",
            Make = "Toyota",
            Model = "Corolla",
            Year = 2022,
            Vin = "TYT334455",
            Color = "White"
        };
        context.Vehicles.Add(vehicleB);

        // Vehicle C: Brand new vehicle, never serviced -> Initial checkup overdue
        var vehicleC = new Vehicle
        {
            CustomerId = customer.Id,
            LicensePlateNumber = "DHA-9988",
            Make = "Nissan",
            Model = "X-Trail",
            Year = 2023,
            Vin = "NSN998877",
            Color = "Black"
        };
        context.Vehicles.Add(vehicleC);
        await context.SaveChangesAsync();

        // Seed Vehicle A jobs:
        // Job A1: Completed 120 days ago
        context.ServiceJobs.Add(new ServiceJob
        {
            VehicleId = vehicleA.Id,
            CustomerId = customer.Id,
            BookingReference = "GRJ-2026-A001",
            JobType = JobType.RoutineService,
            Status = JobStatus.Completed,
            CreatedAt = DateTime.UtcNow.AddDays(-120),
            CompletedAt = DateTime.UtcNow.AddDays(-120)
        });
        // Job A2: Completed 65 days ago (interval = 55 days)
        context.ServiceJobs.Add(new ServiceJob
        {
            VehicleId = vehicleA.Id,
            CustomerId = customer.Id,
            BookingReference = "GRJ-2026-A002",
            JobType = JobType.RoutineService,
            Status = JobStatus.Completed,
            CreatedAt = DateTime.UtcNow.AddDays(-65),
            CompletedAt = DateTime.UtcNow.AddDays(-65)
        });

        // Seed Vehicle B job:
        // Job B1: Completed 10 days ago
        context.ServiceJobs.Add(new ServiceJob
        {
            VehicleId = vehicleB.Id,
            CustomerId = customer.Id,
            BookingReference = "GRJ-2026-B001",
            JobType = JobType.RoutineService,
            Status = JobStatus.Completed,
            CreatedAt = DateTime.UtcNow.AddDays(-10),
            CompletedAt = DateTime.UtcNow.AddDays(-10)
        });

        await context.SaveChangesAsync();

        // Act
        var predictions = (await intelligenceService.FlagVehiclesDueForServiceAsync()).ToList();

        // Assert
        Assert.Contains(predictions, p => p.LicensePlateNumber == "DHA-1122"); // Overdue based on ~55-day cycle
        Assert.Contains(predictions, p => p.LicensePlateNumber == "DHA-9988"); // Overdue initial baseline inspection
        Assert.DoesNotContain(predictions, p => p.LicensePlateNumber == "DHA-3344"); // Healthy, serviced 10 days ago

        var predA = predictions.First(p => p.LicensePlateNumber == "DHA-1122");
        Assert.Equal("Overdue", predA.UrgencyLevel);
        Assert.True(predA.DaysOverdue > 0);
        Assert.Equal(2, predA.TotalServicesCompleted);
        Assert.NotEmpty(predA.RecommendedService);

        var predC = predictions.First(p => p.LicensePlateNumber == "DHA-9988");
        Assert.Equal("Overdue", predC.UrgencyLevel);
        Assert.Equal(0, predC.TotalServicesCompleted);
        Assert.Contains("Initial", predC.RecommendedService);
    }
}
