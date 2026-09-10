using Garij.Domain.Entities;

namespace Garij.Infrastructure.Repositories;

public interface IProjectPurchaseRepository : IRepository<ProjectPurchase>
{
    Task<ProjectPurchase?> GetByLicenseKeyAsync(string licenseKey);

    Task<ProjectPurchase?> GetActiveLicenseForUserAsync(string? identityUserId, string? email);

    Task<IEnumerable<ProjectPurchase>> GetPurchasesByEmailAsync(string email);
}
