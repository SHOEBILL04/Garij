using Garij.Application.Interfaces;
using Garij.Domain.Enums;
using Garij.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Garij.Web.Controllers;

[Authorize(Roles = nameof(UserRole.Admin) + "," + nameof(UserRole.FrontDesk))]
public class ReportController : Controller
{
    private readonly IReportingService _reportingService;

    public ReportController(IReportingService reportingService)
    {
        _reportingService = reportingService;
    }

    [HttpGet]
    public IActionResult Index()
    {
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> Revenue(DateTime? start, DateTime? end)
    {
        var range = ApplyDateRange(start ?? new DateTime(DateTime.UtcNow.Year, 1, 1), end ?? DateTime.UtcNow.Date);

        var report = await _reportingService.GetRevenueReportAsync(range.Start!.Value, range.End!.Value);
        return View(report);
    }

    [HttpGet]
    public async Task<IActionResult> PartConsumption(DateTime? start, DateTime? end)
    {
        var range = ApplyDateRange(start ?? new DateTime(DateTime.UtcNow.Year, 1, 1), end ?? DateTime.UtcNow.Date);

        var report = await _reportingService.GetPartsConsumptionReportAsync(range.Start!.Value, range.End!.Value);
        ViewBag.StartDate = range.Start;
        ViewBag.EndDate = range.End;
        return View(report);
    }

    [HttpGet]
    public async Task<IActionResult> MechanicWorkload(DateTime? start, DateTime? end)
    {
        // No defaults here: an empty date leaves that end of the range open.
        var range = ApplyDateRange(start, end);

        var report = await _reportingService.GetMechanicWorkloadReportAsync(range.Start, range.End);
        ViewBag.StartDate = range.Start;
        ViewBag.EndDate = range.End;
        return View(report);
    }

    // No date range: stock levels are a current snapshot.
    [HttpGet]
    public async Task<IActionResult> LowStock()
    {
        var report = await _reportingService.GetLowStockReportAsync();
        return View(report);
    }

    /// <summary>
    /// Resolves the range a date-driven report runs with and hands it to the view, where
    /// _DateRangeFilterPartial tells the user if a reversed range was swapped. A date that failed to
    /// parse (e.g. ?start=banana) has already been bound as null by this point, so it takes the report's
    /// default as before and is never reported as reversed on its own.
    /// </summary>
    private ReportDateRange ApplyDateRange(DateTime? start, DateTime? end)
    {
        var range = ReportDateRange.Resolve(start, end);
        ViewData[ReportDateRange.ViewDataKey] = range;
        return range;
    }
}
