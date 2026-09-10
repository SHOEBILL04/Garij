using Garij.Application.DTOs;
using Garij.Application.Interfaces;
using Garij.Domain.Exceptions;
using Garij.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Garij.Web.Controllers;

[Authorize]
public class PartsController : Controller
{
    private readonly IPartsInventoryService _partsInventoryService;
    private readonly IServiceJobService _serviceJobService;

    public PartsController(IPartsInventoryService partsInventoryService, IServiceJobService serviceJobService)
    {
        _partsInventoryService = partsInventoryService;
        _serviceJobService = serviceJobService;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var parts = await _partsInventoryService.GetAllPartsAsync();
        return View(parts);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var part = await _partsInventoryService.GetPartByIdAsync(id);
        if (part is null)
        {
            return NotFound();
        }

        return View(part);
    }

    [HttpGet]
    [Authorize(Roles = nameof(UserRole.Admin) + "," + nameof(UserRole.FrontDesk))]
    public IActionResult Create()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = nameof(UserRole.Admin) + "," + nameof(UserRole.FrontDesk))]
    public async Task<IActionResult> Create(PartDto part)
    {
        if (!ModelState.IsValid)
        {
            return View(part);
        }

        try
        {
            await _partsInventoryService.AddPartAsync(part);
        }
        catch (ValidationException ex)
        {
            foreach (var error in ex.Errors)
            {
                foreach (var message in error.Value)
                {
                    ModelState.AddModelError(error.Key, message);
                }
            }

            return View(part);
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize(Roles = nameof(UserRole.Admin) + "," + nameof(UserRole.FrontDesk))]
    public async Task<IActionResult> Edit(int id)
    {
        var part = await _partsInventoryService.GetPartByIdAsync(id);
        if (part is null)
        {
            return NotFound();
        }

        return View(part);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = nameof(UserRole.Admin) + "," + nameof(UserRole.FrontDesk))]
    public async Task<IActionResult> Edit(int id, PartDto part)
    {
        if (id != part.Id)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return View(part);
        }

        try
        {
            await _partsInventoryService.UpdatePartAsync(part);
        }
        catch (ValidationException ex)
        {
            foreach (var error in ex.Errors)
            {
                foreach (var message in error.Value)
                {
                    ModelState.AddModelError(error.Key, message);
                }
            }

            return View(part);
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = nameof(UserRole.Admin) + "," + nameof(UserRole.FrontDesk))]
    public async Task<IActionResult> AdjustStock(int id, int delta)
    {
        try
        {
            await _partsInventoryService.AdjustStockAsync(id, delta);
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> LogUsage(int serviceJobId)
    {
        var job = await _serviceJobService.GetServiceJobByIdAsync(serviceJobId);
        if (job is null)
        {
            return NotFound();
        }

        ViewBag.ServiceJob = job;
        await PopulatePartsDropDownList();

        return View(new JobPartUsedDto { ServiceJobId = serviceJobId, QuantityUsed = 1 });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LogUsage(JobPartUsedDto model)
    {
        if (model.PartId <= 0)
        {
            ModelState.AddModelError(nameof(model.PartId), "Please select a part.");
        }

        if (model.QuantityUsed <= 0)
        {
            ModelState.AddModelError(nameof(model.QuantityUsed), "Quantity must be at least 1.");
        }

        if (!ModelState.IsValid)
        {
            ViewBag.ServiceJob = await _serviceJobService.GetServiceJobByIdAsync(model.ServiceJobId);
            await PopulatePartsDropDownList(model.PartId);
            return View(model);
        }

        try
        {
            await _partsInventoryService.RecordPartUsageAsync(model);
            TempData["SuccessMessage"] = "Part usage logged.";
            return RedirectToAction("Details", "ServiceJob", new { id = model.ServiceJobId });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            ViewBag.ServiceJob = await _serviceJobService.GetServiceJobByIdAsync(model.ServiceJobId);
            await PopulatePartsDropDownList(model.PartId);
            return View(model);
        }
    }

    private async Task PopulatePartsDropDownList(object? selectedPart = null)
    {
        var parts = await _partsInventoryService.GetAllPartsAsync();
        var partList = parts.Select(p => new
        {
            p.Id,
            DisplayText = $"{p.Name} ({p.PartNumber}) - {p.UnitPrice:C} - {p.QuantityInStock} in stock"
        }).OrderBy(p => p.DisplayText);

        ViewBag.Parts = new SelectList(partList, "Id", "DisplayText", selectedPart);
    }
}
