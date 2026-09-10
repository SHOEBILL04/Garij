using Garij.Domain.Entities;
using Garij.Domain.Enums;
using Garij.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Garij.Infrastructure.Repositories;

public class ServiceJobRepository : Repository<ServiceJob>, IServiceJobRepository
{
    public ServiceJobRepository(GarijDbContext context) : base(context)
    {
    }

    public async Task<ServiceJob?> GetByBookingReferenceAsync(string bookingReference)
    {
        var normalizedBookingReference = bookingReference.Trim().ToUpperInvariant();

        return await DbSet.Include(sj => sj.Vehicle)
            .Include(sj => sj.Customer)
            .Include(sj => sj.MechanicAssignments)
                .ThenInclude(ma => ma.User)
            .FirstOrDefaultAsync(sj => sj.BookingReference.ToUpper() == normalizedBookingReference);
    }

    public async Task<IEnumerable<ServiceJob>> GetServiceHistoryByVehicleAsync(int vehicleId) =>
        await DbSet.Include(sj => sj.Vehicle)
            .Include(sj => sj.Customer)
            .Where(sj => sj.VehicleId == vehicleId)
            .OrderByDescending(sj => sj.CompletedAt ?? sj.CreatedAt)
            .ThenByDescending(sj => sj.CreatedAt)
            .ToListAsync();

    public async Task<IEnumerable<ServiceJob>> GetAllWithDetailsAsync() =>
        await DbSet.Include(sj => sj.Vehicle)
            .Include(sj => sj.Customer)
            .Include(sj => sj.MechanicAssignments)
                .ThenInclude(ma => ma.User)
            .OrderByDescending(sj => sj.CreatedAt)
            .ToListAsync();

    public async Task<ServiceJob?> GetByIdWithDetailsAsync(int id) =>
        await DbSet.Include(sj => sj.Vehicle)
            .Include(sj => sj.Customer)
            .Include(sj => sj.MechanicAssignments)
                .ThenInclude(ma => ma.User)
            .Include(sj => sj.JobServiceDetails)
                .ThenInclude(jsd => jsd.ServiceCatalog)
            .Include(sj => sj.JobPartsUsed)
                .ThenInclude(jpu => jpu.Part)
            .FirstOrDefaultAsync(sj => sj.Id == id);

    public async Task<IEnumerable<ServiceJob>> GetJobsByStatusAsync(JobStatus status) =>
        await DbSet.Include(sj => sj.Vehicle)
            .Include(sj => sj.Customer)
            .Include(sj => sj.MechanicAssignments)
                .ThenInclude(ma => ma.User)
            .Where(sj => sj.Status == status)
            .OrderByDescending(sj => sj.CreatedAt)
            .ToListAsync();

    public async Task<IEnumerable<ServiceJob>> GetJobsByMechanicAsync(int mechanicUserId) =>
        await DbSet.Include(sj => sj.Vehicle)
            .Include(sj => sj.Customer)
            .Include(sj => sj.MechanicAssignments)
                .ThenInclude(ma => ma.User)
            .Include(sj => sj.JobPartsUsed)
                .ThenInclude(jpu => jpu.Part)
            .Where(sj => sj.MechanicAssignments.Any(ma => ma.UserId == mechanicUserId))
            .OrderByDescending(sj => sj.CreatedAt)
            .ToListAsync();
}
