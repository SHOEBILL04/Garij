using Garij.Application.Configuration;
using Garij.Application.DTOs;
using Garij.Application.Services;
using Garij.Domain.Entities;
using Garij.Domain.Enums;
using Garij.Infrastructure.Persistence;
using Garij.Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Garij.UnitTests;

public class ProjectPurchaseServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly GarijDbContext _context;
    private readonly ProjectPurchaseRepository _repository;
    private readonly ProjectPurchaseService _service;

    public ProjectPurchaseServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<GarijDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new GarijDbContext(options);
        _context.Database.EnsureCreated();

        _repository = new ProjectPurchaseRepository(_context);
        var settings = Options.Create(new LicenseSettings
        {
            Price = 499.00m,
            Currency = "USD",
            ProductName = "Garij Intelligent Vehicle Workshop — Lifetime License"
        });

        _service = new ProjectPurchaseService(_repository, settings);
    }

    [Fact]
    public async Task ProcessPurchase_WithValidCard_CreatesActiveLicenseAndReturnsValidKey()
    {
        var dto = new CreateProjectPurchaseDto
        {
            BuyerName = "Alex Mercer",
            BuyerEmail = "alex.mercer@workshop.com",
            WorkshopName = "Apex Motors",
            PaymentMethod = "CreditCard",
            CardholderName = "Alex Mercer",
            CardNumber = "4242 4242 4242 4242",
            ExpiryDate = "12/28",
            Cvv = "123"
        };

        var result = await _service.ProcessPurchaseAsync(dto, "user-id-123");

        Assert.True(result.Success);
        Assert.NotNull(result.Purchase);
        Assert.NotNull(result.LicenseKey);
        Assert.StartsWith("GRJ-LIC-", result.LicenseKey);
        Assert.Equal("alex.mercer@workshop.com", result.Purchase.BuyerEmail);
        Assert.Equal(499.00m, result.Purchase.Amount);
        Assert.Equal(LicenseStatus.Active, result.Purchase.Status);
        Assert.True(result.Purchase.IsActive);
    }

    [Fact]
    public async Task ProcessPurchase_WithTestMode_ProcessesSuccessfullyWithoutCardValidation()
    {
        var dto = new CreateProjectPurchaseDto
        {
            BuyerName = "Demo Tester",
            BuyerEmail = "tester@garij.com",
            PaymentMethod = "CreditCard",
            IsTestPayment = true
        };

        var result = await _service.ProcessPurchaseAsync(dto, "user-id-test");

        Assert.True(result.Success);
        Assert.NotNull(result.Purchase);
        Assert.Equal("TestCheckout", result.Purchase.PaymentMethod);
        Assert.True(result.Purchase.IsActive);
    }

    [Fact]
    public async Task ProcessPurchase_WithInvalidCard_FailsValidation()
    {
        var dto = new CreateProjectPurchaseDto
        {
            BuyerName = "Invalid Card User",
            BuyerEmail = "invalid@user.com",
            PaymentMethod = "CreditCard",
            CardNumber = "123", // Too short
            ExpiryDate = "12/28",
            Cvv = "123",
            IsTestPayment = false
        };

        var result = await _service.ProcessPurchaseAsync(dto, null);

        Assert.False(result.Success);
        Assert.Contains("valid card number", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HasActiveLicense_ReturnsTrueForLicensedUser_AndFalseForUnlicensed()
    {
        var purchase = new ProjectPurchase
        {
            LicenseKey = "GRJ-LIC-TEST-1111-2222-3333",
            IdentityUserId = "licensed-user-id",
            BuyerName = "Licensed User",
            BuyerEmail = "licensed@user.com",
            Amount = 499.00m,
            Currency = "USD",
            PaymentMethod = "CreditCard",
            TransactionReference = "TXN-TEST-1",
            PurchasedAt = DateTime.UtcNow,
            Status = LicenseStatus.Active,
            IsActive = true
        };
        await _repository.AddAsync(purchase);
        await _repository.SaveChangesAsync();

        var isLicensed = await _service.HasActiveLicenseAsync("licensed-user-id", "licensed@user.com");
        var isUnlicensed = await _service.HasActiveLicenseAsync("unlicensed-user-id", "unlicensed@user.com");

        Assert.True(isLicensed);
        Assert.False(isUnlicensed);
    }

    [Fact]
    public async Task ActivateLicenseKey_WithValidKey_ClaimsAndActivatesKey()
    {
        var purchase = new ProjectPurchase
        {
            LicenseKey = "GRJ-LIC-UNCLAIMED-KEY-9999",
            IdentityUserId = null,
            BuyerName = "Direct Invoice Purchaser",
            BuyerEmail = "",
            Amount = 499.00m,
            Currency = "USD",
            PaymentMethod = "BankWire",
            TransactionReference = "TXN-WIRE-9999",
            PurchasedAt = DateTime.UtcNow,
            Status = LicenseStatus.Active,
            IsActive = true
        };
        await _repository.AddAsync(purchase);
        await _repository.SaveChangesAsync();

        var result = await _service.ActivateLicenseKeyAsync("GRJ-LIC-UNCLAIMED-KEY-9999", "new-user-id", "new@user.com");

        Assert.True(result.Success);
        var activated = await _service.GetActiveLicenseAsync("new-user-id", "new@user.com");
        Assert.NotNull(activated);
        Assert.Equal("new-user-id", activated.IdentityUserId);
    }

    [Fact]
    public async Task ActivateLicenseKey_WithInvalidKey_ReturnsError()
    {
        var result = await _service.ActivateLicenseKeyAsync("NON-EXISTENT-KEY", "user-id", "user@test.com");

        Assert.False(result.Success);
        Assert.Contains("Invalid license key", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateWorkshopName_And_GetWorkshopName_UpdatesSuccessfully()
    {
        var purchase = new ProjectPurchase
        {
            LicenseKey = "GRJ-LIC-GARAGE-NAME-TEST",
            IdentityUserId = "owner-id",
            BuyerName = "Garage Owner",
            BuyerEmail = "owner@test.com",
            WorkshopName = "Initial Garage Name",
            Amount = 499.00m,
            Currency = "USD",
            PaymentMethod = "CreditCard",
            TransactionReference = "TXN-NAME-1111",
            PurchasedAt = DateTime.UtcNow,
            Status = LicenseStatus.Active,
            IsActive = true
        };
        await _repository.AddAsync(purchase);
        await _repository.SaveChangesAsync();

        var initialName = await _service.GetWorkshopNameAsync("owner-id", "owner@test.com");
        Assert.Equal("Initial Garage Name", initialName);

        var updateResult = await _service.UpdateWorkshopNameAsync("owner-id", "owner@test.com", "Apex Performance Tuning");
        Assert.True(updateResult);

        var updatedName = await _service.GetWorkshopNameAsync("owner-id", "owner@test.com");
        Assert.Equal("Apex Performance Tuning", updatedName);
    }

    public void Dispose()
    {
        _connection.Dispose();
        _context.Dispose();
    }
}
