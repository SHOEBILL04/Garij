using Garij.Application.DTOs;
using Garij.Application.Interfaces;
using Garij.Domain.Entities;
using Garij.Domain.Exceptions;
using Garij.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Garij.Application.Services;

public class PartsInventoryService : IPartsInventoryService
{
    private readonly IPartRepository _partRepository;
    private readonly IJobPartUsedRepository _jobPartUsedRepository;
    private readonly ICurrentGarageService? _currentGarageService;

    public PartsInventoryService(
        IPartRepository partRepository,
        IJobPartUsedRepository jobPartUsedRepository,
        ICurrentGarageService? currentGarageService = null)
    {
        _partRepository = partRepository;
        _jobPartUsedRepository = jobPartUsedRepository;
        _currentGarageService = currentGarageService;
    }

    private async Task<string> ResolveGarageIdAsync(string? explicitGarageId = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitGarageId))
        {
            return explicitGarageId;
        }

        if (_currentGarageService != null)
        {
            var id = await _currentGarageService.GetCurrentGarageIdAsync();
            if (!string.IsNullOrWhiteSpace(id))
            {
                return id;
            }
        }

        return "default-garij-master";
    }

    public async Task<IEnumerable<PartDto>> GetAllPartsAsync()
    {
        var garageId = await ResolveGarageIdAsync();
        var parts = await _partRepository.GetAllAsync();
        return parts
            .Where(p => (p.GarageId ?? "default-garij-master") == garageId)
            .Select(ToDto);
    }

    public async Task<PartDto?> GetPartByIdAsync(int id)
    {
        var garageId = await ResolveGarageIdAsync();
        var part = await _partRepository.GetByIdAsync(id);
        if (part is null || (part.GarageId ?? "default-garij-master") != garageId)
        {
            return null;
        }

        return ToDto(part);
    }

    public async Task<PartDto> AddPartAsync(PartDto part)
    {
        var garageId = await ResolveGarageIdAsync(part.GarageId);
        var parts = await _partRepository.GetAllAsync();
        if (parts.Any(p => (p.GarageId ?? "default-garij-master") == garageId && p.PartNumber == part.PartNumber))
        {
            throw new ValidationException(nameof(PartDto.PartNumber), $"A part with part number '{part.PartNumber}' already exists.");
        }

        var entity = new Part
        {
            Name = part.Name,
            PartNumber = part.PartNumber,
            UnitPrice = part.UnitPrice,
            QuantityInStock = part.QuantityInStock,
            ReorderLevel = part.ReorderLevel,
            GarageId = garageId
        };

        await _partRepository.AddAsync(entity);
        await _partRepository.SaveChangesAsync();

        return ToDto(entity);
    }

    public async Task<PartDto> UpdatePartAsync(PartDto part)
    {
        var garageId = await ResolveGarageIdAsync(part.GarageId);
        var entity = await _partRepository.GetByIdAsync(part.Id)
            ?? throw new NotFoundException(nameof(Part), part.Id);

        if ((entity.GarageId ?? "default-garij-master") != garageId)
        {
            throw new NotFoundException(nameof(Part), part.Id);
        }

        entity.Name = part.Name;
        entity.PartNumber = part.PartNumber;
        entity.UnitPrice = part.UnitPrice;
        entity.ReorderLevel = part.ReorderLevel;
        entity.RowVersion = Guid.NewGuid();

        _partRepository.Update(entity);
        await SaveOrThrowOnConflictAsync(_partRepository, entity);

        return ToDto(entity);
    }

    public async Task DeletePartAsync(int id)
    {
        var garageId = await ResolveGarageIdAsync();
        var entity = await _partRepository.GetByIdAsync(id)
            ?? throw new NotFoundException(nameof(Part), id);

        if ((entity.GarageId ?? "default-garij-master") != garageId)
        {
            throw new NotFoundException(nameof(Part), id);
        }

        _partRepository.Remove(entity);
        await _partRepository.SaveChangesAsync();
    }

    public async Task AdjustStockAsync(int partId, int quantityDelta)
    {
        var garageId = await ResolveGarageIdAsync();
        var entity = await _partRepository.GetByIdAsync(partId)
            ?? throw new NotFoundException(nameof(Part), partId);

        if ((entity.GarageId ?? "default-garij-master") != garageId)
        {
            throw new NotFoundException(nameof(Part), partId);
        }

        var newQuantity = entity.QuantityInStock + quantityDelta;
        if (newQuantity < 0)
        {
            throw new BusinessRuleException("BR-009", $"Adjusting stock for part '{entity.Name}' by {quantityDelta} would drop quantity below zero.");
        }

        entity.QuantityInStock = newQuantity;
        entity.RowVersion = Guid.NewGuid();
        _partRepository.Update(entity);
        await SaveOrThrowOnConflictAsync(_partRepository, entity);
    }

    public async Task<IEnumerable<PartDto>> GetLowStockPartsAsync()
    {
        var garageId = await ResolveGarageIdAsync();
        var parts = await _partRepository.GetLowStockAsync();
        return parts
            .Where(p => (p.GarageId ?? "default-garij-master") == garageId)
            .Select(ToDto);
    }

    public async Task<JobPartUsedDto> RecordPartUsageAsync(JobPartUsedDto jobPartUsed)
    {
        // Guarded here and not only in the controller: a negative quantity would flip the
        // decrement below into an increment and silently manufacture stock.
        if (jobPartUsed.QuantityUsed <= 0)
        {
            throw new ValidationException(nameof(JobPartUsedDto.QuantityUsed), "Quantity used must be at least 1.");
        }

        var garageId = await ResolveGarageIdAsync();
        var part = await _partRepository.GetByIdAsync(jobPartUsed.PartId);
        if (part is null || (part.GarageId ?? "default-garij-master") != garageId)
        {
            throw new NotFoundException(nameof(Part), jobPartUsed.PartId);
        }

        var newQuantity = part.QuantityInStock - jobPartUsed.QuantityUsed;
        if (newQuantity < 0)
        {
            throw new BusinessRuleException("BR-009", $"Not enough stock for part '{part.Name}' to log a usage of {jobPartUsed.QuantityUsed}.");
        }

        part.QuantityInStock = newQuantity;
        part.RowVersion = Guid.NewGuid();
        _partRepository.Update(part);

        var entity = new JobPartUsed
        {
            ServiceJobId = jobPartUsed.ServiceJobId,
            PartId = jobPartUsed.PartId,
            QuantityUsed = jobPartUsed.QuantityUsed,
            PriceAtUsage = part.UnitPrice
        };

        await _jobPartUsedRepository.AddAsync(entity);

        // Both repositories share the request-scoped DbContext, so this single save commits
        // the stock decrement and the usage line together, or neither of them.
        await SaveOrThrowOnConflictAsync(_jobPartUsedRepository, part);

        jobPartUsed.Id = entity.Id;
        jobPartUsed.PriceAtUsage = entity.PriceAtUsage;
        return jobPartUsed;
    }

    /// <summary>
    /// Commits pending changes and converts an optimistic-concurrency failure into a
    /// business rule violation the UI can present. A conflict means another user changed
    /// this part's stock between our read and our write, so our arithmetic was based on a
    /// stale quantity and the write must be rejected rather than applied (BR-017).
    /// </summary>
    private static async Task SaveOrThrowOnConflictAsync<T>(IRepository<T> repository, Part part) where T : class
    {
        try
        {
            await repository.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BusinessRuleException(
                "BR-017",
                $"Stock for part '{part.Name}' was changed by someone else while this operation was in progress. Reload the part and try again.");
        }
    }

    private static PartDto ToDto(Part part) => new()
    {
        Id = part.Id,
        GarageId = part.GarageId,
        Name = part.Name,
        PartNumber = part.PartNumber,
        UnitPrice = part.UnitPrice,
        QuantityInStock = part.QuantityInStock,
        ReorderLevel = part.ReorderLevel
    };
}
