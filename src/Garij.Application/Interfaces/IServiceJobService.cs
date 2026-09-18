using Garij.Application.DTOs;
using Garij.Domain.Enums;

namespace Garij.Application.Interfaces;

public interface IServiceJobService
{
    Task<IEnumerable<ServiceJobDto>> GetAllServiceJobsAsync();

    Task<IEnumerable<ServiceJobDto>> GetServiceJobsByStatusAsync(JobStatus status);

    Task<IEnumerable<ServiceJobDto>> GetFilteredServiceJobsAsync(JobStatus? status = null, int? mechanicId = null, string? sortBy = null, string? searchTerm = null)
        => GetAllServiceJobsAsync();

    Task<ServiceJobDto?> GetServiceJobByIdAsync(int id);

    Task<ServiceJobDto?> GetServiceJobByBookingReferenceAsync(string bookingReference);

    Task<ServiceJobDto> CreateServiceJobAsync(ServiceJobDto serviceJob);

    Task<ServiceJobDto> UpdateServiceJobAsync(ServiceJobDto serviceJob);

    Task<ServiceJobDto> UpdateServiceJobStatusAsync(int id, JobStatus status);

    /// <summary>
    /// Records an Approved/Rejected decision on a notification and applies its effect on the job.
    /// For a customer approval request, approving moves the job to InProgress and rejecting moves
    /// it to Cancelled, both through the normal status transition rules. A completion notification
    /// only records the decision.
    /// </summary>
    Task<NotificationDto> RespondToNotificationAsync(int notificationId, NotificationStatus decision);

    Task DeleteServiceJobAsync(int id);

    Task<MechanicAssignmentDto> AssignMechanicAsync(int serviceJobId, int userId, RoleInJob roleInJob);

    Task<MechanicAssignmentDto> UpdateMechanicAssignmentRoleAsync(int assignmentId, RoleInJob roleInJob);

    Task RemoveMechanicAssignmentAsync(int assignmentId);

    Task<IEnumerable<MechanicAssignmentDto>> GetAssignmentsByServiceJobAsync(int serviceJobId);

    Task<IEnumerable<ServiceJobDto>> GetJobsByMechanicAsync(int mechanicUserId);

    Task<ServiceJobDto> SaveDiagnosticNotesAsync(int serviceJobId, string notes);
}
