using Garij.Domain.Entities;
using Garij.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Garij.Infrastructure.Repositories;

public class UserRepository : Repository<User>, IUserRepository
{
    public UserRepository(GarijDbContext context) : base(context)
    {
    }

    public async Task<User?> GetByIdentityUserIdAsync(string identityUserId) =>
        await DbSet.FirstOrDefaultAsync(u => u.IdentityUserId == identityUserId);

    public async Task<string> GetUserGarageIdAsync(string? identityUserId, string? email)
    {
        if (string.IsNullOrWhiteSpace(identityUserId) && string.IsNullOrWhiteSpace(email))
        {
            return "default-garij-master";
        }

        var normalizedEmail = email?.Trim().ToLowerInvariant();
        var user = await DbSet.FirstOrDefaultAsync(u =>
            (!string.IsNullOrEmpty(identityUserId) && u.IdentityUserId == identityUserId) ||
            (!string.IsNullOrEmpty(normalizedEmail) && u.Email.ToLower() == normalizedEmail));

        return string.IsNullOrWhiteSpace(user?.GarageId) ? "default-garij-master" : user.GarageId;
    }

    public async Task<IEnumerable<User>> GetByGarageIdAsync(string garageId)
    {
        var effectiveGarageId = string.IsNullOrWhiteSpace(garageId) ? "default-garij-master" : garageId;
        return await DbSet
            .Where(u => (u.GarageId ?? "default-garij-master") == effectiveGarageId)
            .OrderBy(u => u.FullName)
            .ToListAsync();
    }

    public async Task<IEnumerable<User>> GetMechanicsByGarageIdAsync(string garageId)
    {
        var effectiveGarageId = string.IsNullOrWhiteSpace(garageId) ? "default-garij-master" : garageId;
        return await DbSet
            .Where(u => u.Role == Domain.Enums.UserRole.Mechanic && (u.GarageId ?? "default-garij-master") == effectiveGarageId)
            .OrderBy(u => u.FullName)
            .ToListAsync();
    }
}
