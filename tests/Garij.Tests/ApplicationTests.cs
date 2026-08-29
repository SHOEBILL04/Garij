using Garij.Application.DTOs;
using Garij.Domain.Entities;
using Garij.Domain.Enums;
using Garij.Domain.Exceptions;
using Garij.Infrastructure.Persistence;
using Garij.Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using CustomerVehicleService = Garij.Application.Services.CustomerVehicleService;

namespace Garij.Tests;

public class ApplicationTests
{
    [Fact]
    public async Task CustomerVehicleService_AddVehicleRejectsDuplicatePlate()
    {
        using var database = CreateDatabase();
        var service = CreateService(database.Context);
        var customer = await service.CreateCustomerAsync(new CustomerDto
        {
            FullName = "Samia Tabassum",
            Email = "samia@example.com",
            PhoneNumber = "+8801711111111",
            Address = "Dhaka"
        });

        await service.AddVehicleAsync(new VehicleDto
        {
            CustomerId = customer.Id,
            LicensePlateNumber = "br-002",
            Make = "Toyota",
            Model = "Axio",
            Year = 2020
        });

        var exception = await Assert.ThrowsAsync<BusinessRuleException>(() => service.AddVehicleAsync(new VehicleDto
        {
            CustomerId = customer.Id,
            LicensePlateNumber = "BR-002",
            Make = "Honda",
            Model = "Civic",
            Year = 2021
        }));

        Assert.Equal("BR-002", exception.RuleCode);
    }

    [Fact]
    public async Task CustomerVehicleService_ReturnsServiceHistoryNewestFirst()
    {
        using var database = CreateDatabase();
        var service = CreateService(database.Context);
        var customer = await service.CreateCustomerAsync(new CustomerDto
        {
            FullName = "Amina Rahman",
            Email = "amina@example.com",
            PhoneNumber = "+8801722222222",
            Address = "Dhaka"
        });
        var vehicle = await service.AddVehicleAsync(new VehicleDto
        {
            CustomerId = customer.Id,
            LicensePlateNumber = "DHA-1001",
            Make = "Nissan",
            Model = "X-Trail",
            Year = 2019
        });

        database.Context.ServiceJobs.AddRange(
            new ServiceJob
            {
                CustomerId = customer.Id,
                VehicleId = vehicle.Id,
                BookingReference = "GRJ-2026-0001",
                JobType = JobType.RoutineService,
                Status = JobStatus.Completed,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                CompletedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)
            },
            new ServiceJob
            {
                CustomerId = customer.Id,
                VehicleId = vehicle.Id,
                BookingReference = "GRJ-2026-0002",
                JobType = JobType.Repair,
                Status = JobStatus.InProgress,
                CreatedAt = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)
            });
        await database.Context.SaveChangesAsync();

        var history = (await service.GetServiceHistoryByVehicleAsync(vehicle.Id)).ToList();

        Assert.Equal(2, history.Count);
        Assert.Equal("GRJ-2026-0002", history[0].BookingReference);
        Assert.Equal("GRJ-2026-0001", history[1].BookingReference);
    }

    private static CustomerVehicleService CreateService(GarijDbContext context) =>
        new(
            new CustomerRepository(context),
            new VehicleRepository(context),
            new ServiceJobRepository(context));

    private static TestDatabase CreateDatabase()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<GarijDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new GarijDbContext(options);
        context.Database.EnsureCreated();

        return new TestDatabase(connection, context);
    }

    private sealed class TestDatabase : IDisposable
    {
        private readonly SqliteConnection _connection;

        public TestDatabase(SqliteConnection connection, GarijDbContext context)
        {
            _connection = connection;
            Context = context;
        }

        public GarijDbContext Context { get; }

        public void Dispose()
        {
            Context.Dispose();
            _connection.Dispose();
        }
    }
}
