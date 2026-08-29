using Garij.Domain.Entities;

namespace Garij.Infrastructure.Repositories;

public interface ICustomerRepository : IRepository<Customer>
{
    Task<Customer?> GetByIdWithVehiclesAsync(int id);
}
