using Garij.Application.DTOs;
using Garij.Application.Interfaces;
using Garij.Domain.Exceptions;
using Garij.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Garij.Web.Controllers;

[Authorize(Roles = nameof(UserRole.Admin) + "," + nameof(UserRole.FrontDesk))]
public class VehicleController : Controller
{
    private readonly ICustomerVehicleService _customerVehicleService;

    public VehicleController(ICustomerVehicleService customerVehicleService)
    {
        _customerVehicleService = customerVehicleService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? plateNumber)
    {
        if (!string.IsNullOrWhiteSpace(plateNumber))
        {
            var match = await _customerVehicleService.GetVehicleByLicensePlateAsync(plateNumber);
            ViewData["PlateNumber"] = plateNumber;
            return View(match is null ? Enumerable.Empty<VehicleDto>() : new[] { match });
        }

        var customers = await _customerVehicleService.GetAllCustomersAsync();
        var vehicles = new List<VehicleDto>();
        foreach (var customer in customers)
        {
            vehicles.AddRange(await _customerVehicleService.GetVehiclesByCustomerAsync(customer.Id));
        }

        return View(vehicles.OrderBy(v => v.LicensePlateNumber));
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var vehicle = await _customerVehicleService.GetVehicleByIdAsync(id);
        if (vehicle is null)
        {
            return NotFound();
        }

        ViewBag.ServiceHistory = await _customerVehicleService.GetServiceHistoryByVehicleAsync(id);
        return View(vehicle);
    }

    [HttpGet]
    public async Task<IActionResult> Create(int? customerId)
    {
        await PopulateCustomers(customerId);
        return View(new VehicleDto { CustomerId = customerId ?? 0, Year = DateTime.UtcNow.Year });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(VehicleDto vehicle)
    {
        if (!ModelState.IsValid)
        {
            await PopulateCustomers(vehicle.CustomerId);
            return View(vehicle);
        }

        try
        {
            var saved = await _customerVehicleService.AddVehicleAsync(vehicle);
            TempData["SuccessMessage"] = "Vehicle added successfully.";
            return RedirectToAction(nameof(Details), new { id = saved.Id });
        }
        catch (BusinessRuleException ex)
        {
            ModelState.AddModelError(nameof(VehicleDto.LicensePlateNumber), ex.Message);
            await PopulateCustomers(vehicle.CustomerId);
            return View(vehicle);
        }
        catch (NotFoundException ex)
        {
            ModelState.AddModelError(nameof(VehicleDto.CustomerId), ex.Message);
            await PopulateCustomers(vehicle.CustomerId);
            return View(vehicle);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var vehicle = await _customerVehicleService.GetVehicleByIdAsync(id);
        if (vehicle is null)
        {
            return NotFound();
        }

        await PopulateCustomers(vehicle.CustomerId);
        return View(vehicle);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, VehicleDto vehicle)
    {
        if (id != vehicle.Id)
        {
            return BadRequest();
        }

        if (!ModelState.IsValid)
        {
            await PopulateCustomers(vehicle.CustomerId);
            return View(vehicle);
        }

        try
        {
            await _customerVehicleService.UpdateVehicleAsync(vehicle);
            TempData["SuccessMessage"] = "Vehicle updated successfully.";
            return RedirectToAction(nameof(Details), new { id = vehicle.Id });
        }
        catch (BusinessRuleException ex)
        {
            ModelState.AddModelError(nameof(VehicleDto.LicensePlateNumber), ex.Message);
            await PopulateCustomers(vehicle.CustomerId);
            return View(vehicle);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Delete(int id)
    {
        var vehicle = await _customerVehicleService.GetVehicleByIdAsync(id);
        return vehicle is null ? NotFound() : View(vehicle);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        try
        {
            await _customerVehicleService.DeleteVehicleAsync(id);
            TempData["SuccessMessage"] = "Vehicle deleted successfully.";
            return RedirectToAction(nameof(Index));
        }
        catch (BusinessRuleException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            var vehicle = await _customerVehicleService.GetVehicleByIdAsync(id);
            return vehicle is null ? NotFound() : View("Delete", vehicle);
        }
    }

    private async Task PopulateCustomers(int? selectedCustomerId = null)
    {
        var customers = await _customerVehicleService.GetAllCustomersAsync();
        ViewBag.Customers = new SelectList(customers, nameof(CustomerDto.Id), nameof(CustomerDto.FullName), selectedCustomerId);
    }
}
