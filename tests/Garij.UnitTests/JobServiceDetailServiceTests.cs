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

/// <summary>
/// Covers attaching catalogue services (labour) to a job - the counterpart to
/// PartsInventoryServiceTests' usage logging. Runs against SQLite rather than fakes
/// because the service reads the job through GetByIdWithDetailsAsync's eager loads.
/// </summary>
public class JobServiceDetailServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly GarijDbContext _context;
    private readonly JobServiceDetailService _jobServiceDetailService;

    public JobServiceDetailServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<GarijDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new GarijDbContext(options);
        _context.Database.EnsureCreated();

        _jobServiceDetailService = new JobServiceDetailService(
            new ServiceCatalogRepository(_context),
            new JobServiceDetailRepository(_context),
            new ServiceJobRepository(_context));
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private async Task<(int jobId, int catalogueId)> SeedJobAndCatalogueAsync(decimal basePrice = 80.00m)
    {
        var customer = new Customer { FullName = "Arif Hossain", Email = $"arif-{Guid.NewGuid():N}@test.local", PhoneNumber = "123", Address = "Dhaka" };
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync();

        var vehicle = new Vehicle { CustomerId = customer.Id, LicensePlateNumber = "DHA-7070", Make = "Toyota", Model = "Axio", Year = 2020, Vin = $"VIN-{Guid.NewGuid():N}"[..16], Color = "Blue" };
        _context.Vehicles.Add(vehicle);
        await _context.SaveChangesAsync();

        var job = new ServiceJob
        {
            CustomerId = customer.Id,
            VehicleId = vehicle.Id,
            BookingReference = $"GRJ-{Guid.NewGuid():N}"[..12],
            JobType = JobType.RoutineService,
            Status = JobStatus.InProgress,
            CreatedAt = DateTime.UtcNow
        };
        _context.ServiceJobs.Add(job);

        var catalogService = new ServiceCatalog { Name = "Engine Computer Diagnostic", Description = "Full OBD-II scan", BasePrice = basePrice, EstimatedDurationMinutes = 60 };
        _context.ServiceCatalogs.Add(catalogService);
        await _context.SaveChangesAsync();

        return (job.Id, catalogService.Id);
    }

    [Fact]
    public async Task LogJobServiceAsync_AttachesLabourLineToJob()
    {
        // Arrange
        var (jobId, catalogueId) = await SeedJobAndCatalogueAsync(basePrice: 80.00m);

        // Act
        var line = await _jobServiceDetailService.LogJobServiceAsync(new JobServiceDetailDto
        {
            ServiceJobId = jobId,
            ServiceCatalogId = catalogueId,
            Quantity = 2
        });

        // Assert
        Assert.True(line.Id > 0);
        Assert.Equal(80.00m, line.PriceAtBooking);
        Assert.Equal("Engine Computer Diagnostic", line.ServiceName);

        var persisted = await _context.JobServiceDetails.SingleAsync(d => d.ServiceJobId == jobId);
        Assert.Equal(catalogueId, persisted.ServiceCatalogId);
        Assert.Equal(2, persisted.Quantity);
        Assert.Equal(80.00m, persisted.PriceAtBooking);
    }

    [Fact]
    public async Task LogJobServiceAsync_CapturesPriceAtBooking_UnaffectedByLaterCataloguePriceChanges()
    {
        // Arrange: same price-snapshot guarantee parts get through PriceAtUsage (TC-PRT-12).
        var (jobId, catalogueId) = await SeedJobAndCatalogueAsync(basePrice: 80.00m);

        var line = await _jobServiceDetailService.LogJobServiceAsync(new JobServiceDetailDto
        {
            ServiceJobId = jobId,
            ServiceCatalogId = catalogueId,
            Quantity = 1
        });
        Assert.Equal(80.00m, line.PriceAtBooking);

        // Act: the catalogue is re-priced after the labour was booked.
        var catalogService = await _context.ServiceCatalogs.FindAsync(catalogueId);
        catalogService!.BasePrice = 150.00m;
        await _context.SaveChangesAsync();

        // Assert: the booked line keeps the price it was attached at.
        var persisted = await _context.JobServiceDetails.SingleAsync(d => d.ServiceJobId == jobId);
        Assert.Equal(80.00m, persisted.PriceAtBooking);
        Assert.Equal(150.00m, catalogService.BasePrice);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task LogJobServiceAsync_ThrowsValidationException_WhenQuantityIsNotPositive(int quantity)
    {
        // Arrange
        var (jobId, catalogueId) = await SeedJobAndCatalogueAsync();

        // Act & Assert
        await Assert.ThrowsAsync<ValidationException>(() =>
            _jobServiceDetailService.LogJobServiceAsync(new JobServiceDetailDto
            {
                ServiceJobId = jobId,
                ServiceCatalogId = catalogueId,
                Quantity = quantity
            }));

        Assert.Empty(_context.JobServiceDetails.Where(d => d.ServiceJobId == jobId));
    }

    [Fact]
    public async Task LogJobServiceAsync_ThrowsNotFoundException_WhenCatalogueServiceDoesNotExist()
    {
        // Arrange
        var (jobId, _) = await SeedJobAndCatalogueAsync();

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _jobServiceDetailService.LogJobServiceAsync(new JobServiceDetailDto
            {
                ServiceJobId = jobId,
                ServiceCatalogId = 9999,
                Quantity = 1
            }));
    }

    [Fact]
    public async Task LogJobServiceAsync_ThrowsNotFoundException_WhenJobDoesNotExist()
    {
        // Arrange
        var (_, catalogueId) = await SeedJobAndCatalogueAsync();

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _jobServiceDetailService.LogJobServiceAsync(new JobServiceDetailDto
            {
                ServiceJobId = 9999,
                ServiceCatalogId = catalogueId,
                Quantity = 1
            }));
    }

    [Fact]
    public async Task GetServicesForJobAsync_ReturnsAttachedLabourWithCatalogueNames()
    {
        // Arrange
        var (jobId, catalogueId) = await SeedJobAndCatalogueAsync(basePrice: 45.50m);
        await _jobServiceDetailService.LogJobServiceAsync(new JobServiceDetailDto { ServiceJobId = jobId, ServiceCatalogId = catalogueId, Quantity = 3 });

        // Act
        var lines = (await _jobServiceDetailService.GetServicesForJobAsync(jobId)).ToList();

        // Assert
        var line = Assert.Single(lines);
        Assert.Equal("Engine Computer Diagnostic", line.ServiceName);
        Assert.Equal(3, line.Quantity);
        Assert.Equal(45.50m, line.PriceAtBooking);
    }

    [Fact]
    public async Task GetServiceCatalogAsync_ReturnsCatalogueOrderedByName()
    {
        // Arrange
        await SeedJobAndCatalogueAsync();
        _context.ServiceCatalogs.Add(new ServiceCatalog { Name = "AC Service & Gas Refill", Description = "Refill", BasePrice = 95.00m, EstimatedDurationMinutes = 60 });
        await _context.SaveChangesAsync();

        // Act
        var catalogue = (await _jobServiceDetailService.GetServiceCatalogAsync()).ToList();

        // Assert
        Assert.Equal(2, catalogue.Count);
        Assert.Equal("AC Service & Gas Refill", catalogue[0].Name);
        Assert.Equal("Engine Computer Diagnostic", catalogue[1].Name);
    }

    [Fact]
    public async Task RemoveJobServiceAsync_DetachesLabourLineFromJob()
    {
        // Arrange
        var (jobId, catalogueId) = await SeedJobAndCatalogueAsync();
        var line = await _jobServiceDetailService.LogJobServiceAsync(new JobServiceDetailDto { ServiceJobId = jobId, ServiceCatalogId = catalogueId, Quantity = 1 });

        // Act
        await _jobServiceDetailService.RemoveJobServiceAsync(line.Id);

        // Assert
        Assert.Empty(_context.JobServiceDetails.Where(d => d.ServiceJobId == jobId));
    }
}
