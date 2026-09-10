using Garij.Application.DTOs;
using Garij.Domain.Enums;
using System;
using System.Collections.Generic;

namespace Garij.Web.Models;

public class StatusLookupViewModel
{
    public string Query { get; set; } = string.Empty;

    public bool WasSearched { get; set; }

    public bool MatchedByBookingReference { get; set; }

    public string? Message { get; set; }

    public VehicleDto? Vehicle { get; set; }

    public ServiceJobDto? CurrentJob { get; set; }

    public IReadOnlyList<ServiceHistoryDto> ServiceHistory { get; set; } = Array.Empty<ServiceHistoryDto>();

    public static IReadOnlyList<JobStatus> TimelineStatuses { get; } =
    [
        JobStatus.Requested,
        JobStatus.InspectionPending,
        JobStatus.CustomerApprovalNeeded,
        JobStatus.InProgress,
        JobStatus.Completed
    ];
}
