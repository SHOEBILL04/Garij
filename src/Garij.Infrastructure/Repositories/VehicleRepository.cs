using Garij.Domain.Entities;
using Garij.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Garij.Infrastructure.Repositories;

public class VehicleRepository : Repository<Vehicle>, IVehicleRepository
{
    public VehicleRepository(GarijDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<Vehicle>> GetAllWithCustomersAsync() =>
        await DbSet.Include(v => v.Customer)
            .OrderBy(v => v.LicensePlateNumber)
            .ToListAsync();

    public async Task<IEnumerable<Vehicle>> GetByCustomerAsync(int customerId) =>
        await DbSet.Include(v => v.Customer)
            .Where(v => v.CustomerId == customerId)
            .OrderBy(v => v.LicensePlateNumber)
            .ToListAsync();

    public async Task<Vehicle?> GetByIdWithCustomerAsync(int id) =>
        await DbSet.Include(v => v.Customer)
            .FirstOrDefaultAsync(v => v.Id == id);

    public async Task<Vehicle?> GetByLicensePlateAsync(string licensePlateNumber) =>
        await DbSet.Include(v => v.Customer)
            .FirstOrDefaultAsync(v => v.LicensePlateNumber == licensePlateNumber);
}
