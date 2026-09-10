using Garij.Domain.Entities;
using Garij.Domain.Enums;

namespace Garij.Infrastructure.Repositories;

public interface IServiceJobRepository : IRepository<ServiceJob>
{
    Task<ServiceJob?> GetByBookingReferenceAsync(string bookingReference);

    Task<IEnumerable<ServiceJob>> GetServiceHistoryByVehicleAsync(int vehicleId);

    Task<IEnumerable<ServiceJob>> GetAllWithDetailsAsync();

    Task<ServiceJob?> GetByIdWithDetailsAsync(int id);

    Task<IEnumerable<ServiceJob>> GetJobsByStatusAsync(JobStatus status);

    Task<IEnumerable<ServiceJob>> GetJobsByMechanicAsync(int mechanicUserId);
}
