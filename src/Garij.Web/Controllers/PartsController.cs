using Garij.Application.DTOs;
using Garij.Application.Interfaces;
using Garij.Domain.Exceptions;
using Garij.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Garij.Web.Controllers;

[Authorize(Roles = nameof(UserRole.Admin) + "," + nameof(UserRole.FrontDesk) + "," + nameof(UserRole.Mechanic))]
public class PartsController : Controller
{
    private readonly IPartsInventoryService _partsInventoryService;

    public PartsController(IPartsInventoryService partsInventoryService)
    {
        _partsInventoryService = partsInventoryService;
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
    public IActionResult Create()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
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

    [HttpGet]
    public async Task<IActionResult> Delete(int id)
    {
        var part = await _partsInventoryService.GetPartByIdAsync(id);
        if (part is null)
        {
            return NotFound();
        }

        return View(part);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        await _partsInventoryService.DeletePartAsync(id);
        return RedirectToAction(nameof(Index));
    }
}
