using Garij.Application.DTOs;
using Garij.Application.Interfaces;
using Garij.Domain.Enums;
using Garij.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Garij.Web.Controllers;

[Authorize(Roles = nameof(UserRole.Admin) + "," + nameof(UserRole.FrontDesk))]
public class DashboardController : Controller
{
    private readonly IIntelligenceService? _intelligenceService;

    public DashboardController(IIntelligenceService? intelligenceService = null)
    {
        _intelligenceService = intelligenceService;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var dueVehicles = _intelligenceService != null
            ? await _intelligenceService.FlagVehiclesDueForServiceAsync()
            : Enumerable.Empty<VehicleMaintenancePredictionDto>();

        var model = new FrontDeskDashboardViewModel
        {
            DueVehicles = dueVehicles
        };

        return View(model);
    }
}
