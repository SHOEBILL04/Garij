using Garij.Domain.Entities;

namespace Garij.Infrastructure.Repositories;

public interface IMechanicAssignmentRepository : IRepository<MechanicAssignment>
{
    Task<IEnumerable<MechanicAssignment>> GetAssignmentsByJobIdAsync(int serviceJobId);
}
