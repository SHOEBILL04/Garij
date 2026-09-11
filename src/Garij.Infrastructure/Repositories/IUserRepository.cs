using Garij.Domain.Entities;

namespace Garij.Infrastructure.Repositories;

public interface IUserRepository : IRepository<User>
{
    Task<User?> GetByIdentityUserIdAsync(string identityUserId);

    /// <summary>
    /// Resolves the effective garage identifier for a user, falling back to 'default-garij-master' if unassigned.
    /// </summary>
    Task<string> GetUserGarageIdAsync(string? identityUserId, string? email);

    /// <summary>
    /// Gets all staff users belonging to a specific garage.
    /// </summary>
    Task<IEnumerable<User>> GetByGarageIdAsync(string garageId);

    /// <summary>
    /// Gets all mechanics belonging to a specific garage.
    /// </summary>
    Task<IEnumerable<User>> GetMechanicsByGarageIdAsync(string garageId);
}
