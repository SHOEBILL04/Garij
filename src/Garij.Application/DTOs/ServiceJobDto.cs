using System.ComponentModel.DataAnnotations;
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

    // Mirrors ServiceJobConfiguration's HasMaxLength(20) on the column. Left optional: an
    // empty reference is replaced by a generated one in ServiceJobService.
    [StringLength(20, ErrorMessage = "Booking reference cannot exceed 20 characters.")]
    [Display(Name = "Booking reference")]
    public string BookingReference { get; set; } = string.Empty;

    public JobType JobType { get; set; }

    public JobStatus Status { get; set; }

    // Mirrors ServiceJobConfiguration's HasMaxLength(2000) on the column.
    [StringLength(2000, ErrorMessage = "Diagnostic notes cannot exceed 2000 characters.")]
    [Display(Name = "Diagnostic notes")]
    public string? DiagnosticNotes { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public string? GarageId { get; set; }

    public List<MechanicAssignmentDto> MechanicAssignments { get; set; } = new();

    public List<JobServiceDetailDto> JobServiceDetails { get; set; } = new();
}
