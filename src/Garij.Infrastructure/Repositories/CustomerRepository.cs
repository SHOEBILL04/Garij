using Garij.Domain.Entities;
using Garij.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Garij.Infrastructure.Repositories;

public class CustomerRepository : Repository<Customer>, ICustomerRepository
{
    public CustomerRepository(GarijDbContext context) : base(context)
    {
    }

    public async Task<Customer?> GetByIdWithVehiclesAsync(int id) =>
        await DbSet.Include(c => c.Vehicles)
            .FirstOrDefaultAsync(c => c.Id == id);
}
