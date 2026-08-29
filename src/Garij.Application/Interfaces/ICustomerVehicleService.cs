using Garij.Application.DTOs;

namespace Garij.Application.Interfaces;

public interface ICustomerVehicleService
{
    Task<IEnumerable<CustomerDto>> GetAllCustomersAsync();

    Task<CustomerDto?> GetCustomerByIdAsync(int id);

    Task<CustomerDto> CreateCustomerAsync(CustomerDto customer);

    Task<CustomerDto> UpdateCustomerAsync(CustomerDto customer);

    Task DeleteCustomerAsync(int id);

    Task<IEnumerable<VehicleDto>> GetVehiclesByCustomerAsync(int customerId);

    Task<VehicleDto?> GetVehicleByIdAsync(int id);

    Task<VehicleDto?> GetVehicleByLicensePlateAsync(string licensePlateNumber);

    Task<IEnumerable<ServiceHistoryDto>> GetServiceHistoryByVehicleAsync(int vehicleId);

    Task<VehicleDto> AddVehicleAsync(VehicleDto vehicle);

    Task<VehicleDto> UpdateVehicleAsync(VehicleDto vehicle);

    Task DeleteVehicleAsync(int id);
}
