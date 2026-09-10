namespace Garij.Application.DTOs;

/// <summary>
/// Vehicle maintenance prediction projection based on historical service jobs and intervals.
/// </summary>
public class VehicleMaintenancePredictionDto : VehicleDto
{
    public string CustomerPhoneNumber { get; set; } = string.Empty;

    public string CustomerEmail { get; set; } = string.Empty;

    public DateTime? LastServiceDate { get; set; }

    public int? DaysSinceLastService { get; set; }

    public DateTime PredictedDueDate { get; set; }

    public int DaysOverdue { get; set; }

    public int EstimatedDaysUntilDue { get; set; }

    public int? AverageIntervalDays { get; set; }

    public int TotalServicesCompleted { get; set; }

    /// <summary>
    /// Overdue, Due Soon, or Upcoming.
    /// </summary>
    public string UrgencyLevel { get; set; } = "Upcoming";

    public string RecommendedService { get; set; } = string.Empty;

    public string PredictionReason { get; set; } = string.Empty;

    public bool HasActiveJob { get; set; }

    public string? LastJobType { get; set; }
}
