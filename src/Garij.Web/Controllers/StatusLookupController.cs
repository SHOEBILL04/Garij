using Garij.Application.DTOs;
using Garij.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Garij.Web.Controllers;

[AllowAnonymous]
public class StatusLookupController : Controller
{
    private readonly ICustomerVehicleService _customerVehicleService;

    public StatusLookupController(ICustomerVehicleService customerVehicleService)
    {
        _customerVehicleService = customerVehicleService;
    }

    [HttpGet]
    public IActionResult Index()
    {
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> Result(string? plateNumber)
    {
        if (string.IsNullOrWhiteSpace(plateNumber))
        {
            return RedirectToAction(nameof(Index));
        }

        var vehicle = await _customerVehicleService.GetVehicleByLicensePlateAsync(plateNumber);
        if (vehicle is null)
        {
            ViewBag.SearchPlate = plateNumber;
            return View(new List<ServiceHistoryDto>());
        }

        ViewBag.Vehicle = vehicle;
        ViewBag.SearchPlate = plateNumber;
        var history = await _customerVehicleService.GetServiceHistoryByVehicleAsync(vehicle.Id);
        return View(history);
    }
}
