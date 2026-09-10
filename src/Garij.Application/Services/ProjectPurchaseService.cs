using System.Security.Cryptography;
using Garij.Application.Configuration;
using Garij.Application.DTOs;
using Garij.Application.Interfaces;
using Garij.Domain.Entities;
using Garij.Domain.Enums;
using Garij.Infrastructure.Repositories;
using Microsoft.Extensions.Options;

namespace Garij.Application.Services;

public class ProjectPurchaseService : IProjectPurchaseService
{
    private readonly IProjectPurchaseRepository _purchaseRepository;
    private readonly LicenseSettings _settings;

    public ProjectPurchaseService(
        IProjectPurchaseRepository purchaseRepository,
        IOptions<LicenseSettings> settings)
    {
        _purchaseRepository = purchaseRepository;
        _settings = settings.Value;
    }

    public async Task<PurchaseResultDto> ProcessPurchaseAsync(CreateProjectPurchaseDto dto, string? currentUserId)
    {
        if (string.IsNullOrWhiteSpace(dto.BuyerEmail))
        {
            return PurchaseResultDto.Failed("Buyer email is required.");
        }

        if (string.IsNullOrWhiteSpace(dto.BuyerName))
        {
            return PurchaseResultDto.Failed("Buyer name is required.");
        }

        // Validate payment information if not in test/demo mode
        if (!dto.IsTestPayment)
        {
            if (dto.PaymentMethod == "CreditCard")
            {
                var cleanCard = (dto.CardNumber ?? string.Empty).Replace(" ", "").Replace("-", "");
                if (cleanCard.Length < 12 || !cleanCard.All(char.IsDigit))
                {
                    return PurchaseResultDto.Failed("Please provide a valid card number (12-19 digits).");
                }

                if (string.IsNullOrWhiteSpace(dto.ExpiryDate) || !dto.ExpiryDate.Contains('/'))
                {
                    return PurchaseResultDto.Failed("Please provide a valid card expiration date in MM/YY format.");
                }

                if (string.IsNullOrWhiteSpace(dto.Cvv) || dto.Cvv.Length < 3 || !dto.Cvv.All(char.IsDigit))
                {
                    return PurchaseResultDto.Failed("Please provide a valid 3 or 4 digit CVV/CVC code.");
                }
            }
        }

        // Check if user or email already has an active lifetime license
        var existing = await _purchaseRepository.GetActiveLicenseForUserAsync(currentUserId, dto.BuyerEmail);
        if (existing is not null && existing.IsActive && existing.Status == LicenseStatus.Active)
        {
            return PurchaseResultDto.Successful(
                MapToDto(existing),
                "An active lifetime license already exists for this account/email.");
        }

        // Generate cryptographically unique License Key
        var licenseKey = await GenerateUniqueLicenseKeyAsync();
        var transactionRef = GenerateTransactionReference();

        var purchase = new ProjectPurchase
        {
            LicenseKey = licenseKey,
            IdentityUserId = currentUserId,
            BuyerName = dto.BuyerName.Trim(),
            BuyerEmail = dto.BuyerEmail.Trim().ToLowerInvariant(),
            WorkshopName = string.IsNullOrWhiteSpace(dto.WorkshopName) ? null : dto.WorkshopName.Trim(),
            Amount = _settings.Price,
            Currency = _settings.Currency,
            PaymentMethod = dto.IsTestPayment ? "TestCheckout" : dto.PaymentMethod,
            TransactionReference = transactionRef,
            PurchasedAt = DateTime.UtcNow,
            Status = LicenseStatus.Active,
            IsActive = true,
            Notes = dto.IsTestPayment ? "Instant test checkout completed." : "Direct customer checkout purchase."
        };

        await _purchaseRepository.AddAsync(purchase);
        await _purchaseRepository.SaveChangesAsync();

        return PurchaseResultDto.Successful(MapToDto(purchase));
    }

    public async Task<bool> HasActiveLicenseAsync(string? userId, string? email)
    {
        if (string.IsNullOrWhiteSpace(userId) && string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        var license = await _purchaseRepository.GetActiveLicenseForUserAsync(userId, email);
        return license is not null && license.IsActive && license.Status == LicenseStatus.Active;
    }

    public async Task<ProjectPurchaseDto?> GetActiveLicenseAsync(string? userId, string? email)
    {
        if (string.IsNullOrWhiteSpace(userId) && string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var license = await _purchaseRepository.GetActiveLicenseForUserAsync(userId, email);
        return license is null ? null : MapToDto(license);
    }

    public async Task<ProjectPurchaseDto?> GetPurchaseByIdAsync(int id)
    {
        var purchase = await _purchaseRepository.GetByIdAsync(id);
        return purchase is null ? null : MapToDto(purchase);
    }

    public async Task<PurchaseResultDto> ActivateLicenseKeyAsync(string licenseKey, string? userId, string? email)
    {
        if (string.IsNullOrWhiteSpace(licenseKey))
        {
            return PurchaseResultDto.Failed("License key cannot be empty.");
        }

        var normalizedKey = licenseKey.Trim().ToUpperInvariant();
        var purchase = await _purchaseRepository.GetByLicenseKeyAsync(normalizedKey);

        if (purchase is null)
        {
            return PurchaseResultDto.Failed("Invalid license key. Please verify and try again.");
        }

        if (!purchase.IsActive || purchase.Status != LicenseStatus.Active)
        {
            return PurchaseResultDto.Failed("This license key is deactivated or revoked.");
        }

        // Link with current user / email if not already bound or if demo key
        if (!string.IsNullOrWhiteSpace(userId))
        {
            if (string.IsNullOrWhiteSpace(purchase.IdentityUserId) || purchase.LicenseKey.Contains("DEMO"))
            {
                purchase.IdentityUserId = userId;
            }
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            if (string.IsNullOrWhiteSpace(purchase.BuyerEmail) || purchase.LicenseKey.Contains("DEMO"))
            {
                purchase.BuyerEmail = email.Trim().ToLowerInvariant();
            }
        }

        _purchaseRepository.Update(purchase);
        await _purchaseRepository.SaveChangesAsync();

        return PurchaseResultDto.Successful(MapToDto(purchase), "License key activated successfully!");
    }

    public async Task<IEnumerable<ProjectPurchaseDto>> GetAllPurchasesAsync()
    {
        var all = await _purchaseRepository.GetAllAsync();
        return all.OrderByDescending(p => p.PurchasedAt).Select(MapToDto);
    }

    public async Task<string> GetWorkshopNameAsync(string? userId = null, string? email = null)
    {
        // 1. Try to find the active license associated with this specific user or email
        if (!string.IsNullOrWhiteSpace(userId) || !string.IsNullOrWhiteSpace(email))
        {
            var userLicense = await _purchaseRepository.GetActiveLicenseForUserAsync(userId, email);
            if (userLicense is not null && 
                !string.IsNullOrWhiteSpace(userLicense.WorkshopName) && 
                userLicense.WorkshopName != "Workshop Staff Member")
            {
                return userLicense.WorkshopName;
            }
        }

        // 2. Look for the primary workshop purchase record with a custom workshop name
        var allPurchases = await _purchaseRepository.GetAllAsync();
        var primaryPurchase = allPurchases
            .Where(p => p.IsActive && p.Status == LicenseStatus.Active && 
                        !string.IsNullOrWhiteSpace(p.WorkshopName) && 
                        p.WorkshopName != "Workshop Staff Member")
            .OrderByDescending(p => p.Amount)
            .ThenByDescending(p => p.PurchasedAt)
            .FirstOrDefault();

        if (primaryPurchase is not null && !string.IsNullOrWhiteSpace(primaryPurchase.WorkshopName))
        {
            return primaryPurchase.WorkshopName;
        }

        return "Garij Master Workshop";
    }

    public async Task<bool> UpdateWorkshopNameAsync(string? userId, string? email, string newWorkshopName)
    {
        if (string.IsNullOrWhiteSpace(newWorkshopName))
        {
            return false;
        }

        var cleanName = newWorkshopName.Trim();

        // 1. Find user's active license if possible
        ProjectPurchase? licenseToUpdate = null;
        if (!string.IsNullOrWhiteSpace(userId) || !string.IsNullOrWhiteSpace(email))
        {
            licenseToUpdate = await _purchaseRepository.GetActiveLicenseForUserAsync(userId, email);
        }

        // 2. If not found, find the main workshop license
        var allPurchases = (await _purchaseRepository.GetAllAsync()).ToList();
        if (licenseToUpdate is null)
        {
            licenseToUpdate = allPurchases
                .Where(p => p.IsActive && p.Status == LicenseStatus.Active)
                .OrderByDescending(p => p.Amount)
                .FirstOrDefault();
        }

        if (licenseToUpdate is not null)
        {
            licenseToUpdate.WorkshopName = cleanName;
            _purchaseRepository.Update(licenseToUpdate);

            // Also synchronize name across any staff licenses created under this workshop or seeded
            foreach (var staffLicense in allPurchases.Where(p => p.PaymentMethod.Contains("Staff") || p.WorkshopName == "Workshop Staff Member" || p.PaymentMethod == "SystemSeeded"))
            {
                staffLicense.WorkshopName = cleanName;
                _purchaseRepository.Update(staffLicense);
            }

            await _purchaseRepository.SaveChangesAsync();
            return true;
        }

        return false;
    }

    private async Task<string> GenerateUniqueLicenseKeyAsync()
    {
        while (true)
        {
            var bytes = new byte[8];
            RandomNumberGenerator.Fill(bytes);
            var hex = Convert.ToHexString(bytes); // 16 characters
            var key = $"GRJ-LIC-{hex[0..4]}-{hex[4..8]}-{hex[8..12]}-{hex[12..16]}";

            var existing = await _purchaseRepository.GetByLicenseKeyAsync(key);
            if (existing is null)
            {
                return key;
            }
        }
    }

    private static string GenerateTransactionReference()
    {
        var bytes = new byte[3];
        RandomNumberGenerator.Fill(bytes);
        var hex = Convert.ToHexString(bytes);
        return $"TXN-{DateTime.UtcNow:yyyyMMddHHmmss}-{hex}";
    }

    private static ProjectPurchaseDto MapToDto(ProjectPurchase entity) =>
        new()
        {
            Id = entity.Id,
            LicenseKey = entity.LicenseKey,
            IdentityUserId = entity.IdentityUserId,
            BuyerName = entity.BuyerName,
            BuyerEmail = entity.BuyerEmail,
            WorkshopName = entity.WorkshopName,
            Amount = entity.Amount,
            Currency = entity.Currency,
            PaymentMethod = entity.PaymentMethod,
            TransactionReference = entity.TransactionReference,
            PurchasedAt = entity.PurchasedAt,
            Status = entity.Status,
            IsActive = entity.IsActive,
            Notes = entity.Notes
        };
}
