using Garij.Application.DTOs;
using Garij.Application.Services;
using Garij.Domain.Entities;
using Garij.Domain.Exceptions;
using Garij.Infrastructure.Repositories;

namespace Garij.UnitTests;

public class PartsInventoryServiceTests
{
    [Fact]
    public async Task AdjustStockAsync_ShouldThrowBusinessRuleException_WhenAdjustmentWouldGoNegative()
    {
        var part = new Part { Id = 1, Name = "Oil Filter Type-A", PartNumber = "FLT-OIL-A", UnitPrice = 12.00m, QuantityInStock = 5, ReorderLevel = 2 };
        var partRepository = new FakePartRepository(part);
        var service = new PartsInventoryService(partRepository, new FakeJobPartUsedRepository());

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => service.AdjustStockAsync(1, -10));

        Assert.Equal("BR-009", ex.RuleCode);
        Assert.Equal(5, part.QuantityInStock);
    }

    [Fact]
    public async Task RecordPartUsageAsync_ShouldLockPriceAtUsageTime_UnaffectedByLaterPriceChanges()
    {
        var part = new Part { Id = 1, Name = "Spark Plug Set Platinum", PartNumber = "SPK-PLG-P", UnitPrice = 45.00m, QuantityInStock = 10, ReorderLevel = 2 };
        var partRepository = new FakePartRepository(part);
        var service = new PartsInventoryService(partRepository, new FakeJobPartUsedRepository());

        var usage = await service.RecordPartUsageAsync(new JobPartUsedDto { ServiceJobId = 1, PartId = 1, QuantityUsed = 2 });

        Assert.Equal(45.00m, usage.PriceAtUsage);
        Assert.Equal(8, part.QuantityInStock);

        part.UnitPrice = 99.00m;

        Assert.Equal(45.00m, usage.PriceAtUsage);
    }

    [Fact]
    public async Task RecordPartUsageAsync_ShouldThrowBusinessRuleException_WhenUsageExceedsStock()
    {
        var part = new Part { Id = 1, Name = "Timing Belt Kit", PartNumber = "BLT-TMG-K", UnitPrice = 85.00m, QuantityInStock = 1, ReorderLevel = 2 };
        var partRepository = new FakePartRepository(part);
        var service = new PartsInventoryService(partRepository, new FakeJobPartUsedRepository());

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.RecordPartUsageAsync(new JobPartUsedDto { ServiceJobId = 1, PartId = 1, QuantityUsed = 5 }));

        Assert.Equal("BR-009", ex.RuleCode);
        Assert.Equal(1, part.QuantityInStock);
    }

    private sealed class FakePartRepository : IPartRepository
    {
        private readonly Dictionary<int, Part> _parts = new();
        private int _nextId = 1;

        public FakePartRepository(params Part[] seed)
        {
            foreach (var part in seed)
            {
                _parts[part.Id] = part;
                _nextId = Math.Max(_nextId, part.Id + 1);
            }
        }

        public Task<Part?> GetByIdAsync(int id) => Task.FromResult(_parts.GetValueOrDefault(id));

        public Task<IEnumerable<Part>> GetAllAsync() => Task.FromResult<IEnumerable<Part>>(_parts.Values.ToList());

        public Task AddAsync(Part entity)
        {
            entity.Id = _nextId++;
            _parts[entity.Id] = entity;
            return Task.CompletedTask;
        }

        public void Update(Part entity) => _parts[entity.Id] = entity;

        public void Remove(Part entity) => _parts.Remove(entity.Id);

        public Task<int> SaveChangesAsync() => Task.FromResult(0);

        public Task<IEnumerable<Part>> GetLowStockAsync() =>
            Task.FromResult<IEnumerable<Part>>(_parts.Values.Where(p => p.QuantityInStock <= p.ReorderLevel).ToList());
    }

    private sealed class FakeJobPartUsedRepository : IJobPartUsedRepository
    {
        private readonly List<JobPartUsed> _items = new();
        private int _nextId = 1;

        public Task<JobPartUsed?> GetByIdAsync(int id) => Task.FromResult(_items.FirstOrDefault(i => i.Id == id));

        public Task<IEnumerable<JobPartUsed>> GetAllAsync() => Task.FromResult<IEnumerable<JobPartUsed>>(_items.ToList());

        public Task AddAsync(JobPartUsed entity)
        {
            entity.Id = _nextId++;
            _items.Add(entity);
            return Task.CompletedTask;
        }

        public void Update(JobPartUsed entity)
        {
        }

        public void Remove(JobPartUsed entity) => _items.Remove(entity);

        public Task<int> SaveChangesAsync() => Task.FromResult(0);
    }
}
