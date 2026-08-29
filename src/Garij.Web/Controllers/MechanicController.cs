using Garij.Application.DTOs;
using Garij.Application.Interfaces;
using Garij.Domain.Enums;
using Garij.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Garij.Web.Controllers;

[Authorize(Roles = nameof(UserRole.Admin) + "," + nameof(UserRole.FrontDesk) + "," + nameof(UserRole.Mechanic))]
public class MechanicController : Controller
{
    private readonly IServiceJobService _serviceJobService;
    private readonly IUserRepository _userRepository;

    public MechanicController(
        IServiceJobService serviceJobService,
        IUserRepository userRepository)
    {
        _serviceJobService = serviceJobService;
        _userRepository = userRepository;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var users = await _userRepository.GetAllAsync();
        var mechanics = users.Where(u => u.Role == UserRole.Mechanic || u.Role == UserRole.Admin)
                             .OrderBy(u => u.FullName);

        return View(mechanics);
    }

    [HttpGet]
    public async Task<IActionResult> Assign(int serviceJobId)
    {
        var job = await _serviceJobService.GetServiceJobByIdAsync(serviceJobId);
        if (job == null)
        {
            return NotFound();
        }

        ViewBag.ServiceJob = job;
        await PopulateMechanicsDropDownList();

        var model = new MechanicAssignmentDto
        {
            ServiceJobId = serviceJobId,
            RoleInJob = RoleInJob.Lead
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Assign(MechanicAssignmentDto model)
    {
        if (model.UserId <= 0)
        {
            ModelState.AddModelError(nameof(model.UserId), "Please select a mechanic.");
        }

        if (!ModelState.IsValid)
        {
            var job = await _serviceJobService.GetServiceJobByIdAsync(model.ServiceJobId);
            ViewBag.ServiceJob = job;
            await PopulateMechanicsDropDownList(model.UserId);
            return View(model);
        }

        try
        {
            await _serviceJobService.AssignMechanicAsync(model.ServiceJobId, model.UserId, model.RoleInJob);
            TempData["SuccessMessage"] = "Mechanic assigned successfully.";
            return RedirectToAction("Details", "ServiceJob", new { id = model.ServiceJobId });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            var job = await _serviceJobService.GetServiceJobByIdAsync(model.ServiceJobId);
            ViewBag.ServiceJob = job;
            await PopulateMechanicsDropDownList(model.UserId);
            return View(model);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveAssignment(int id, int serviceJobId)
    {
        try
        {
            await _serviceJobService.RemoveMechanicAssignmentAsync(id);
            TempData["SuccessMessage"] = "Mechanic assignment removed.";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToAction("Details", "ServiceJob", new { id = serviceJobId });
    }

    private async Task PopulateMechanicsDropDownList(object? selectedMechanic = null)
    {
        var users = await _userRepository.GetAllAsync();
        var mechanics = users.Where(u => u.Role == UserRole.Mechanic || u.Role == UserRole.Admin)
                             .Select(u => new
                             {
                                 u.Id,
                                 DisplayText = $"{u.FullName} ({u.Role})"
                             })
                             .OrderBy(u => u.DisplayText);

        ViewBag.Mechanics = new SelectList(mechanics, "Id", "DisplayText", selectedMechanic);
    }
}
