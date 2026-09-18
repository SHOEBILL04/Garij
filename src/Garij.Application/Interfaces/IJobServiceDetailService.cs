using Garij.Application.DTOs;

namespace Garij.Application.Interfaces;

/// <summary>
/// Attaches catalogue services (labour) to a service job. The parts-side equivalent is
/// <see cref="IPartsInventoryService.RecordPartUsageAsync"/>; labour has no stock to draw
/// down, so attaching a service is the price snapshot and the line item only.
/// </summary>
public interface IJobServiceDetailService
{
    Task<IEnumerable<ServiceCatalogDto>> GetServiceCatalogAsync();

    Task<IEnumerable<JobServiceDetailDto>> GetServicesForJobAsync(int serviceJobId);

    Task<JobServiceDetailDto> LogJobServiceAsync(JobServiceDetailDto jobServiceDetail);

    Task RemoveJobServiceAsync(int jobServiceDetailId);
}
