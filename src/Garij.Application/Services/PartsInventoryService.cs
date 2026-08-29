using Garij.Application.DTOs;
using Garij.Application.Interfaces;
using Garij.Domain.Entities;
using Garij.Domain.Exceptions;
using Garij.Infrastructure.Repositories;

namespace Garij.Application.Services;

public class PartsInventoryService : IPartsInventoryService
{
    private readonly IPartRepository _partRepository;
    private readonly IJobPartUsedRepository _jobPartUsedRepository;

    public PartsInventoryService(IPartRepository partRepository, IJobPartUsedRepository jobPartUsedRepository)
    {
        _partRepository = partRepository;
        _jobPartUsedRepository = jobPartUsedRepository;
    }

    public async Task<IEnumerable<PartDto>> GetAllPartsAsync()
    {
        var parts = await _partRepository.GetAllAsync();
        return parts.Select(ToDto);
    }

    public async Task<PartDto?> GetPartByIdAsync(int id)
    {
        var part = await _partRepository.GetByIdAsync(id);
        return part is null ? null : ToDto(part);
    }

    public async Task<PartDto> AddPartAsync(PartDto part)
    {
        var parts = await _partRepository.GetAllAsync();
        if (parts.Any(p => p.PartNumber == part.PartNumber))
        {
            throw new ValidationException(nameof(PartDto.PartNumber), $"A part with part number '{part.PartNumber}' already exists.");
        }

        var entity = new Part
        {
            Name = part.Name,
            PartNumber = part.PartNumber,
            UnitPrice = part.UnitPrice,
            QuantityInStock = part.QuantityInStock,
            ReorderLevel = part.ReorderLevel
        };

        await _partRepository.AddAsync(entity);
        await _partRepository.SaveChangesAsync();

        return ToDto(entity);
    }

    public async Task<PartDto> UpdatePartAsync(PartDto part)
    {
        var entity = await _partRepository.GetByIdAsync(part.Id)
            ?? throw new NotFoundException(nameof(Part), part.Id);

        entity.Name = part.Name;
        entity.PartNumber = part.PartNumber;
        entity.UnitPrice = part.UnitPrice;
        entity.QuantityInStock = part.QuantityInStock;
        entity.ReorderLevel = part.ReorderLevel;

        _partRepository.Update(entity);
        await _partRepository.SaveChangesAsync();

        return ToDto(entity);
    }

    public async Task DeletePartAsync(int id)
    {
        var entity = await _partRepository.GetByIdAsync(id)
            ?? throw new NotFoundException(nameof(Part), id);

        _partRepository.Remove(entity);
        await _partRepository.SaveChangesAsync();
    }

    public async Task AdjustStockAsync(int partId, int quantityDelta)
    {
        var entity = await _partRepository.GetByIdAsync(partId)
            ?? throw new NotFoundException(nameof(Part), partId);

        var newQuantity = entity.QuantityInStock + quantityDelta;
        if (newQuantity < 0)
        {
            throw new BusinessRuleException("BR-009", $"Adjusting stock for part '{entity.Name}' by {quantityDelta} would drop quantity below zero.");
        }

        entity.QuantityInStock = newQuantity;
        _partRepository.Update(entity);
        await _partRepository.SaveChangesAsync();
    }

    public async Task<IEnumerable<PartDto>> GetLowStockPartsAsync()
    {
        var parts = await _partRepository.GetLowStockAsync();
        return parts.Select(ToDto);
    }

    public async Task<JobPartUsedDto> RecordPartUsageAsync(JobPartUsedDto jobPartUsed)
    {
        var part = await _partRepository.GetByIdAsync(jobPartUsed.PartId)
            ?? throw new NotFoundException(nameof(Part), jobPartUsed.PartId);

        var newQuantity = part.QuantityInStock - jobPartUsed.QuantityUsed;
        if (newQuantity < 0)
        {
            throw new BusinessRuleException("BR-009", $"Not enough stock for part '{part.Name}' to log a usage of {jobPartUsed.QuantityUsed}.");
        }

        part.QuantityInStock = newQuantity;
        _partRepository.Update(part);

        var entity = new JobPartUsed
        {
            ServiceJobId = jobPartUsed.ServiceJobId,
            PartId = jobPartUsed.PartId,
            QuantityUsed = jobPartUsed.QuantityUsed,
            PriceAtUsage = part.UnitPrice
        };

        await _jobPartUsedRepository.AddAsync(entity);
        await _jobPartUsedRepository.SaveChangesAsync();

        jobPartUsed.Id = entity.Id;
        jobPartUsed.PriceAtUsage = entity.PriceAtUsage;
        return jobPartUsed;
    }

    private static PartDto ToDto(Part part) => new()
    {
        Id = part.Id,
        Name = part.Name,
        PartNumber = part.PartNumber,
        UnitPrice = part.UnitPrice,
        QuantityInStock = part.QuantityInStock,
        ReorderLevel = part.ReorderLevel
    };
}
