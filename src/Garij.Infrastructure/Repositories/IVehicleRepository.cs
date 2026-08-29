using Garij.Domain.Entities;

namespace Garij.Infrastructure.Repositories;

public interface IVehicleRepository : IRepository<Vehicle>
{
    Task<IEnumerable<Vehicle>> GetAllWithCustomersAsync();

    Task<IEnumerable<Vehicle>> GetByCustomerAsync(int customerId);

    Task<Vehicle?> GetByIdWithCustomerAsync(int id);

    Task<Vehicle?> GetByLicensePlateAsync(string licensePlateNumber);
}
