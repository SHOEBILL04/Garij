using Garij.Application.DTOs;

namespace Garij.Application.Interfaces;

public interface IReportingService
{
    Task<RevenueReportDto> GetRevenueReportAsync(DateTime periodStart, DateTime periodEnd);

    Task<IEnumerable<MechanicWorkloadDto>> GetMechanicWorkloadReportAsync(DateTime? periodStart = null, DateTime? periodEnd = null);

    Task<IEnumerable<PartsConsumptionReportDto>> GetPartsConsumptionReportAsync(DateTime periodStart, DateTime periodEnd);

    Task<IEnumerable<PartDto>> GetLowStockReportAsync();

    Task<IEnumerable<ServiceJobDto>> GetCompletedJobsReportAsync(DateTime periodStart, DateTime periodEnd);
}
