using Garij.Application.DTOs;
using Garij.Application.Interfaces;
using Garij.Domain.Entities;
using Garij.Domain.Exceptions;
using Garij.Infrastructure.Repositories;

namespace Garij.Application.Services;

/// <summary>
/// Labour counterpart to <see cref="PartsInventoryService"/>'s parts usage logging. The
/// shapes deliberately match: pick a catalogue row, capture its price at the moment of
/// attachment, write a line item against the job. Labour draws down no stock, so there is
/// no inventory decrement and no concurrency token to re-stamp here.
/// </summary>
public class JobServiceDetailService : IJobServiceDetailService
{
    private readonly IServiceCatalogRepository _serviceCatalogRepository;
    private readonly IJobServiceDetailRepository _jobServiceDetailRepository;
    private readonly IServiceJobRepository _serviceJobRepository;
    private readonly ICurrentGarageService? _currentGarageService;

    public JobServiceDetailService(
        IServiceCatalogRepository serviceCatalogRepository,
        IJobServiceDetailRepository jobServiceDetailRepository,
        IServiceJobRepository serviceJobRepository,
        ICurrentGarageService? currentGarageService = null)
    {
        _serviceCatalogRepository = serviceCatalogRepository;
        _jobServiceDetailRepository = jobServiceDetailRepository;
        _serviceJobRepository = serviceJobRepository;
        _currentGarageService = currentGarageService;
    }

    private async Task<string> ResolveGarageIdAsync()
    {
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

    /// <summary>
    /// The catalogue is shared across garages - <see cref="ServiceCatalog"/> carries no
    /// GarageId, unlike <see cref="Part"/> - so it is returned unfiltered. The job the
    /// labour is attached to is still tenant-checked in <see cref="LogJobServiceAsync"/>.
    /// </summary>
    public async Task<IEnumerable<ServiceCatalogDto>> GetServiceCatalogAsync()
    {
        var catalogue = await _serviceCatalogRepository.GetAllAsync();
        return catalogue.OrderBy(c => c.Name).Select(ToDto);
    }

    public async Task<IEnumerable<JobServiceDetailDto>> GetServicesForJobAsync(int serviceJobId)
    {
        var job = await GetJobInCurrentGarageAsync(serviceJobId);
        return job.JobServiceDetails.Select(ToDto);
    }

    public async Task<JobServiceDetailDto> LogJobServiceAsync(JobServiceDetailDto jobServiceDetail)
    {
        // Guarded here and not only in the controller, so a direct request cannot book
        // labour at zero or negative quantity and skew the invoice subtotal.
        if (jobServiceDetail.Quantity <= 0)
        {
            throw new ValidationException(nameof(JobServiceDetailDto.Quantity), "Quantity must be at least 1.");
        }

        var job = await GetJobInCurrentGarageAsync(jobServiceDetail.ServiceJobId);

        var catalogService = await _serviceCatalogRepository.GetByIdAsync(jobServiceDetail.ServiceCatalogId)
            ?? throw new NotFoundException(nameof(ServiceCatalog), jobServiceDetail.ServiceCatalogId);

        var entity = new JobServiceDetail
        {
            ServiceJobId = job.Id,
            ServiceCatalogId = catalogService.Id,
            Quantity = jobServiceDetail.Quantity,
            // Snapshot, exactly as PriceAtUsage snapshots Part.UnitPrice: a later change to
            // the catalogue's BasePrice must not re-price labour already booked on a job.
            PriceAtBooking = catalogService.BasePrice
        };

        await _jobServiceDetailRepository.AddAsync(entity);
        await _jobServiceDetailRepository.SaveChangesAsync();

        jobServiceDetail.Id = entity.Id;
        jobServiceDetail.PriceAtBooking = entity.PriceAtBooking;
        jobServiceDetail.ServiceName = catalogService.Name;
        return jobServiceDetail;
    }

    public async Task RemoveJobServiceAsync(int jobServiceDetailId)
    {
        var entity = await _jobServiceDetailRepository.GetByIdAsync(jobServiceDetailId)
            ?? throw new NotFoundException(nameof(JobServiceDetail), jobServiceDetailId);

        // Re-resolves the parent job so labour on another garage's job cannot be removed.
        await GetJobInCurrentGarageAsync(entity.ServiceJobId);

        _jobServiceDetailRepository.Remove(entity);
        await _jobServiceDetailRepository.SaveChangesAsync();
    }

    private async Task<ServiceJob> GetJobInCurrentGarageAsync(int serviceJobId)
    {
        var garageId = await ResolveGarageIdAsync();
        var job = await _serviceJobRepository.GetByIdWithDetailsAsync(serviceJobId)
            ?? throw new NotFoundException(nameof(ServiceJob), serviceJobId);

        if ((job.GarageId ?? "default-garij-master") != garageId)
        {
            throw new NotFoundException(nameof(ServiceJob), serviceJobId);
        }

        return job;
    }

    private static ServiceCatalogDto ToDto(ServiceCatalog catalogService) => new()
    {
        Id = catalogService.Id,
        Name = catalogService.Name,
        Description = catalogService.Description,
        EstimatedDurationMinutes = catalogService.EstimatedDurationMinutes,
        BasePrice = catalogService.BasePrice
    };

    private static JobServiceDetailDto ToDto(JobServiceDetail detail) => new()
    {
        Id = detail.Id,
        ServiceJobId = detail.ServiceJobId,
        ServiceCatalogId = detail.ServiceCatalogId,
        ServiceName = detail.ServiceCatalog?.Name ?? string.Empty,
        Quantity = detail.Quantity,
        PriceAtBooking = detail.PriceAtBooking
    };
}
