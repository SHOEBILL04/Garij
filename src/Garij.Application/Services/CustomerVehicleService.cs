using Garij.Application.DTOs;
using Garij.Application.Interfaces;
using Garij.Domain.Entities;
using Garij.Domain.Exceptions;
using Garij.Infrastructure.Repositories;

namespace Garij.Application.Services;

public class CustomerVehicleService : ICustomerVehicleService
{
    private readonly ICustomerRepository _customers;
    private readonly IVehicleRepository _vehicles;
    private readonly IServiceJobRepository _serviceJobs;

    public CustomerVehicleService(
        ICustomerRepository customers,
        IVehicleRepository vehicles,
        IServiceJobRepository serviceJobs)
    {
        _customers = customers;
        _vehicles = vehicles;
        _serviceJobs = serviceJobs;
    }

    public async Task<IEnumerable<CustomerDto>> GetAllCustomersAsync()
    {
        var customers = await _customers.GetAllAsync();
        return customers.OrderBy(c => c.FullName).Select(MapCustomer);
    }

    public async Task<CustomerDto?> GetCustomerByIdAsync(int id)
    {
        var customer = await _customers.GetByIdWithVehiclesAsync(id);
        return customer is null ? null : MapCustomer(customer);
    }

    public async Task<CustomerDto> CreateCustomerAsync(CustomerDto customer)
    {
        var entity = new Customer
        {
            FullName = customer.FullName.Trim(),
            Email = customer.Email.Trim(),
            PhoneNumber = customer.PhoneNumber.Trim(),
            Address = customer.Address.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        await _customers.AddAsync(entity);
        await _customers.SaveChangesAsync();

        return MapCustomer(entity);
    }

    public async Task<CustomerDto> UpdateCustomerAsync(CustomerDto customer)
    {
        var entity = await _customers.GetByIdAsync(customer.Id)
            ?? throw new NotFoundException(nameof(Customer), customer.Id);

        entity.FullName = customer.FullName.Trim();
        entity.Email = customer.Email.Trim();
        entity.PhoneNumber = customer.PhoneNumber.Trim();
        entity.Address = customer.Address.Trim();

        _customers.Update(entity);
        await _customers.SaveChangesAsync();

        return MapCustomer(entity);
    }

    public async Task DeleteCustomerAsync(int id)
    {
        var entity = await _customers.GetByIdWithVehiclesAsync(id)
            ?? throw new NotFoundException(nameof(Customer), id);

        if (entity.Vehicles.Any())
        {
            throw new BusinessRuleException("BR-001", "Customers with registered vehicles cannot be deleted.");
        }

        _customers.Remove(entity);
        await _customers.SaveChangesAsync();
    }

    public async Task<IEnumerable<VehicleDto>> GetVehiclesByCustomerAsync(int customerId)
    {
        var vehicles = await _vehicles.GetByCustomerAsync(customerId);
        return vehicles.Select(MapVehicle);
    }

    public async Task<VehicleDto?> GetVehicleByIdAsync(int id)
    {
        var vehicle = await _vehicles.GetByIdWithCustomerAsync(id);
        return vehicle is null ? null : MapVehicle(vehicle);
    }

    public async Task<VehicleDto?> GetVehicleByLicensePlateAsync(string licensePlateNumber)
    {
        var normalizedPlate = NormalizePlate(licensePlateNumber);
        if (string.IsNullOrWhiteSpace(normalizedPlate))
        {
            return null;
        }

        var vehicle = await _vehicles.GetByLicensePlateAsync(normalizedPlate);
        return vehicle is null ? null : MapVehicle(vehicle);
    }

    public async Task<IEnumerable<ServiceHistoryDto>> GetServiceHistoryByVehicleAsync(int vehicleId)
    {
        var jobs = await _serviceJobs.GetServiceHistoryByVehicleAsync(vehicleId);
        return jobs.Select(job => new ServiceHistoryDto
        {
            ServiceJobId = job.Id,
            BookingReference = job.BookingReference,
            JobType = job.JobType,
            Status = job.Status,
            CreatedAt = job.CreatedAt,
            CompletedAt = job.CompletedAt,
            VehiclePlate = job.Vehicle.LicensePlateNumber,
            VehicleDescription = $"{job.Vehicle.Year} {job.Vehicle.Make} {job.Vehicle.Model}".Trim()
        });
    }

    public async Task<VehicleDto> AddVehicleAsync(VehicleDto vehicle)
    {
        await EnsureCustomerExists(vehicle.CustomerId);

        var normalizedPlate = NormalizePlate(vehicle.LicensePlateNumber);
        if (await _vehicles.GetByLicensePlateAsync(normalizedPlate) is not null)
        {
            throw new BusinessRuleException("BR-002", "License plate number must be unique.");
        }

        var entity = new Vehicle
        {
            CustomerId = vehicle.CustomerId,
            LicensePlateNumber = normalizedPlate,
            Make = vehicle.Make.Trim(),
            Model = vehicle.Model.Trim(),
            Year = vehicle.Year,
            Vin = vehicle.Vin.Trim(),
            Color = vehicle.Color.Trim()
        };

        await _vehicles.AddAsync(entity);
        await _vehicles.SaveChangesAsync();

        var saved = await _vehicles.GetByIdWithCustomerAsync(entity.Id);
        return MapVehicle(saved ?? entity);
    }

    public async Task<VehicleDto> UpdateVehicleAsync(VehicleDto vehicle)
    {
        await EnsureCustomerExists(vehicle.CustomerId);

        var entity = await _vehicles.GetByIdAsync(vehicle.Id)
            ?? throw new NotFoundException(nameof(Vehicle), vehicle.Id);

        var normalizedPlate = NormalizePlate(vehicle.LicensePlateNumber);
        var duplicate = await _vehicles.GetByLicensePlateAsync(normalizedPlate);
        if (duplicate is not null && duplicate.Id != entity.Id)
        {
            throw new BusinessRuleException("BR-002", "License plate number must be unique.");
        }

        entity.CustomerId = vehicle.CustomerId;
        entity.LicensePlateNumber = normalizedPlate;
        entity.Make = vehicle.Make.Trim();
        entity.Model = vehicle.Model.Trim();
        entity.Year = vehicle.Year;
        entity.Vin = vehicle.Vin.Trim();
        entity.Color = vehicle.Color.Trim();

        _vehicles.Update(entity);
        await _vehicles.SaveChangesAsync();

        var saved = await _vehicles.GetByIdWithCustomerAsync(entity.Id);
        return MapVehicle(saved ?? entity);
    }

    public async Task DeleteVehicleAsync(int id)
    {
        var entity = await _vehicles.GetByIdWithCustomerAsync(id)
            ?? throw new NotFoundException(nameof(Vehicle), id);

        var history = await _serviceJobs.GetServiceHistoryByVehicleAsync(id);
        if (history.Any())
        {
            throw new BusinessRuleException("FR-004", "Vehicles with service history cannot be deleted.");
        }

        _vehicles.Remove(entity);
        await _vehicles.SaveChangesAsync();
    }

    private async Task EnsureCustomerExists(int customerId)
    {
        if (await _customers.GetByIdAsync(customerId) is null)
        {
            throw new NotFoundException(nameof(Customer), customerId);
        }
    }

    private static string NormalizePlate(string licensePlateNumber) =>
        licensePlateNumber.Trim().ToUpperInvariant();

    private static CustomerDto MapCustomer(Customer customer) => new()
    {
        Id = customer.Id,
        FullName = customer.FullName,
        Email = customer.Email,
        PhoneNumber = customer.PhoneNumber,
        Address = customer.Address
    };

    private static VehicleDto MapVehicle(Vehicle vehicle) => new()
    {
        Id = vehicle.Id,
        CustomerId = vehicle.CustomerId,
        CustomerName = vehicle.Customer?.FullName ?? string.Empty,
        LicensePlateNumber = vehicle.LicensePlateNumber,
        Make = vehicle.Make,
        Model = vehicle.Model,
        Year = vehicle.Year,
        Vin = vehicle.Vin,
        Color = vehicle.Color
    };
}
