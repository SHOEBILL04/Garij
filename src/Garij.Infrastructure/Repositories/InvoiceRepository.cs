using Garij.Domain.Entities;
using Garij.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Garij.Infrastructure.Repositories;

public class InvoiceRepository : Repository<Invoice>, IInvoiceRepository
{
    public InvoiceRepository(GarijDbContext context) : base(context)
    {
    }

    public async Task<Invoice?> GetByServiceJobIdAsync(int serviceJobId) =>
        await DbSet.FirstOrDefaultAsync(i => i.ServiceJobId == serviceJobId);

    public async Task<Invoice?> GetByIdWithPaymentsAsync(int id) =>
        await DbSet.Include(i => i.PaymentTransactions).FirstOrDefaultAsync(i => i.Id == id);
}
