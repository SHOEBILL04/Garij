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
    private readonly ICurrentGarageService? _currentGarageService;

    public CustomerVehicleService(
        ICustomerRepository customers,
        IVehicleRepository vehicles,
        IServiceJobRepository serviceJobs,
        ICurrentGarageService? currentGarageService = null)
    {
        _customers = customers;
        _vehicles = vehicles;
        _serviceJobs = serviceJobs;
        _currentGarageService = currentGarageService;
    }

    private async Task<string> ResolveGarageIdAsync(string? explicitGarageId = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitGarageId))
        {
            return explicitGarageId;
        }

        if (_currentGarageService != null)
        {
            var id = await _currentGarageService.GetCurrentGarageIdAsync();
            if (!string.IsNullOrWhiteSpace(id))
            {
                return id;
            }
        }

        return "default-garij-master";
    }

    public async Task<IEnumerable<CustomerDto>> GetAllCustomersAsync()
    {
        var garageId = await ResolveGarageIdAsync();
        var customers = await _customers.GetAllAsync();
        return customers
            .Where(c => (c.GarageId ?? "default-garij-master") == garageId)
            .OrderBy(c => c.FullName)
            .Select(MapCustomer);
    }

    public async Task<CustomerDto?> GetCustomerByIdAsync(int id)
    {
        var garageId = await ResolveGarageIdAsync();
        var customer = await _customers.GetByIdWithVehiclesAsync(id);
        if (customer is null || (customer.GarageId ?? "default-garij-master") != garageId)
        {
            return null;
        }

        return MapCustomer(customer);
    }

    public async Task<CustomerDto> CreateCustomerAsync(CustomerDto customer)
    {
        var garageId = await ResolveGarageIdAsync(customer.GarageId);
        var entity = new Customer
        {
            FullName = customer.FullName.Trim(),
            Email = customer.Email.Trim(),
            PhoneNumber = customer.PhoneNumber.Trim(),
            Address = customer.Address.Trim(),
            GarageId = garageId,
            CreatedAt = DateTime.UtcNow
        };

        await _customers.AddAsync(entity);
        await _customers.SaveChangesAsync();

        return MapCustomer(entity);
    }

    public async Task<CustomerDto> UpdateCustomerAsync(CustomerDto customer)
    {
        var garageId = await ResolveGarageIdAsync();
        var entity = await _customers.GetByIdAsync(customer.Id)
            ?? throw new NotFoundException(nameof(Customer), customer.Id);

        if ((entity.GarageId ?? "default-garij-master") != garageId)
        {
            throw new NotFoundException(nameof(Customer), customer.Id);
        }

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
        var garageId = await ResolveGarageIdAsync();
        var entity = await _customers.GetByIdWithVehiclesAsync(id)
            ?? throw new NotFoundException(nameof(Customer), id);

        if ((entity.GarageId ?? "default-garij-master") != garageId)
        {
            throw new NotFoundException(nameof(Customer), id);
        }

        if (entity.Vehicles.Any())
        {
            throw new BusinessRuleException("BR-001", "Customers with registered vehicles cannot be deleted.");
        }

        _customers.Remove(entity);
        await _customers.SaveChangesAsync();
    }

    public async Task<IEnumerable<VehicleDto>> GetVehiclesByCustomerAsync(int customerId)
    {
        var garageId = await ResolveGarageIdAsync();
        var customer = await _customers.GetByIdAsync(customerId);
        if (customer == null || (customer.GarageId ?? "default-garij-master") != garageId)
        {
            return Enumerable.Empty<VehicleDto>();
        }

        var vehicles = await _vehicles.GetByCustomerAsync(customerId);
        return vehicles
            .Where(v => (v.GarageId ?? "default-garij-master") == garageId)
            .Select(MapVehicle);
    }

    public async Task<VehicleDto?> GetVehicleByIdAsync(int id)
    {
        var garageId = await ResolveGarageIdAsync();
        var vehicle = await _vehicles.GetByIdWithCustomerAsync(id);
        if (vehicle is null)
        {
            return null;
        }

        if (garageId != "PUBLIC_ANONYMOUS" && (vehicle.GarageId ?? "default-garij-master") != garageId)
        {
            return null;
        }

        return MapVehicle(vehicle);
    }

    public async Task<VehicleDto?> GetVehicleByLicensePlateAsync(string licensePlateNumber)
    {
        var garageId = await ResolveGarageIdAsync();
        var normalizedPlate = NormalizePlate(licensePlateNumber);
        if (string.IsNullOrWhiteSpace(normalizedPlate))
        {
            return null;
        }

        var vehicle = await _vehicles.GetByLicensePlateAsync(normalizedPlate);
        if (vehicle is null)
        {
            return null;
        }

        if (garageId != "PUBLIC_ANONYMOUS" && (vehicle.GarageId ?? "default-garij-master") != garageId)
        {
            return null;
        }

        return MapVehicle(vehicle);
    }

    public async Task<IEnumerable<ServiceHistoryDto>> GetServiceHistoryByVehicleAsync(int vehicleId)
    {
        var garageId = await ResolveGarageIdAsync();
        var vehicle = await _vehicles.GetByIdAsync(vehicleId);
        if (vehicle is null)
        {
            return Enumerable.Empty<ServiceHistoryDto>();
        }

        if (garageId != "PUBLIC_ANONYMOUS" && (vehicle.GarageId ?? "default-garij-master") != garageId)
        {
            return Enumerable.Empty<ServiceHistoryDto>();
        }

        var jobs = await _serviceJobs.GetServiceHistoryByVehicleAsync(vehicleId);
        return jobs
            .Where(j => garageId == "PUBLIC_ANONYMOUS" || (j.GarageId ?? "default-garij-master") == garageId)
            .Select(job => new ServiceHistoryDto
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
        var garageId = await ResolveGarageIdAsync(vehicle.GarageId);
        await EnsureCustomerExists(vehicle.CustomerId, garageId);

        var normalizedPlate = NormalizePlate(vehicle.LicensePlateNumber);
        var existing = await _vehicles.GetByLicensePlateAsync(normalizedPlate);
        if (existing is not null && (existing.GarageId ?? "default-garij-master") == garageId)
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
            Color = vehicle.Color.Trim(),
            GarageId = garageId
        };

        await _vehicles.AddAsync(entity);
        await _vehicles.SaveChangesAsync();

        var saved = await _vehicles.GetByIdWithCustomerAsync(entity.Id);
        return MapVehicle(saved ?? entity);
    }

    public async Task<VehicleDto> UpdateVehicleAsync(VehicleDto vehicle)
    {
        var garageId = await ResolveGarageIdAsync(vehicle.GarageId);
        await EnsureCustomerExists(vehicle.CustomerId, garageId);

        var entity = await _vehicles.GetByIdAsync(vehicle.Id)
            ?? throw new NotFoundException(nameof(Vehicle), vehicle.Id);

        if ((entity.GarageId ?? "default-garij-master") != garageId)
        {
            throw new NotFoundException(nameof(Vehicle), vehicle.Id);
        }

        var normalizedPlate = NormalizePlate(vehicle.LicensePlateNumber);
        var duplicate = await _vehicles.GetByLicensePlateAsync(normalizedPlate);
        if (duplicate is not null && duplicate.Id != entity.Id && (duplicate.GarageId ?? "default-garij-master") == garageId)
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
        entity.GarageId = garageId;

        _vehicles.Update(entity);
        await _vehicles.SaveChangesAsync();

        var saved = await _vehicles.GetByIdWithCustomerAsync(entity.Id);
        return MapVehicle(saved ?? entity);
    }

    public async Task DeleteVehicleAsync(int id)
    {
        var garageId = await ResolveGarageIdAsync();
        var entity = await _vehicles.GetByIdWithCustomerAsync(id)
            ?? throw new NotFoundException(nameof(Vehicle), id);

        if ((entity.GarageId ?? "default-garij-master") != garageId)
        {
            throw new NotFoundException(nameof(Vehicle), id);
        }

        var history = await _serviceJobs.GetServiceHistoryByVehicleAsync(id);
        if (history.Where(j => (j.GarageId ?? "default-garij-master") == garageId).Any())
        {
            throw new BusinessRuleException("FR-004", "Vehicles with service history cannot be deleted.");
        }

        _vehicles.Remove(entity);
        await _vehicles.SaveChangesAsync();
    }

    private async Task EnsureCustomerExists(int customerId, string? garageId = null)
    {
        var customer = await _customers.GetByIdAsync(customerId);
        if (customer is null)
        {
            throw new NotFoundException(nameof(Customer), customerId);
        }

        if (garageId != null && (customer.GarageId ?? "default-garij-master") != garageId)
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
        Address = customer.Address,
        GarageId = customer.GarageId
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
        Color = vehicle.Color,
        GarageId = vehicle.GarageId
    };
}
