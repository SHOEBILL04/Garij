using Garij.Application.Interfaces;
using Garij.Domain.Enums;
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
        var startDate = start ?? new DateTime(DateTime.UtcNow.Year, 1, 1);
        var endDate = end ?? DateTime.UtcNow.Date;

        if (startDate > endDate)
        {
            var temp = startDate;
            startDate = endDate;
            endDate = temp;
        }

        var report = await _reportingService.GetRevenueReportAsync(startDate, endDate);
        return View(report);
    }

    [HttpGet]
    public async Task<IActionResult> PartConsumption(DateTime? start, DateTime? end)
    {
        var startDate = start ?? new DateTime(DateTime.UtcNow.Year, 1, 1);
        var endDate = end ?? DateTime.UtcNow.Date;

        if (startDate > endDate)
        {
            var temp = startDate;
            startDate = endDate;
            endDate = temp;
        }

        var report = await _reportingService.GetPartsConsumptionReportAsync(startDate, endDate);
        ViewBag.StartDate = startDate;
        ViewBag.EndDate = endDate;
        return View(report);
    }

    [HttpGet]
    public async Task<IActionResult> MechanicWorkload(DateTime? start, DateTime? end)
    {
        var report = await _reportingService.GetMechanicWorkloadReportAsync(start, end);
        ViewBag.StartDate = start;
        ViewBag.EndDate = end;
        return View(report);
    }

    [HttpGet]
    public IActionResult LowStock()
    {
        return View();
    }
}
