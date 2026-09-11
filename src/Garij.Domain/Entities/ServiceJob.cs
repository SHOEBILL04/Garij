using Garij.Domain.Enums;

namespace Garij.Domain.Entities;

public class ServiceJob
{
    public int Id { get; set; }

    public int CustomerId { get; set; }

    public Customer Customer { get; set; } = null!;

    public int VehicleId { get; set; }

    public Vehicle Vehicle { get; set; } = null!;

    /// <summary>Unique reference used for public status lookup.</summary>
    public string BookingReference { get; set; } = string.Empty;

    public JobType JobType { get; set; }

    public JobStatus Status { get; set; }

    public string? DiagnosticNotes { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public string? GarageId { get; set; }

    public ICollection<JobServiceDetail> JobServiceDetails { get; set; } = new List<JobServiceDetail>();

    /// <summary>Lead mechanic is tracked only via MechanicAssignment.RoleInJob, not a dedicated FK here.</summary>
    public ICollection<MechanicAssignment> MechanicAssignments { get; set; } = new List<MechanicAssignment>();

    public ICollection<JobPartUsed> JobPartsUsed { get; set; } = new List<JobPartUsed>();

    public Invoice? Invoice { get; set; }

    public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
}
