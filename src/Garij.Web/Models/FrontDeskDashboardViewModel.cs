using Garij.Application.DTOs;

namespace Garij.Web.Models;

public class FrontDeskDashboardViewModel
{
    public IEnumerable<VehicleMaintenancePredictionDto> DueVehicles { get; set; } = Enumerable.Empty<VehicleMaintenancePredictionDto>();

    public int TotalDueCount => DueVehicles.Count();

    public int OverdueCount => DueVehicles.Count(v => v.UrgencyLevel == "Overdue");

    public int DueSoonCount => DueVehicles.Count(v => v.UrgencyLevel == "Due Soon");
}
