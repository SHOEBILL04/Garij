using Garij.Application.DTOs;
using Garij.Application.Interfaces;
using Garij.Application.Services;
using Garij.Infrastructure.Persistence;
using Garij.Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using ValidationException = Garij.Domain.Exceptions.ValidationException;

namespace Garij.UnitTests;

/// <summary>
/// Covers defect D-10 (Report 05): a customer's e-mail and phone number were never checked for
/// uniqueness, so one person could be registered several times in a garage and have their vehicles
/// and service history split across the duplicate rows.
///
/// Runs the real CustomerVehicleService over SQLite. Each service instance is bound to one garage,
/// the way the request-scoped ICurrentGarageService binds it in the running app, so the cross-garage
/// cases use exactly the lookup path production uses.
///
/// Covers TC-CUS-09.
/// </summary>
public class CustomerUniquenessTests : IDisposable
{
    private const string GarageA = "default-garij-master";
    private const string GarageB = "second-garage";

    private readonly SqliteConnection _connection;

    public CustomerUniquenessTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using var context = NewContext();
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private GarijDbContext NewContext() =>
        new(new DbContextOptionsBuilder<GarijDbContext>().UseSqlite(_connection).Options);

    private static CustomerVehicleService ServiceFor(GarijDbContext context, string garageId) =>
        new(
            new CustomerRepository(context),
            new VehicleRepository(context),
            new ServiceJobRepository(context),
            new FixedGarage(garageId));

    private static CustomerDto NewCustomer(string email, string phone, string name = "Test Customer") => new()
    {
        FullName = name,
        Email = email,
        PhoneNumber = phone,
        Address = "Dhaka"
    };

    private async Task<CustomerDto> CreateAsync(string garageId, CustomerDto customer)
    {
        using var context = NewContext();
        return await ServiceFor(context, garageId).CreateCustomerAsync(customer);
    }

    private async Task<CustomerDto> UpdateAsync(string garageId, CustomerDto customer)
    {
        using var context = NewContext();
        return await ServiceFor(context, garageId).UpdateCustomerAsync(customer);
    }

    private int CustomersWithEmail(string email)
    {
        using var context = NewContext();
        return context.Customers.AsNoTracking().AsEnumerable()
            .Count(c => string.Equals(c.Email, email, StringComparison.OrdinalIgnoreCase));
    }

    // =====================================================================================
    // (a) A customer with a unique e-mail and phone registers.
    // =====================================================================================

    [Fact]
    public async Task CreateCustomerAsync_Succeeds_WhenTheEmailAndPhoneAreNotOnFile()
    {
        await CreateAsync(GarageA, NewCustomer("nadia@example.com", "+8801711000001"));

        var created = await CreateAsync(GarageA, NewCustomer("rafi@example.com", "+8801711000002", "Rafi Karim"));

        Assert.True(created.Id > 0);
        Assert.Equal(GarageA, created.GarageId);
        Assert.Equal(1, CustomersWithEmail("rafi@example.com"));
    }

    // =====================================================================================
    // (b) TC-CUS-09: a duplicate within the same garage is rejected, naming the field.
    // =====================================================================================

    [Fact]
    public async Task CreateCustomerAsync_RejectsAnEmailAlreadyOnFileInTheGarage()
    {
        await CreateAsync(GarageA, NewCustomer("nadia@example.com", "+8801711000001"));

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            CreateAsync(GarageA, NewCustomer("nadia@example.com", "+8801711000099", "Second Nadia")));

        Assert.Equal(["A customer with this e-mail address already exists."], ex.Errors[nameof(CustomerDto.Email)]);
        Assert.False(ex.Errors.ContainsKey(nameof(CustomerDto.PhoneNumber)));
        Assert.Equal(1, CustomersWithEmail("nadia@example.com"));
    }

    [Fact]
    public async Task CreateCustomerAsync_RejectsAPhoneNumberAlreadyOnFileInTheGarage()
    {
        await CreateAsync(GarageA, NewCustomer("nadia@example.com", "+8801711000001"));

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            CreateAsync(GarageA, NewCustomer("different@example.com", "+8801711000001")));

        Assert.Equal(["A customer with this phone number already exists."], ex.Errors[nameof(CustomerDto.PhoneNumber)]);
        Assert.False(ex.Errors.ContainsKey(nameof(CustomerDto.Email)));
        Assert.Equal(0, CustomersWithEmail("different@example.com"));
    }

    [Fact]
    public async Task CreateCustomerAsync_ReportsBothFields_WhenBothAreAlreadyOnFile()
    {
        await CreateAsync(GarageA, NewCustomer("nadia@example.com", "+8801711000001"));

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            CreateAsync(GarageA, NewCustomer("nadia@example.com", "+8801711000001")));

        Assert.True(ex.Errors.ContainsKey(nameof(CustomerDto.Email)), "The duplicate e-mail was not reported.");
        Assert.True(ex.Errors.ContainsKey(nameof(CustomerDto.PhoneNumber)), "The duplicate phone number was not reported.");
    }

    [Theory]
    [InlineData("NADIA@Example.COM", "+8801711000077")]      // e-mail differing only in letter case
    [InlineData("  nadia@example.com  ", "+8801711000077")]  // e-mail with surrounding spaces
    [InlineData("other@example.com", "+880 1711-000001")]    // same phone number, formatted differently
    public async Task CreateCustomerAsync_RejectsTheSameContactDetailsWrittenDifferently(string email, string phone)
    {
        await CreateAsync(GarageA, NewCustomer("nadia@example.com", "+8801711000001"));

        await Assert.ThrowsAsync<ValidationException>(() => CreateAsync(GarageA, NewCustomer(email, phone)));
    }

    // =====================================================================================
    // (c) The same e-mail and phone are allowed in a different garage.
    // =====================================================================================

    [Fact]
    public async Task CreateCustomerAsync_AllowsTheSameEmailAndPhoneInAnotherGarage()
    {
        await CreateAsync(GarageA, NewCustomer("nadia@example.com", "+8801711000001"));

        var inGarageB = await CreateAsync(GarageB, NewCustomer("nadia@example.com", "+8801711000001"));

        Assert.Equal(GarageB, inGarageB.GarageId);
        Assert.Equal(2, CustomersWithEmail("nadia@example.com"));
    }

    [Fact]
    public async Task UpdateCustomerAsync_AllowsMatchingACustomerWhoBelongsToAnotherGarage()
    {
        await CreateAsync(GarageB, NewCustomer("shared@example.com", "+8801711000050"));
        var mine = await CreateAsync(GarageA, NewCustomer("mine@example.com", "+8801711000051"));

        mine.Email = "shared@example.com";
        mine.PhoneNumber = "+8801711000050";
        var updated = await UpdateAsync(GarageA, mine);

        Assert.Equal("shared@example.com", updated.Email);
    }

    // =====================================================================================
    // (d) Editing a customer without changing the e-mail or phone still saves.
    // =====================================================================================

    [Fact]
    public async Task UpdateCustomerAsync_Succeeds_WhenTheEmailAndPhoneAreUnchanged()
    {
        var customer = await CreateAsync(GarageA, NewCustomer("nadia@example.com", "+8801711000001", "Nadia Islam"));
        await CreateAsync(GarageA, NewCustomer("someone.else@example.com", "+8801711000002"));

        customer.FullName = "Nadia Islam Chowdhury";
        customer.Address = "Gulshan, Dhaka";
        var updated = await UpdateAsync(GarageA, customer);

        // Not blocked by matching its own e-mail and phone.
        Assert.Equal("Nadia Islam Chowdhury", updated.FullName);
        Assert.Equal("nadia@example.com", updated.Email);

        using var context = NewContext();
        Assert.Equal("Gulshan, Dhaka", context.Customers.AsNoTracking().Single(c => c.Id == customer.Id).Address);
    }

    [Fact]
    public async Task UpdateCustomerAsync_Succeeds_WhenOnlyTheLetterCaseOfItsOwnEmailChanges()
    {
        var customer = await CreateAsync(GarageA, NewCustomer("nadia@example.com", "+8801711000001"));

        customer.Email = "Nadia@Example.com";
        var updated = await UpdateAsync(GarageA, customer);

        Assert.Equal("Nadia@Example.com", updated.Email);
    }

    // =====================================================================================
    // (e) Editing a customer onto another customer's e-mail or phone in the garage is rejected.
    // =====================================================================================

    [Fact]
    public async Task UpdateCustomerAsync_RejectsTakingAnotherCustomersEmail()
    {
        await CreateAsync(GarageA, NewCustomer("nadia@example.com", "+8801711000001"));
        var other = await CreateAsync(GarageA, NewCustomer("rafi@example.com", "+8801711000002"));

        other.Email = "nadia@example.com";
        var ex = await Assert.ThrowsAsync<ValidationException>(() => UpdateAsync(GarageA, other));

        Assert.Equal(["A customer with this e-mail address already exists."], ex.Errors[nameof(CustomerDto.Email)]);

        using var context = NewContext();
        Assert.Equal("rafi@example.com", context.Customers.AsNoTracking().Single(c => c.Id == other.Id).Email);
    }

    [Fact]
    public async Task UpdateCustomerAsync_RejectsTakingAnotherCustomersPhoneNumber()
    {
        await CreateAsync(GarageA, NewCustomer("nadia@example.com", "+8801711000001"));
        var other = await CreateAsync(GarageA, NewCustomer("rafi@example.com", "+8801711000002"));

        other.PhoneNumber = "+8801711000001";
        var ex = await Assert.ThrowsAsync<ValidationException>(() => UpdateAsync(GarageA, other));

        Assert.Equal(["A customer with this phone number already exists."], ex.Errors[nameof(CustomerDto.PhoneNumber)]);

        using var context = NewContext();
        Assert.Equal("+8801711000002", context.Customers.AsNoTracking().Single(c => c.Id == other.Id).PhoneNumber);
    }

    private sealed class FixedGarage(string garageId) : ICurrentGarageService
    {
        public Task<string> GetCurrentGarageIdAsync() => Task.FromResult(garageId);
    }
}
