using Garij.Domain.Entities;
using Garij.Domain.Enums;
using Garij.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Garij.Infrastructure.Repositories;

public class ProjectPurchaseRepository : Repository<ProjectPurchase>, IProjectPurchaseRepository
{
    public ProjectPurchaseRepository(GarijDbContext context) : base(context)
    {
    }

    public async Task<ProjectPurchase?> GetByLicenseKeyAsync(string licenseKey)
    {
        var normalizedKey = licenseKey.Trim().ToUpperInvariant();
        return await DbSet.FirstOrDefaultAsync(p => p.LicenseKey.ToUpper() == normalizedKey);
    }

    public async Task<ProjectPurchase?> GetActiveLicenseForUserAsync(string? identityUserId, string? email)
    {
        var query = DbSet.Where(p => p.IsActive && p.Status == LicenseStatus.Active);

        if (!string.IsNullOrWhiteSpace(identityUserId) && !string.IsNullOrWhiteSpace(email))
        {
            var normalizedEmail = email.Trim().ToLowerInvariant();
            return await query.FirstOrDefaultAsync(p => p.IdentityUserId == identityUserId || p.BuyerEmail.ToLower() == normalizedEmail);
        }

        if (!string.IsNullOrWhiteSpace(identityUserId))
        {
            return await query.FirstOrDefaultAsync(p => p.IdentityUserId == identityUserId);
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            var normalizedEmail = email.Trim().ToLowerInvariant();
            return await query.FirstOrDefaultAsync(p => p.BuyerEmail.ToLower() == normalizedEmail);
        }

        return null;
    }

    public async Task<IEnumerable<ProjectPurchase>> GetPurchasesByEmailAsync(string email)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        return await DbSet.Where(p => p.BuyerEmail.ToLower() == normalizedEmail)
                          .OrderByDescending(p => p.PurchasedAt)
                          .ToListAsync();
    }
}
