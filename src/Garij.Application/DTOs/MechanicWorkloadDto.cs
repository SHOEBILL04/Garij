namespace Garij.Application.DTOs;

public class MechanicWorkloadDto
{
    public int UserId { get; set; }

    public string FullName { get; set; } = string.Empty;

    /// <summary>Total active jobs where the mechanic is assigned.</summary>
    public int ActiveJobCount { get; set; }

    /// <summary>Total completed jobs where the mechanic was assigned.</summary>
    public int CompletedJobCount { get; set; }

    /// <summary>Active jobs where the mechanic is Lead.</summary>
    public int LeadActiveJobCount { get; set; }

    /// <summary>Completed jobs where the mechanic was Lead.</summary>
    public int LeadCompletedJobCount { get; set; }

    /// <summary>Active jobs where the mechanic is Assisting.</summary>
    public int AssistingActiveJobCount { get; set; }

    /// <summary>Completed jobs where the mechanic was Assisting.</summary>
    public int AssistingCompletedJobCount { get; set; }

    /// <summary>Total jobs assigned (Active + Completed).</summary>
    public int TotalJobsAssigned => ActiveJobCount + CompletedJobCount;

    /// <summary>Mechanic's share of total completed jobs across all mechanics in the period (0.0% to 100.0%).</summary>
    public decimal CompletedSharePercentage { get; set; }
}
