using Garij.Application.DTOs;
using Garij.Application.Interfaces;
using Garij.Domain.Entities;
using Garij.Domain.Enums;
using Garij.Infrastructure.Persistence;
using Garij.Infrastructure.Repositories;
using Garij.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Garij.Web.Controllers;

[Authorize(Roles = nameof(UserRole.Admin) + "," + nameof(UserRole.FrontDesk) + "," + nameof(UserRole.Mechanic))]
public class ServiceJobController : Controller
{
    private readonly IServiceJobService _serviceJobService;
    private readonly IVehicleRepository _vehicleRepository;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly GarijDbContext _context;

    public ServiceJobController(
        IServiceJobService serviceJobService,
        IVehicleRepository vehicleRepository,
        UserManager<IdentityUser> userManager,
        RoleManager<IdentityRole> roleManager,
        GarijDbContext context)
    {
        _serviceJobService = serviceJobService;
        _vehicleRepository = vehicleRepository;
        _userManager = userManager;
        _roleManager = roleManager;
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> Index(JobStatus? status, int? mechanicId, string? sortBy, string? search)
    {
        var jobs = await _serviceJobService.GetFilteredServiceJobsAsync(status, mechanicId, sortBy, search);

        var mechanics = _context.StaffUsers
            .Where(u => u.Role == UserRole.Mechanic)
            .OrderBy(u => u.FullName)
            .Select(u => new { u.Id, u.FullName })
            .ToList();

        ViewBag.Mechanics = new SelectList(mechanics, "Id", "FullName", mechanicId);
        ViewBag.SelectedStatus = status;
        ViewBag.SelectedMechanicId = mechanicId;
        ViewBag.SelectedSortBy = sortBy ?? "date_desc";
        ViewBag.SearchTerm = search;

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

    [Authorize(Roles = nameof(UserRole.Admin) + "," + nameof(UserRole.FrontDesk))]
    [HttpGet]
    public IActionResult AddEmployee()
    {
        return View(new CreateStaffUserViewModel());
    }

    [Authorize(Roles = nameof(UserRole.Admin) + "," + nameof(UserRole.FrontDesk))]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddEmployee(CreateStaffUserViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var normalizedEmail = model.Email.Trim().ToLowerInvariant();

        // Check if user already exists
        var existingUser = await _userManager.FindByEmailAsync(normalizedEmail);
        if (existingUser != null)
        {
            ModelState.AddModelError(nameof(model.Email), $"An account with email '{model.Email}' already exists.");
            return View(model);
        }

        var identityUser = new IdentityUser
        {
            UserName = normalizedEmail,
            Email = normalizedEmail,
            PhoneNumber = model.PhoneNumber,
            EmailConfirmed = true
        };

        var createResult = await _userManager.CreateAsync(identityUser, model.Password);
        if (!createResult.Succeeded)
        {
            foreach (var error in createResult.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
            return View(model);
        }

        // Ensure role exists in Identity
        var roleName = model.Role.ToString();
        if (!await _roleManager.RoleExistsAsync(roleName))
        {
            await _roleManager.CreateAsync(new IdentityRole(roleName));
        }

        await _userManager.AddToRoleAsync(identityUser, roleName);

        // Add to StaffUsers table
        _context.StaffUsers.Add(new User
        {
            IdentityUserId = identityUser.Id,
            FullName = model.FullName.Trim(),
            Email = normalizedEmail,
            PhoneNumber = model.PhoneNumber.Trim(),
            Role = model.Role,
            CreatedAt = DateTime.UtcNow
        });

        // Grant the workshop staff member license access under the workshop's ownership
        var licenseSlug = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        _context.ProjectPurchases.Add(new ProjectPurchase
        {
            LicenseKey = $"GRJ-LIC-STAFF-{licenseSlug}",
            IdentityUserId = identityUser.Id,
            BuyerName = model.FullName.Trim(),
            BuyerEmail = normalizedEmail,
            WorkshopName = "Workshop Staff Member",
            Amount = 0m,
            Currency = "USD",
            PaymentMethod = "GarageOwnerCreatedStaff",
            TransactionReference = $"TXN-STAFF-{licenseSlug}",
            PurchasedAt = DateTime.UtcNow,
            Status = LicenseStatus.Active,
            IsActive = true
        });

        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = $"Garage employee '{model.FullName}' was created successfully as {model.Role}! They can now log in with '{normalizedEmail}'.";
        return RedirectToAction(nameof(Index));
    }
}

