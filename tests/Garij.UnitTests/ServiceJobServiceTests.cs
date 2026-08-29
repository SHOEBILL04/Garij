using Garij.Application.DTOs;
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

        _serviceJobService = new ServiceJobService(jobRepo, vehicleRepo, userRepo, assignmentRepo);
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

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
