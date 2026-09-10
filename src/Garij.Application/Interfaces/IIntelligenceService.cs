using Garij.Application.DTOs;

namespace Garij.Application.Interfaces;

/// <summary>Predictive / decision-support and AI assistant features.</summary>
public interface IIntelligenceService
{
    Task<IEnumerable<VehicleDto>> PredictMaintenanceDueAsync();

    Task<IEnumerable<VehicleMaintenancePredictionDto>> FlagVehiclesDueForServiceAsync();

    Task<TimeSpan> EstimateJobDurationAsync(int serviceJobId);

    Task<IEnumerable<PartDto>> PredictPartsShortageAsync();

    Task<IEnumerable<ServiceCatalogDto>> SuggestServicesForVehicleAsync(int vehicleId);

    /// <summary>
    /// AI Smart Intake Assistant: Grounded on active ServiceCatalog entries, returns advisory service recommendations,
    /// estimated cost/duration, and clarifying questions based on the customer complaint.
    /// </summary>
    Task<IntakeSuggestionResponseDto> SuggestServicesForIntakeAsync(string complaintText, CancellationToken cancellationToken = default);
}
