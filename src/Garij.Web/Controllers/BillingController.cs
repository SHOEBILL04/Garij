using Garij.Application.DTOs;
using Garij.Application.Interfaces;
using Garij.Domain.Enums;
using Garij.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Garij.Web.Controllers;

[Authorize(Roles = nameof(UserRole.Admin) + "," + nameof(UserRole.FrontDesk))]
public class BillingController : Controller
{
    private readonly IBillingService _billingService;

    public BillingController(IBillingService billingService)
    {
        _billingService = billingService;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var invoices = await _billingService.GetAllInvoicesAsync();
        return View(invoices);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var invoice = await _billingService.GetInvoiceByIdAsync(id);
        if (invoice is null)
        {
            return NotFound();
        }

        return View(invoice);
    }

    [HttpGet]
    public IActionResult Create(int serviceJobId)
    {
        if (serviceJobId <= 0)
        {
            return BadRequest();
        }

        return View(serviceJobId);
    }

    [HttpPost, ActionName("Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateConfirmed(int serviceJobId)
    {
        try
        {
            var invoice = await _billingService.GenerateInvoiceAsync(serviceJobId);
            TempData["SuccessMessage"] = $"Invoice {invoice.InvoiceNumber} generated and job marked Completed.";
            return RedirectToAction(nameof(Details), new { id = invoice.Id });
        }
        catch (Exception ex) when (ex is NotFoundException or BusinessRuleException)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View("Create", serviceJobId);
        }
    }

    [HttpGet]
    public async Task<IActionResult> RecordPayment(int invoiceId)
    {
        var invoice = await _billingService.GetInvoiceByIdAsync(invoiceId);
        if (invoice is null)
        {
            return NotFound();
        }

        return View(invoice);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RecordPayment(int invoiceId, PaymentTransactionDto payment)
    {
        payment.InvoiceId = invoiceId;

        try
        {
            await _billingService.RecordPaymentAsync(payment);
            TempData["SuccessMessage"] = "Payment recorded successfully.";
            return RedirectToAction(nameof(Details), new { id = invoiceId });
        }
        catch (Exception ex) when (ex is NotFoundException or BusinessRuleException)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            var invoice = await _billingService.GetInvoiceByIdAsync(invoiceId);
            if (invoice is null)
            {
                return NotFound();
            }

            return View(invoice);
        }
    }
}
