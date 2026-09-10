using Garij.Application.DTOs;

namespace Garij.Application.Interfaces;

public interface IProjectPurchaseService
{
    Task<PurchaseResultDto> ProcessPurchaseAsync(CreateProjectPurchaseDto dto, string? currentUserId);

    Task<bool> HasActiveLicenseAsync(string? userId, string? email);

    Task<ProjectPurchaseDto?> GetActiveLicenseAsync(string? userId, string? email);

    Task<ProjectPurchaseDto?> GetPurchaseByIdAsync(int id);

    Task<PurchaseResultDto> ActivateLicenseKeyAsync(string licenseKey, string? userId, string? email);

    Task<IEnumerable<ProjectPurchaseDto>> GetAllPurchasesAsync();

    Task<string> GetWorkshopNameAsync(string? userId = null, string? email = null);

    Task<bool> UpdateWorkshopNameAsync(string? userId, string? email, string newWorkshopName);
}
