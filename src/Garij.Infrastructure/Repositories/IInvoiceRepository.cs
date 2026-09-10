using Garij.Domain.Entities;

namespace Garij.Infrastructure.Repositories;

public interface IInvoiceRepository : IRepository<Invoice>
{
    Task<Invoice?> GetByServiceJobIdAsync(int serviceJobId);

    Task<Invoice?> GetByIdWithPaymentsAsync(int id);
}
