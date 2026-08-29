using Garij.Application.DTOs;
using Garij.Domain.Enums;

namespace Garij.Application.Interfaces;

public interface IServiceJobService
{
    Task<IEnumerable<ServiceJobDto>> GetAllServiceJobsAsync();

    Task<IEnumerable<ServiceJobDto>> GetServiceJobsByStatusAsync(JobStatus status);

    Task<ServiceJobDto?> GetServiceJobByIdAsync(int id);

    Task<ServiceJobDto?> GetServiceJobByBookingReferenceAsync(string bookingReference);

    Task<ServiceJobDto> CreateServiceJobAsync(ServiceJobDto serviceJob);

    Task<ServiceJobDto> UpdateServiceJobAsync(ServiceJobDto serviceJob);

    Task<ServiceJobDto> UpdateServiceJobStatusAsync(int id, JobStatus status);

    Task DeleteServiceJobAsync(int id);

    Task<MechanicAssignmentDto> AssignMechanicAsync(int serviceJobId, int userId, RoleInJob roleInJob);

    Task RemoveMechanicAssignmentAsync(int assignmentId);

    Task<IEnumerable<MechanicAssignmentDto>> GetAssignmentsByServiceJobAsync(int serviceJobId);
}
