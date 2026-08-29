using Garij.Domain.Entities;
using Garij.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Garij.Infrastructure.Repositories;

public class MechanicAssignmentRepository : Repository<MechanicAssignment>, IMechanicAssignmentRepository
{
    public MechanicAssignmentRepository(GarijDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<MechanicAssignment>> GetAssignmentsByJobIdAsync(int serviceJobId) =>
        await DbSet.Include(ma => ma.User)
            .Where(ma => ma.ServiceJobId == serviceJobId)
            .ToListAsync();
}
