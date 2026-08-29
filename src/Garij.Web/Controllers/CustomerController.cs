using Garij.Application.DTOs;
using Garij.Application.Interfaces;
using Garij.Domain.Exceptions;
using Garij.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Garij.Web.Controllers;

[Authorize(Roles = nameof(UserRole.Admin) + "," + nameof(UserRole.FrontDesk))]
public class CustomerController : Controller
{
    private readonly ICustomerVehicleService _customerVehicleService;

    public CustomerController(ICustomerVehicleService customerVehicleService)
    {
        _customerVehicleService = customerVehicleService;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var customers = await _customerVehicleService.GetAllCustomersAsync();
        return View(customers);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var customer = await _customerVehicleService.GetCustomerByIdAsync(id);
        if (customer is null)
        {
            return NotFound();
        }

        ViewBag.Vehicles = await _customerVehicleService.GetVehiclesByCustomerAsync(id);
        return View(customer);
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CustomerDto customer)
    {
        if (!ModelState.IsValid)
        {
            return View(customer);
        }

        var saved = await _customerVehicleService.CreateCustomerAsync(customer);
        TempData["SuccessMessage"] = "Customer registered successfully.";
        return RedirectToAction(nameof(Details), new { id = saved.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var customer = await _customerVehicleService.GetCustomerByIdAsync(id);
        return customer is null ? NotFound() : View(customer);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, CustomerDto customer)
    {
        if (id != customer.Id)
        {
            return BadRequest();
        }

        if (!ModelState.IsValid)
        {
            return View(customer);
        }

        await _customerVehicleService.UpdateCustomerAsync(customer);
        TempData["SuccessMessage"] = "Customer updated successfully.";
        return RedirectToAction(nameof(Details), new { id = customer.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Delete(int id)
    {
        var customer = await _customerVehicleService.GetCustomerByIdAsync(id);
        return customer is null ? NotFound() : View(customer);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        try
        {
            await _customerVehicleService.DeleteCustomerAsync(id);
            TempData["SuccessMessage"] = "Customer deleted successfully.";
            return RedirectToAction(nameof(Index));
        }
        catch (BusinessRuleException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            var customer = await _customerVehicleService.GetCustomerByIdAsync(id);
            return customer is null ? NotFound() : View("Delete", customer);
        }
    }
}
