using Garij.Domain.Enums;

namespace Garij.Application.DTOs;

public class ServiceJobDto
{
    public int Id { get; set; }

    public int CustomerId { get; set; }

    public string CustomerName { get; set; } = string.Empty;

    public int VehicleId { get; set; }

    public string VehiclePlateNumber { get; set; } = string.Empty;

    public string VehicleDescription { get; set; } = string.Empty;

    public string BookingReference { get; set; } = string.Empty;

    public JobType JobType { get; set; }

    public JobStatus Status { get; set; }

    public string? DiagnosticNotes { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public List<MechanicAssignmentDto> MechanicAssignments { get; set; } = new();
}
