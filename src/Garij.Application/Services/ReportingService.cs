using System.Globalization;
using Garij.Application.DTOs;
using Garij.Application.Interfaces;
using Garij.Domain.Enums;
using Garij.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Garij.Application.Services;

public class ReportingService : IReportingService
{
    private readonly GarijDbContext _context;
    private readonly ICurrentGarageService? _currentGarageService;

    public ReportingService(GarijDbContext context, ICurrentGarageService? currentGarageService = null)
    {
        _context = context;
        _currentGarageService = currentGarageService;
    }

    private async Task<string> ResolveGarageIdAsync(string? explicitGarageId = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitGarageId))
        {
            return explicitGarageId;
        }

        if (_currentGarageService != null)
        {
            var id = await _currentGarageService.GetCurrentGarageIdAsync();
            if (!string.IsNullOrWhiteSpace(id))
            {
                return id;
            }
        }

        return "default-garij-master";
    }

    public async Task<RevenueReportDto> GetRevenueReportAsync(DateTime periodStart, DateTime periodEnd)
    {
        var garageId = await ResolveGarageIdAsync();

        // Normalize range to include full end day if midnight
        var rangeEnd = periodEnd.TimeOfDay == TimeSpan.Zero
            ? periodEnd.Date.AddDays(1).AddTicks(-1)
            : periodEnd;

        // 1. Billed Revenue: Grouped by Invoice.IssuedAt month (excluding Refunded/Void invoices)
        var billedQuery = _context.Invoices.AsNoTracking()
            .Where(i => (i.GarageId ?? "default-garij-master") == garageId &&
                        i.IssuedAt >= periodStart && i.IssuedAt <= rangeEnd && i.PaymentStatus != PaymentStatus.Refunded);


        var billedList = await billedQuery
            .GroupBy(i => new { i.IssuedAt.Year, i.IssuedAt.Month })
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Month,
                NetRevenue = g.Sum(i => i.SubTotal),
                GrossRevenue = g.Sum(i => i.TotalAmount),
                InvoiceCount = g.Count()
            })
            .OrderBy(b => b.Year).ThenBy(b => b.Month)
            .ToListAsync();

        var billedItems = billedList.Select(b => new MonthlyBilledRevenueItemDto
        {
            Year = b.Year,
            Month = b.Month,
            MonthName = CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(b.Month) + " " + b.Year,
            NetRevenue = b.NetRevenue,
            GrossRevenue = b.GrossRevenue,
            InvoiceCount = b.InvoiceCount
        }).ToList();

        // 2. Collected Revenue: Grouped by PaymentTransaction.PaidAt month
        var collectedQuery = _context.PaymentTransactions.AsNoTracking()
            .Where(pt => (pt.Invoice.GarageId ?? "default-garij-master") == garageId &&
                         pt.PaidAt >= periodStart && pt.PaidAt <= rangeEnd);

        var collectedList = await collectedQuery
            .GroupBy(pt => new { pt.PaidAt.Year, pt.PaidAt.Month })
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Month,
                CollectedAmount = g.Sum(pt => pt.Amount),
                TransactionCount = g.Count()
            })
            .OrderBy(c => c.Year).ThenBy(c => c.Month)
            .ToListAsync();

        var collectedItems = collectedList.Select(c => new MonthlyCollectedRevenueItemDto
        {
            Year = c.Year,
            Month = c.Month,
            MonthName = CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(c.Month) + " " + c.Year,
            CollectedAmount = c.CollectedAmount,
            TransactionCount = c.TransactionCount
        }).ToList();

        var totalBilledNet = billedItems.Sum(b => b.NetRevenue);
        var totalBilledGross = billedItems.Sum(b => b.GrossRevenue);
        var totalCollected = collectedItems.Sum(c => c.CollectedAmount);
        var totalInvoices = billedItems.Sum(b => b.InvoiceCount);
        var avgInvoiceValue = totalInvoices > 0 ? Math.Round(totalBilledGross / totalInvoices, 2) : 0m;

        // 3. Refunded Invoices: Invoices issued in period with PaymentStatus.Refunded
        var refundedQuery = _context.Invoices.AsNoTracking()
            .Where(i => (i.GarageId ?? "default-garij-master") == garageId &&
                        i.IssuedAt >= periodStart && i.IssuedAt <= rangeEnd && i.PaymentStatus == PaymentStatus.Refunded);

        var refundedInvoiceCount = await refundedQuery.CountAsync();
        var refundedGrossAmount = await refundedQuery.SumAsync(i => (decimal?)i.TotalAmount) ?? 0m;

        return new RevenueReportDto
        {
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            TotalBilledNet = totalBilledNet,
            TotalBilledGross = totalBilledGross,
            TotalCollected = totalCollected,
            TotalInvoices = totalInvoices,
            AverageInvoiceValue = avgInvoiceValue,
            RefundedInvoiceCount = refundedInvoiceCount,
            RefundedGrossAmount = refundedGrossAmount,
            MonthlyBilledBreakdown = billedItems,
            MonthlyCollectedBreakdown = collectedItems
        };
    }

    public async Task<IEnumerable<PartsConsumptionReportDto>> GetPartsConsumptionReportAsync(DateTime periodStart, DateTime periodEnd)
    {
        var garageId = await ResolveGarageIdAsync();
        var rangeEnd = periodEnd.TimeOfDay == TimeSpan.Zero
            ? periodEnd.Date.AddDays(1).AddTicks(-1)
            : periodEnd;

        // JobPartUsed has no timestamp. Filter is anchored on ServiceJob.CompletedAt,
        // falling back to ServiceJob.CreatedAt when CompletedAt is null.
        var usages = await _context.JobPartsUsed.AsNoTracking()
            .Include(jpu => jpu.Part)
            .Include(jpu => jpu.ServiceJob)
            .Where(jpu => (jpu.ServiceJob.GarageId ?? "default-garij-master") == garageId &&
                          (jpu.ServiceJob.CompletedAt ?? jpu.ServiceJob.CreatedAt) >= periodStart &&
                          (jpu.ServiceJob.CompletedAt ?? jpu.ServiceJob.CreatedAt) <= rangeEnd)
            .ToListAsync();

        var grouped = usages
            .GroupBy(u => u.PartId)
            .Select(g =>
            {
                var part = g.First().Part;
                var totalQty = g.Sum(x => x.QuantityUsed);
                var totalCost = g.Sum(x => x.QuantityUsed * x.PriceAtUsage);

                return new PartsConsumptionReportDto
                {
                    PartId = part.Id,
                    PartName = part.Name,
                    PartNumber = part.PartNumber,
                    TotalQuantityUsed = totalQty,
                    TotalCost = Math.Round(totalCost, 2),
                    CurrentStock = part.QuantityInStock,
                    ReorderLevel = part.ReorderLevel,
                    IsLowStock = part.QuantityInStock <= part.ReorderLevel
                };
            })
            .OrderByDescending(p => p.TotalCost)
            .ToList();

        return grouped;
    }

    public async Task<IEnumerable<MechanicWorkloadDto>> GetMechanicWorkloadReportAsync(DateTime? periodStart = null, DateTime? periodEnd = null)
    {
        var garageId = await ResolveGarageIdAsync();
        var rangeEnd = periodEnd.HasValue && periodEnd.Value.TimeOfDay == TimeSpan.Zero
            ? periodEnd.Value.Date.AddDays(1).AddTicks(-1)
            : periodEnd;

        var mechanics = await _context.StaffUsers.AsNoTracking()
            .Where(u => (u.GarageId ?? "default-garij-master") == garageId && u.Role == UserRole.Mechanic)
            .OrderBy(u => u.FullName)
            .ToListAsync();

        var assignmentsQuery = _context.MechanicAssignments.AsNoTracking()
            .Include(ma => ma.ServiceJob)
            .Where(ma => (ma.ServiceJob.GarageId ?? "default-garij-master") == garageId)
            .AsQueryable();

        if (periodStart.HasValue)
        {
            assignmentsQuery = assignmentsQuery.Where(ma => ma.AssignedAt >= periodStart.Value);
        }

        if (rangeEnd.HasValue)
        {
            assignmentsQuery = assignmentsQuery.Where(ma => ma.AssignedAt <= rangeEnd.Value);
        }

        var assignments = await assignmentsQuery.ToListAsync();

        var workloadList = new List<MechanicWorkloadDto>();

        foreach (var m in mechanics)
        {
            var userAssignments = assignments.Where(a => a.UserId == m.Id).ToList();

            var activeLead = userAssignments.Count(a => a.RoleInJob == RoleInJob.Lead &&
                                                        a.ServiceJob.Status != JobStatus.Completed &&
                                                        a.ServiceJob.Status != JobStatus.Cancelled);

            var completedLead = userAssignments.Count(a => a.RoleInJob == RoleInJob.Lead &&
                                                           a.ServiceJob.Status == JobStatus.Completed);

            var activeAssisting = userAssignments.Count(a => a.RoleInJob == RoleInJob.Assistant &&
                                                             a.ServiceJob.Status != JobStatus.Completed &&
                                                             a.ServiceJob.Status != JobStatus.Cancelled);

            var completedAssisting = userAssignments.Count(a => a.RoleInJob == RoleInJob.Assistant &&
                                                                a.ServiceJob.Status == JobStatus.Completed);


            workloadList.Add(new MechanicWorkloadDto
            {
                UserId = m.Id,
                FullName = m.FullName,
                ActiveJobCount = activeLead + activeAssisting,
                CompletedJobCount = completedLead + completedAssisting,
                LeadActiveJobCount = activeLead,
                LeadCompletedJobCount = completedLead,
                AssistingActiveJobCount = activeAssisting,
                AssistingCompletedJobCount = completedAssisting
            });
        }

        var totalCompletedShop = workloadList.Sum(w => w.CompletedJobCount);

        foreach (var w in workloadList)
        {
            w.CompletedSharePercentage = totalCompletedShop > 0
                ? Math.Round((decimal)w.CompletedJobCount / totalCompletedShop * 100m, 1)
                : 0m;
        }

        return workloadList.OrderByDescending(w => w.CompletedJobCount)
                           .ThenByDescending(w => w.ActiveJobCount)
                           .ThenBy(w => w.FullName);
    }

    public Task<IEnumerable<PartDto>> GetLowStockReportAsync() => throw new NotImplementedException();

    public Task<IEnumerable<ServiceJobDto>> GetCompletedJobsReportAsync(DateTime periodStart, DateTime periodEnd) => throw new NotImplementedException();
}
