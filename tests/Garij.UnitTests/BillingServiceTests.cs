using Garij.Application.Configuration;
using Garij.Application.DTOs;
using Garij.Application.Interfaces;
using Garij.Application.Services;
using Garij.Domain.Entities;
using Garij.Domain.Enums;
using Garij.Domain.Exceptions;
using Garij.Infrastructure.Persistence;
using Garij.Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Garij.UnitTests;

public class BillingServiceTests
{
    private const decimal DefaultTaxRatePercent = 15m;

    [Fact]
    public async Task GenerateInvoiceAsync_ShouldSumServicesAndParts()
    {
        var job = CreateJob(
            1,
            services: new List<JobServiceDetail>
            {
                new() { ServiceCatalogId = 1, ServiceCatalog = new ServiceCatalog { Name = "Oil Change" }, Quantity = 1, PriceAtBooking = 50.00m }
            },
            parts: new List<JobPartUsed>
            {
                new() { PartId = 1, Part = new Part { Name = "Oil Filter" }, QuantityUsed = 2, PriceAtUsage = 12.00m }
            });

        var service = CreateService(serviceJobRepository: new FakeServiceJobRepository(job));

        var invoice = await service.GenerateInvoiceAsync(1);

        Assert.Equal(74.00m, invoice.SubTotal); // 50.00 + (12.00 x 2)
        Assert.Equal(11.10m, invoice.TaxAmount); // 74.00 x 15%
        Assert.Equal(85.10m, invoice.TotalAmount);
        Assert.Equal(PaymentStatus.Pending, invoice.PaymentStatus);
        Assert.StartsWith("INV-", invoice.InvoiceNumber);
    }

    [Fact]
    public async Task GenerateInvoiceAsync_ShouldSumPartsOnly()
    {
        var job = CreateJob(1, parts: new List<JobPartUsed>
        {
            new() { PartId = 1, Part = new Part { Name = "Brake Pad" }, QuantityUsed = 1, PriceAtUsage = 60.00m }
        });

        var service = CreateService(serviceJobRepository: new FakeServiceJobRepository(job));

        var invoice = await service.GenerateInvoiceAsync(1);

        Assert.Equal(60.00m, invoice.SubTotal);
        Assert.Equal(9.00m, invoice.TaxAmount);
        Assert.Equal(69.00m, invoice.TotalAmount);
        Assert.Empty(invoice.ServiceLines);
        Assert.Single(invoice.PartLines);
    }

    [Fact]
    public async Task GenerateInvoiceAsync_ShouldSumServicesOnly()
    {
        var job = CreateJob(1, services: new List<JobServiceDetail>
        {
            new() { ServiceCatalogId = 1, ServiceCatalog = new ServiceCatalog { Name = "Diagnostic" }, Quantity = 1, PriceAtBooking = 80.00m }
        });

        var service = CreateService(serviceJobRepository: new FakeServiceJobRepository(job));

        var invoice = await service.GenerateInvoiceAsync(1);

        Assert.Equal(80.00m, invoice.SubTotal);
        Assert.Equal(12.00m, invoice.TaxAmount);
        Assert.Equal(92.00m, invoice.TotalAmount);
        Assert.Single(invoice.ServiceLines);
        Assert.Empty(invoice.PartLines);
    }

    [Fact]
    public async Task GenerateInvoiceAsync_ShouldRoundTaxToNearestCentAwayFromZero()
    {
        // 11.11 x 3 = 33.33 subtotal (exact at 2 decimals, so subtotal rounding is a no-op here).
        // 33.33 x 15% = 4.9995, which must round up to 5.00, not truncate to 4.99.
        var job = CreateJob(1, services: new List<JobServiceDetail>
        {
            new() { ServiceCatalogId = 1, ServiceCatalog = new ServiceCatalog { Name = "Labour" }, Quantity = 3, PriceAtBooking = 11.11m }
        });

        var service = CreateService(serviceJobRepository: new FakeServiceJobRepository(job));

        var invoice = await service.GenerateInvoiceAsync(1);

        Assert.Equal(33.33m, invoice.SubTotal);
        Assert.Equal(5.00m, invoice.TaxAmount);
        Assert.Equal(38.33m, invoice.TotalAmount);
    }

    [Fact]
    public async Task GenerateInvoiceAsync_ShouldThrowNotFoundException_WhenJobDoesNotExist()
    {
        var service = CreateService(serviceJobRepository: new FakeServiceJobRepository());

        await Assert.ThrowsAsync<NotFoundException>(() => service.GenerateInvoiceAsync(999));
    }

    [Fact]
    public async Task GenerateInvoiceAsync_ShouldThrowBusinessRuleException_WhenJobAlreadyInvoiced()
    {
        var job = CreateJob(1, services: new List<JobServiceDetail>
        {
            new() { ServiceCatalogId = 1, ServiceCatalog = new ServiceCatalog { Name = "Oil Change" }, Quantity = 1, PriceAtBooking = 50.00m }
        });
        var existingInvoice = new Invoice { Id = 1, ServiceJobId = 1, InvoiceNumber = "INV-2026-0001" };

        var service = CreateService(
            invoiceRepository: new FakeInvoiceRepository(existingInvoice),
            serviceJobRepository: new FakeServiceJobRepository(job));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => service.GenerateInvoiceAsync(1));
        Assert.Equal("BR-010", ex.RuleCode);
    }

    [Fact]
    public async Task GenerateInvoiceAsync_ShouldThrowBusinessRuleException_WhenJobHasNoLineItems()
    {
        var job = CreateJob(1);

        var service = CreateService(serviceJobRepository: new FakeServiceJobRepository(job));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => service.GenerateInvoiceAsync(1));
        Assert.Equal("BR-011", ex.RuleCode);
    }

    [Fact]
    public async Task RecordPaymentAsync_ShouldMarkPaid_OnFullPayment()
    {
        var invoice = new Invoice { Id = 1, ServiceJobId = 1, InvoiceNumber = "INV-2026-0001", TotalAmount = 100.00m, PaymentStatus = PaymentStatus.Pending };
        var (invoiceRepo, paymentRepo) = CreateLinkedPaymentRepositories(invoice);
        var service = CreateService(invoiceRepository: invoiceRepo, paymentTransactionRepository: paymentRepo);

        var result = await service.RecordPaymentAsync(new PaymentTransactionDto { InvoiceId = 1, Amount = 100.00m, PaymentMethod = PaymentMethod.Cash });

        Assert.Equal(100.00m, result.Amount);
        Assert.Equal(PaymentStatus.Paid, invoice.PaymentStatus);
    }

    [Fact]
    public async Task RecordPaymentAsync_ShouldStoreEmptyReference_WhenTransactionReferenceIsNull()
    {
        // Regression: an empty "Transaction Reference" field on the RecordPayment form binds to
        // null (not string.Empty) via ASP.NET Core's model binder, and TransactionReference is a
        // NOT NULL column — this must not reach the database as null.
        var invoice = new Invoice { Id = 1, ServiceJobId = 1, InvoiceNumber = "INV-2026-0001", TotalAmount = 100.00m, PaymentStatus = PaymentStatus.Pending };
        var (invoiceRepo, paymentRepo) = CreateLinkedPaymentRepositories(invoice);
        var service = CreateService(invoiceRepository: invoiceRepo, paymentTransactionRepository: paymentRepo);

        var result = await service.RecordPaymentAsync(new PaymentTransactionDto
        {
            InvoiceId = 1,
            Amount = 40.00m,
            PaymentMethod = PaymentMethod.Cash,
            TransactionReference = null!
        });

        Assert.Equal(string.Empty, result.TransactionReference);
    }

    [Fact]
    public async Task RecordPaymentAsync_ShouldMarkPartiallyPaid_OnPartialPayment()
    {
        var invoice = new Invoice { Id = 1, ServiceJobId = 1, InvoiceNumber = "INV-2026-0001", TotalAmount = 100.00m, PaymentStatus = PaymentStatus.Pending };
        var (invoiceRepo, paymentRepo) = CreateLinkedPaymentRepositories(invoice);
        var service = CreateService(invoiceRepository: invoiceRepo, paymentTransactionRepository: paymentRepo);

        await service.RecordPaymentAsync(new PaymentTransactionDto { InvoiceId = 1, Amount = 40.00m, PaymentMethod = PaymentMethod.Cash });

        Assert.Equal(PaymentStatus.PartiallyPaid, invoice.PaymentStatus);
    }

    [Fact]
    public async Task RecordPaymentAsync_ShouldReachPaid_AfterSequenceOfPartialsCoveringTotal()
    {
        var invoice = new Invoice { Id = 1, ServiceJobId = 1, InvoiceNumber = "INV-2026-0001", TotalAmount = 100.00m, PaymentStatus = PaymentStatus.Pending };
        var (invoiceRepo, paymentRepo) = CreateLinkedPaymentRepositories(invoice);
        var service = CreateService(invoiceRepository: invoiceRepo, paymentTransactionRepository: paymentRepo);

        await service.RecordPaymentAsync(new PaymentTransactionDto { InvoiceId = 1, Amount = 40.00m, PaymentMethod = PaymentMethod.Cash });
        Assert.Equal(PaymentStatus.PartiallyPaid, invoice.PaymentStatus);

        await service.RecordPaymentAsync(new PaymentTransactionDto { InvoiceId = 1, Amount = 35.00m, PaymentMethod = PaymentMethod.Card });
        Assert.Equal(PaymentStatus.PartiallyPaid, invoice.PaymentStatus);

        await service.RecordPaymentAsync(new PaymentTransactionDto { InvoiceId = 1, Amount = 25.00m, PaymentMethod = PaymentMethod.DigitalTransfer });
        Assert.Equal(PaymentStatus.Paid, invoice.PaymentStatus);
    }

    [Fact]
    public async Task RecordPaymentAsync_ShouldRejectOverpayment()
    {
        var invoice = new Invoice { Id = 1, ServiceJobId = 1, InvoiceNumber = "INV-2026-0001", TotalAmount = 100.00m, PaymentStatus = PaymentStatus.Pending };
        var (invoiceRepo, paymentRepo) = CreateLinkedPaymentRepositories(invoice);
        var service = CreateService(invoiceRepository: invoiceRepo, paymentTransactionRepository: paymentRepo);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.RecordPaymentAsync(new PaymentTransactionDto { InvoiceId = 1, Amount = 150.00m, PaymentMethod = PaymentMethod.Cash }));

        Assert.Equal("BR-013", ex.RuleCode);
        Assert.Equal(PaymentStatus.Pending, invoice.PaymentStatus);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public async Task RecordPaymentAsync_ShouldRejectZeroOrNegativeAmount(decimal amount)
    {
        var invoice = new Invoice { Id = 1, ServiceJobId = 1, InvoiceNumber = "INV-2026-0001", TotalAmount = 100.00m, PaymentStatus = PaymentStatus.Pending };
        var (invoiceRepo, paymentRepo) = CreateLinkedPaymentRepositories(invoice);
        var service = CreateService(invoiceRepository: invoiceRepo, paymentTransactionRepository: paymentRepo);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.RecordPaymentAsync(new PaymentTransactionDto { InvoiceId = 1, Amount = amount, PaymentMethod = PaymentMethod.Cash }));

        Assert.Equal("BR-012", ex.RuleCode);
    }

    private static (FakeInvoiceRepository InvoiceRepository, FakePaymentTransactionRepository PaymentRepository) CreateLinkedPaymentRepositories(Invoice invoice)
    {
        var paymentRepo = new FakePaymentTransactionRepository();
        var invoiceRepo = new FakeInvoiceRepository(invoice, paymentRepo.Payments);
        return (invoiceRepo, paymentRepo);
    }

    /// <summary>
    /// BillingService takes GarijDbContext directly (see PR description for why), so even
    /// tests that exercise only fake repositories need a real, constructible context to
    /// satisfy the constructor and to let GenerateInvoiceAsync's BeginTransactionAsync/
    /// CommitAsync calls succeed. None of these tests write through this context — the fakes
    /// never touch it — so its schema and contents are irrelevant; it only needs to exist.
    /// </summary>
    private static GarijDbContext CreateInMemoryContext()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<GarijDbContext>().UseSqlite(connection).Options;
        var context = new GarijDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    private static BillingService CreateService(
        IInvoiceRepository? invoiceRepository = null,
        IPaymentTransactionRepository? paymentTransactionRepository = null,
        IServiceJobRepository? serviceJobRepository = null,
        IServiceJobService? serviceJobService = null,
        decimal taxRatePercent = DefaultTaxRatePercent)
    {
        return new BillingService(
            CreateInMemoryContext(),
            invoiceRepository ?? new FakeInvoiceRepository(),
            paymentTransactionRepository ?? new FakePaymentTransactionRepository(),
            serviceJobRepository ?? new FakeServiceJobRepository(),
            serviceJobService ?? new FakeServiceJobService(),
            Options.Create(new BillingSettings { TaxRatePercent = taxRatePercent }));
    }

    private static ServiceJob CreateJob(int id, List<JobServiceDetail>? services = null, List<JobPartUsed>? parts = null)
    {
        services ??= new List<JobServiceDetail>();
        parts ??= new List<JobPartUsed>();

        foreach (var detail in services)
        {
            detail.ServiceJobId = id;
        }

        foreach (var used in parts)
        {
            used.ServiceJobId = id;
        }

        return new ServiceJob
        {
            Id = id,
            CustomerId = 1,
            Customer = new Customer { Id = 1, FullName = "Test Customer" },
            VehicleId = 1,
            Vehicle = new Vehicle { Id = 1, Make = "Toyota", Model = "Corolla", Year = 2020 },
            BookingReference = $"GRJ-2026-{id:D4}",
            JobType = JobType.RoutineService,
            Status = JobStatus.InProgress,
            CreatedAt = DateTime.UtcNow,
            JobServiceDetails = services,
            JobPartsUsed = parts
        };
    }

    private sealed class FakeInvoiceRepository : IInvoiceRepository
    {
        private readonly Dictionary<int, Invoice> _invoices = new();
        private readonly List<PaymentTransaction> _payments;
        private int _nextId = 1;

        public FakeInvoiceRepository(List<PaymentTransaction>? payments = null)
        {
            _payments = payments ?? new List<PaymentTransaction>();
        }

        public FakeInvoiceRepository(Invoice seed, List<PaymentTransaction>? payments = null) : this(payments)
        {
            _invoices[seed.Id] = seed;
            _nextId = seed.Id + 1;
        }

        public Task<Invoice?> GetByIdAsync(int id) => Task.FromResult(_invoices.GetValueOrDefault(id));

        public Task<IEnumerable<Invoice>> GetAllAsync() => Task.FromResult<IEnumerable<Invoice>>(_invoices.Values.ToList());

        public Task AddAsync(Invoice entity)
        {
            entity.Id = _nextId++;
            _invoices[entity.Id] = entity;
            return Task.CompletedTask;
        }

        public void Update(Invoice entity) => _invoices[entity.Id] = entity;

        public void Remove(Invoice entity) => _invoices.Remove(entity.Id);

        public Task<int> SaveChangesAsync() => Task.FromResult(0);

        public Task<Invoice?> GetByServiceJobIdAsync(int serviceJobId) =>
            Task.FromResult(_invoices.Values.FirstOrDefault(i => i.ServiceJobId == serviceJobId));

        /// <summary>Recomputes PaymentTransactions from the shared list every call, mirroring EF's eager Include.</summary>
        public Task<Invoice?> GetByIdWithPaymentsAsync(int id)
        {
            var invoice = _invoices.GetValueOrDefault(id);
            if (invoice is not null)
            {
                invoice.PaymentTransactions = _payments.Where(p => p.InvoiceId == id).ToList();
            }

            return Task.FromResult(invoice);
        }
    }

    private sealed class FakePaymentTransactionRepository : IPaymentTransactionRepository
    {
        public List<PaymentTransaction> Payments { get; } = new();
        private int _nextId = 1;

        public Task<PaymentTransaction?> GetByIdAsync(int id) => Task.FromResult(Payments.FirstOrDefault(p => p.Id == id));

        public Task<IEnumerable<PaymentTransaction>> GetAllAsync() => Task.FromResult<IEnumerable<PaymentTransaction>>(Payments.ToList());

        public Task AddAsync(PaymentTransaction entity)
        {
            entity.Id = _nextId++;
            Payments.Add(entity);
            return Task.CompletedTask;
        }

        public void Update(PaymentTransaction entity)
        {
        }

        public void Remove(PaymentTransaction entity) => Payments.Remove(entity);

        public Task<int> SaveChangesAsync() => Task.FromResult(0);
    }

    private sealed class FakeServiceJobRepository : IServiceJobRepository
    {
        private readonly Dictionary<int, ServiceJob> _jobs;

        public FakeServiceJobRepository(params ServiceJob[] seed)
        {
            _jobs = seed.ToDictionary(j => j.Id);
        }

        public Task<ServiceJob?> GetByIdAsync(int id) => Task.FromResult(_jobs.GetValueOrDefault(id));

        public Task<IEnumerable<ServiceJob>> GetAllAsync() => throw new NotImplementedException();

        public Task AddAsync(ServiceJob entity) => throw new NotImplementedException();

        public void Update(ServiceJob entity) => throw new NotImplementedException();

        public void Remove(ServiceJob entity) => throw new NotImplementedException();

        public Task<int> SaveChangesAsync() => throw new NotImplementedException();

        public Task<ServiceJob?> GetByBookingReferenceAsync(string bookingReference) => throw new NotImplementedException();

        public Task<IEnumerable<ServiceJob>> GetServiceHistoryByVehicleAsync(int vehicleId) => throw new NotImplementedException();

        public Task<IEnumerable<ServiceJob>> GetAllWithDetailsAsync() => throw new NotImplementedException();

        public Task<ServiceJob?> GetByIdWithDetailsAsync(int id) => Task.FromResult(_jobs.GetValueOrDefault(id));

        public Task<IEnumerable<ServiceJob>> GetJobsByStatusAsync(JobStatus status) => throw new NotImplementedException();

        public Task<IEnumerable<ServiceJob>> GetJobsByMechanicAsync(int mechanicUserId) => throw new NotImplementedException();
    }

    private sealed class FakeServiceJobService : IServiceJobService
    {
        public Task<IEnumerable<ServiceJobDto>> GetAllServiceJobsAsync() => throw new NotImplementedException();

        public Task<IEnumerable<ServiceJobDto>> GetServiceJobsByStatusAsync(JobStatus status) => throw new NotImplementedException();

        public Task<IEnumerable<ServiceJobDto>> GetFilteredServiceJobsAsync(JobStatus? status = null, int? mechanicId = null, string? sortBy = null, string? searchTerm = null) => throw new NotImplementedException();

        public Task<ServiceJobDto?> GetServiceJobByIdAsync(int id) => throw new NotImplementedException();

        public Task<ServiceJobDto?> GetServiceJobByBookingReferenceAsync(string bookingReference) => throw new NotImplementedException();

        public Task<ServiceJobDto> CreateServiceJobAsync(ServiceJobDto serviceJob) => throw new NotImplementedException();

        public Task<ServiceJobDto> UpdateServiceJobAsync(ServiceJobDto serviceJob) => throw new NotImplementedException();

        public Task<ServiceJobDto> UpdateServiceJobStatusAsync(int id, JobStatus status) =>
            Task.FromResult(new ServiceJobDto { Id = id, Status = status });

        public Task DeleteServiceJobAsync(int id) => throw new NotImplementedException();

        public Task<MechanicAssignmentDto> AssignMechanicAsync(int serviceJobId, int userId, RoleInJob roleInJob) => throw new NotImplementedException();

        public Task<MechanicAssignmentDto> UpdateMechanicAssignmentRoleAsync(int assignmentId, RoleInJob roleInJob) => throw new NotImplementedException();

        public Task RemoveMechanicAssignmentAsync(int assignmentId) => throw new NotImplementedException();

        public Task<IEnumerable<MechanicAssignmentDto>> GetAssignmentsByServiceJobAsync(int serviceJobId) => throw new NotImplementedException();

        public Task<IEnumerable<ServiceJobDto>> GetJobsByMechanicAsync(int mechanicUserId) => throw new NotImplementedException();

        public Task<ServiceJobDto> SaveDiagnosticNotesAsync(int serviceJobId, string notes) => throw new NotImplementedException();
    }
}
