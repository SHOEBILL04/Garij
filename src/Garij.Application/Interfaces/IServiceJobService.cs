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

    Task DeleteServiceJobAsync(int id);

    Task<MechanicAssignmentDto> AssignMechanicAsync(int serviceJobId, int userId, RoleInJob roleInJob);

    Task<MechanicAssignmentDto> UpdateMechanicAssignmentRoleAsync(int assignmentId, RoleInJob roleInJob);

    Task RemoveMechanicAssignmentAsync(int assignmentId);

    Task<IEnumerable<MechanicAssignmentDto>> GetAssignmentsByServiceJobAsync(int serviceJobId);

    Task<IEnumerable<ServiceJobDto>> GetJobsByMechanicAsync(int mechanicUserId);

    Task<ServiceJobDto> SaveDiagnosticNotesAsync(int serviceJobId, string notes);
}
