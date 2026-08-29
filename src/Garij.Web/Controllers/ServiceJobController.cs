using Garij.Application.DTOs;
using Garij.Application.Interfaces;
using Garij.Domain.Enums;
using Garij.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Garij.Web.Controllers;

[Authorize(Roles = nameof(UserRole.Admin) + "," + nameof(UserRole.FrontDesk) + "," + nameof(UserRole.Mechanic))]
public class ServiceJobController : Controller
{
    private readonly IServiceJobService _serviceJobService;
    private readonly IVehicleRepository _vehicleRepository;

    public ServiceJobController(
        IServiceJobService serviceJobService,
        IVehicleRepository vehicleRepository)
    {
        _serviceJobService = serviceJobService;
        _vehicleRepository = vehicleRepository;
    }

    [HttpGet]
    public async Task<IActionResult> Index(JobStatus? status)
    {
        IEnumerable<ServiceJobDto> jobs;
        if (status.HasValue)
        {
            jobs = await _serviceJobService.GetServiceJobsByStatusAsync(status.Value);
            ViewBag.SelectedStatus = status.Value;
        }
        else
        {
            jobs = await _serviceJobService.GetAllServiceJobsAsync();
            ViewBag.SelectedStatus = null;
        }

        return View(jobs);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var job = await _serviceJobService.GetServiceJobByIdAsync(id);
        if (job == null)
        {
            return NotFound();
        }

        return View(job);
    }

    [HttpGet]
    public async Task<IActionResult> Create(int? vehicleId)
    {
        await PopulateVehiclesDropDownList(vehicleId);
        var model = new ServiceJobDto
        {
            VehicleId = vehicleId ?? 0,
            JobType = JobType.RoutineService,
            Status = JobStatus.Requested
        };
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ServiceJobDto model)
    {
        if (model.VehicleId <= 0)
        {
            ModelState.AddModelError(nameof(model.VehicleId), "Please select a valid vehicle.");
        }

        if (!ModelState.IsValid)
        {
            await PopulateVehiclesDropDownList(model.VehicleId);
            return View(model);
        }

        try
        {
            var createdJob = await _serviceJobService.CreateServiceJobAsync(model);
            TempData["SuccessMessage"] = $"Service Job created successfully with Booking Reference: {createdJob.BookingReference}";
            return RedirectToAction(nameof(Details), new { id = createdJob.Id });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await PopulateVehiclesDropDownList(model.VehicleId);
            return View(model);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var job = await _serviceJobService.GetServiceJobByIdAsync(id);
        if (job == null)
        {
            return NotFound();
        }

        await PopulateVehiclesDropDownList(job.VehicleId);
        return View(job);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ServiceJobDto model)
    {
        if (id != model.Id)
        {
            return BadRequest();
        }

        if (!ModelState.IsValid)
        {
            await PopulateVehiclesDropDownList(model.VehicleId);
            return View(model);
        }

        try
        {
            await _serviceJobService.UpdateServiceJobAsync(model);
            TempData["SuccessMessage"] = "Service Job updated successfully.";
            return RedirectToAction(nameof(Details), new { id = model.Id });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await PopulateVehiclesDropDownList(model.VehicleId);
            return View(model);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Delete(int id)
    {
        var job = await _serviceJobService.GetServiceJobByIdAsync(id);
        if (job == null)
        {
            return NotFound();
        }

        return View(job);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        try
        {
            await _serviceJobService.DeleteServiceJobAsync(id);
            TempData["SuccessMessage"] = "Service job deleted successfully.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
            return RedirectToAction(nameof(Details), new { id });
        }
    }

    private async Task PopulateVehiclesDropDownList(object? selectedVehicle = null)
    {
        var vehicles = await _vehicleRepository.GetAllWithCustomersAsync();
        var vehicleList = vehicles.Select(v => new
        {
            v.Id,
            DisplayText = $"{v.LicensePlateNumber} - {v.Year} {v.Make} {v.Model} (Owner: {v.Customer?.FullName ?? "Unknown"})"
        }).OrderBy(v => v.DisplayText);

        ViewBag.Vehicles = new SelectList(vehicleList, "Id", "DisplayText", selectedVehicle);
    }
}
