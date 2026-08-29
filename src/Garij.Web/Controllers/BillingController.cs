using Garij.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Garij.Web.Controllers;

[Authorize(Roles = nameof(UserRole.Admin) + "," + nameof(UserRole.FrontDesk))]
public class BillingController : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        return View();
    }

    [HttpGet]
    public IActionResult Details(int id)
    {
        return View();
    }

    [HttpGet]
    public IActionResult Create(int serviceJobId)
    {
        return View();
    }

    [HttpGet]
    public IActionResult RecordPayment(int invoiceId)
    {
        return View();
    }
}
