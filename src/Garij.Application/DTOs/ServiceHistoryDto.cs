using Garij.Domain.Enums;
using System;

namespace Garij.Application.DTOs;

public class ServiceHistoryDto
{
    public int ServiceJobId { get; set; }

    public string BookingReference { get; set; } = string.Empty;

    public JobType JobType { get; set; }

    public JobStatus Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public string VehiclePlate { get; set; } = string.Empty;

    public string VehicleDescription { get; set; } = string.Empty;
}
