using Garij.Application.DTOs;
using Garij.Application.Interfaces;
using Garij.Domain.Entities;
using Garij.Domain.Enums;
using Garij.Domain.Exceptions;
using Garij.Infrastructure.Repositories;

namespace Garij.Application.Services;

public class ServiceJobService : IServiceJobService
{
    private readonly IServiceJobRepository _serviceJobRepository;
    private readonly IVehicleRepository _vehicleRepository;
    private readonly IUserRepository _userRepository;
    private readonly IMechanicAssignmentRepository _mechanicAssignmentRepository;
    private readonly INotificationService _notificationService;

    public ServiceJobService(
        IServiceJobRepository serviceJobRepository,
        IVehicleRepository vehicleRepository,
        IUserRepository userRepository,
        IMechanicAssignmentRepository mechanicAssignmentRepository,
        INotificationService notificationService)
    {
        _serviceJobRepository = serviceJobRepository;
        _vehicleRepository = vehicleRepository;
        _userRepository = userRepository;
        _mechanicAssignmentRepository = mechanicAssignmentRepository;
        _notificationService = notificationService;
    }

    public async Task<IEnumerable<ServiceJobDto>> GetAllServiceJobsAsync()
    {
        var jobs = await _serviceJobRepository.GetAllWithDetailsAsync();
        return jobs.Select(MapToDto);
    }

    public async Task<IEnumerable<ServiceJobDto>> GetServiceJobsByStatusAsync(JobStatus status)
    {
        var jobs = await _serviceJobRepository.GetJobsByStatusAsync(status);
        return jobs.Select(MapToDto);
    }

    public async Task<IEnumerable<ServiceJobDto>> GetFilteredServiceJobsAsync(JobStatus? status = null, int? mechanicId = null, string? sortBy = null, string? searchTerm = null)
    {
        var jobs = await _serviceJobRepository.GetAllWithDetailsAsync();

        if (status.HasValue)
        {
            jobs = jobs.Where(j => j.Status == status.Value);
        }

        if (mechanicId.HasValue && mechanicId.Value > 0)
        {
            jobs = jobs.Where(j => j.MechanicAssignments != null && j.MechanicAssignments.Any(ma => ma.UserId == mechanicId.Value));
        }

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim();
            jobs = jobs.Where(j =>
                j.BookingReference.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (j.Vehicle != null && j.Vehicle.LicensePlateNumber.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (j.Customer != null && j.Customer.FullName.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (j.Vehicle != null && (j.Vehicle.Make.Contains(term, StringComparison.OrdinalIgnoreCase) || j.Vehicle.Model.Contains(term, StringComparison.OrdinalIgnoreCase))));
        }

        var dtos = jobs.Select(MapToDto);

        dtos = sortBy?.ToLowerInvariant() switch
        {
            "date_asc" => dtos.OrderBy(j => j.CreatedAt),
            "date_desc" => dtos.OrderByDescending(j => j.CreatedAt),
            "status" => dtos.OrderBy(j => j.Status).ThenByDescending(j => j.CreatedAt),
            "plate" => dtos.OrderBy(j => j.VehiclePlateNumber),
            _ => dtos.OrderByDescending(j => j.CreatedAt)
        };

        return dtos.ToList();
    }

    public async Task<ServiceJobDto?> GetServiceJobByIdAsync(int id)
    {
        var job = await _serviceJobRepository.GetByIdWithDetailsAsync(id);
        return job is null ? null : MapToDto(job);
    }

    public async Task<ServiceJobDto?> GetServiceJobByBookingReferenceAsync(string bookingReference)
    {
        var normalizedBookingReference = bookingReference.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalizedBookingReference))
        {
            return null;
        }

        var job = await _serviceJobRepository.GetByBookingReferenceAsync(normalizedBookingReference);
        return job is null ? null : MapToDto(job);
    }

    public async Task<ServiceJobDto> CreateServiceJobAsync(ServiceJobDto serviceJobDto)
    {
        var vehicle = await _vehicleRepository.GetByIdWithCustomerAsync(serviceJobDto.VehicleId)
            ?? throw new NotFoundException(nameof(Vehicle), serviceJobDto.VehicleId);

        string bookingRef = serviceJobDto.BookingReference;
        if (string.IsNullOrWhiteSpace(bookingRef))
        {
            bookingRef = await GenerateUniqueBookingReferenceAsync();
        }
        else
        {
            var existing = await _serviceJobRepository.GetByBookingReferenceAsync(bookingRef);
            if (existing is not null)
            {
                throw new BusinessRuleException("BR-003", $"Booking reference '{bookingRef}' already exists.");
            }
        }

        var entity = new ServiceJob
        {
            VehicleId = vehicle.Id,
            CustomerId = vehicle.CustomerId,
            BookingReference = bookingRef,
            JobType = serviceJobDto.JobType,
            Status = serviceJobDto.Status == 0 ? JobStatus.Requested : serviceJobDto.Status,
            DiagnosticNotes = serviceJobDto.DiagnosticNotes?.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        await _serviceJobRepository.AddAsync(entity);
        await _serviceJobRepository.SaveChangesAsync();

        var saved = await _serviceJobRepository.GetByIdWithDetailsAsync(entity.Id);
        return MapToDto(saved ?? entity);
    }

    public async Task<ServiceJobDto> UpdateServiceJobAsync(ServiceJobDto serviceJobDto)
    {
        var entity = await _serviceJobRepository.GetByIdWithDetailsAsync(serviceJobDto.Id)
            ?? throw new NotFoundException(nameof(ServiceJob), serviceJobDto.Id);

        if (entity.Status != serviceJobDto.Status)
        {
            ValidateStatusTransition(entity.Status, serviceJobDto.Status);
        }

        entity.JobType = serviceJobDto.JobType;
        entity.Status = serviceJobDto.Status;
        entity.DiagnosticNotes = serviceJobDto.DiagnosticNotes?.Trim();
        if (serviceJobDto.Status == JobStatus.Completed && entity.CompletedAt is null)
        {
            entity.CompletedAt = DateTime.UtcNow;
            await NotifyJobCompletedAsync(entity);
        }

        _serviceJobRepository.Update(entity);
        await _serviceJobRepository.SaveChangesAsync();

        return MapToDto(entity);
    }

    public async Task<ServiceJobDto> UpdateServiceJobStatusAsync(int id, JobStatus status)
    {
        var entity = await _serviceJobRepository.GetByIdWithDetailsAsync(id)
            ?? throw new NotFoundException(nameof(ServiceJob), id);

        if (entity.Status != status)
        {
            ValidateStatusTransition(entity.Status, status);
        }

        entity.Status = status;
        if (status == JobStatus.Completed && entity.CompletedAt is null)
        {
            entity.CompletedAt = DateTime.UtcNow;
            await NotifyJobCompletedAsync(entity);
        }

        _serviceJobRepository.Update(entity);
        await _serviceJobRepository.SaveChangesAsync();

        return MapToDto(entity);
    }

    public async Task DeleteServiceJobAsync(int id)
    {
        var entity = await _serviceJobRepository.GetByIdAsync(id)
            ?? throw new NotFoundException(nameof(ServiceJob), id);

        _serviceJobRepository.Remove(entity);
        await _serviceJobRepository.SaveChangesAsync();
    }

    public async Task<MechanicAssignmentDto> AssignMechanicAsync(int serviceJobId, int userId, RoleInJob roleInJob)
    {
        var job = await _serviceJobRepository.GetByIdAsync(serviceJobId)
            ?? throw new NotFoundException(nameof(ServiceJob), serviceJobId);

        var mechanic = await _userRepository.GetByIdAsync(userId)
            ?? throw new NotFoundException(nameof(User), userId);

        if (mechanic.Role != UserRole.Mechanic)
        {
            throw new BusinessRuleException("BR-003", $"Only users with the Mechanic role can be assigned to service jobs. User '{mechanic.FullName}' has role '{mechanic.Role}'.");
        }

        var existingAssignments = await _mechanicAssignmentRepository.GetAssignmentsByJobIdAsync(serviceJobId);

        if (existingAssignments.Any(a => a.UserId == userId))
        {
            throw new BusinessRuleException("BR-003", $"Mechanic '{mechanic.FullName}' is already assigned to this job.");
        }

        if (roleInJob == RoleInJob.Lead && existingAssignments.Any(a => a.RoleInJob == RoleInJob.Lead))
        {
            throw new BusinessRuleException("BR-003", "This job already has a Lead mechanic assigned. Exactly one Lead mechanic is allowed per job.");
        }

        var assignment = new MechanicAssignment
        {
            ServiceJobId = serviceJobId,
            UserId = userId,
            RoleInJob = roleInJob,
            AssignedAt = DateTime.UtcNow
        };

        await _mechanicAssignmentRepository.AddAsync(assignment);
        await _mechanicAssignmentRepository.SaveChangesAsync();

        return new MechanicAssignmentDto
        {
            Id = assignment.Id,
            ServiceJobId = assignment.ServiceJobId,
            UserId = assignment.UserId,
            MechanicName = mechanic.FullName,
            RoleInJob = assignment.RoleInJob,
            AssignedAt = assignment.AssignedAt
        };
    }

    public async Task<MechanicAssignmentDto> UpdateMechanicAssignmentRoleAsync(int assignmentId, RoleInJob roleInJob)
    {
        var assignment = await _mechanicAssignmentRepository.GetByIdAsync(assignmentId)
            ?? throw new NotFoundException(nameof(MechanicAssignment), assignmentId);

        if (roleInJob == RoleInJob.Lead && assignment.RoleInJob != RoleInJob.Lead)
        {
            var existingAssignments = await _mechanicAssignmentRepository.GetAssignmentsByJobIdAsync(assignment.ServiceJobId);
            if (existingAssignments.Any(a => a.Id != assignmentId && a.RoleInJob == RoleInJob.Lead))
            {
                throw new BusinessRuleException("BR-003", "This job already has a Lead mechanic assigned. Exactly one Lead mechanic is allowed per job.");
            }
        }

        assignment.RoleInJob = roleInJob;
        _mechanicAssignmentRepository.Update(assignment);
        await _mechanicAssignmentRepository.SaveChangesAsync();

        var mechanic = await _userRepository.GetByIdAsync(assignment.UserId);

        return new MechanicAssignmentDto
        {
            Id = assignment.Id,
            ServiceJobId = assignment.ServiceJobId,
            UserId = assignment.UserId,
            MechanicName = mechanic?.FullName ?? "Unknown",
            RoleInJob = assignment.RoleInJob,
            AssignedAt = assignment.AssignedAt
        };
    }

    public async Task RemoveMechanicAssignmentAsync(int assignmentId)
    {
        var assignment = await _mechanicAssignmentRepository.GetByIdAsync(assignmentId)
            ?? throw new NotFoundException(nameof(MechanicAssignment), assignmentId);

        _mechanicAssignmentRepository.Remove(assignment);
        await _mechanicAssignmentRepository.SaveChangesAsync();
    }

    public async Task<IEnumerable<MechanicAssignmentDto>> GetAssignmentsByServiceJobAsync(int serviceJobId)
    {
        var assignments = await _mechanicAssignmentRepository.GetAssignmentsByJobIdAsync(serviceJobId);
        return assignments.Select(a => new MechanicAssignmentDto
        {
            Id = a.Id,
            ServiceJobId = a.ServiceJobId,
            UserId = a.UserId,
            MechanicName = a.User?.FullName ?? "Unknown",
            RoleInJob = a.RoleInJob,
            AssignedAt = a.AssignedAt
        });
    }

    public async Task<IEnumerable<ServiceJobDto>> GetJobsByMechanicAsync(int mechanicUserId)
    {
        var jobs = await _serviceJobRepository.GetJobsByMechanicAsync(mechanicUserId);
        return jobs.Select(MapToDto);
    }

    public async Task<ServiceJobDto> SaveDiagnosticNotesAsync(int serviceJobId, string notes)
    {
        var entity = await _serviceJobRepository.GetByIdWithDetailsAsync(serviceJobId)
            ?? throw new NotFoundException(nameof(ServiceJob), serviceJobId);

        entity.DiagnosticNotes = notes?.Trim();
        _serviceJobRepository.Update(entity);
        await _serviceJobRepository.SaveChangesAsync();

        return MapToDto(entity);
    }

    /// <summary>
    /// The order a job advances through. Stages may be skipped - a mechanic who
    /// finishes the work outright can move a job straight to Completed without
    /// stepping through inspection and approval - but a job never moves back to
    /// an earlier stage. Cancelled sits outside this pipeline and is reachable
    /// from any stage that is not already terminal.
    /// </summary>
    private static readonly JobStatus[] StatusPipeline =
    [
        JobStatus.Requested,
        JobStatus.InspectionPending,
        JobStatus.CustomerApprovalNeeded,
        JobStatus.InProgress,
        JobStatus.Completed
    ];

    private static void ValidateStatusTransition(JobStatus currentStatus, JobStatus newStatus)
    {
        if (currentStatus == newStatus)
        {
            return;
        }

        if (currentStatus == JobStatus.Completed)
        {
            throw new BusinessRuleException("BR-007", "Cannot change status of a job that is already Completed.");
        }

        if (currentStatus == JobStatus.Cancelled)
        {
            throw new BusinessRuleException("BR-007", "Cannot change status of a job that is Cancelled.");
        }

        if (newStatus == JobStatus.Cancelled)
        {
            return;
        }

        var currentStage = Array.IndexOf(StatusPipeline, currentStatus);
        var newStage = Array.IndexOf(StatusPipeline, newStatus);

        if (newStage <= currentStage)
        {
            throw new BusinessRuleException("BR-007", $"Invalid status transition from '{currentStatus}' to '{newStatus}'. A job moves forward through: Requested -> InspectionPending -> CustomerApprovalNeeded -> InProgress -> Completed. Stages may be skipped, but a job cannot move back to an earlier stage.");
        }
    }

    private async Task<string> GenerateUniqueBookingReferenceAsync()
    {
        var year = DateTime.UtcNow.Year;
        var existingJobs = await _serviceJobRepository.GetAllAsync();
        int count = existingJobs.Count() + 1;

        string candidate;
        do
        {
            candidate = $"GRJ-{year}-{count:D4}";
            count++;
        }
        while (await _serviceJobRepository.GetByBookingReferenceAsync(candidate) is not null);

        return candidate;
    }

    private async Task NotifyJobCompletedAsync(ServiceJob entity)
    {
        await _notificationService.CreateNotificationAsync(new NotificationDto
        {
            ServiceJobId = entity.Id,
            Message = $"Job {entity.BookingReference} has been completed and is ready for review."
        });
    }

    private static ServiceJobDto MapToDto(ServiceJob job) => new()
    {
        Id = job.Id,
        CustomerId = job.CustomerId,
        CustomerName = job.Customer?.FullName ?? string.Empty,
        VehicleId = job.VehicleId,
        VehiclePlateNumber = job.Vehicle?.LicensePlateNumber ?? string.Empty,
        VehicleDescription = job.Vehicle is null ? string.Empty : $"{job.Vehicle.Year} {job.Vehicle.Make} {job.Vehicle.Model}".Trim(),
        BookingReference = job.BookingReference,
        JobType = job.JobType,
        Status = job.Status,
        DiagnosticNotes = job.DiagnosticNotes,
        CreatedAt = job.CreatedAt,
        CompletedAt = job.CompletedAt,
        MechanicAssignments = (job.MechanicAssignments ?? Enumerable.Empty<MechanicAssignment>()).Select(ma => new MechanicAssignmentDto
        {
            Id = ma.Id,
            ServiceJobId = ma.ServiceJobId,
            UserId = ma.UserId,
            MechanicName = ma.User?.FullName ?? "Unknown",
            RoleInJob = ma.RoleInJob,
            AssignedAt = ma.AssignedAt
        }).ToList()
    };
}
