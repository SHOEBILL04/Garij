using Garij.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Garij.Web.Controllers;

[Authorize(Roles = nameof(UserRole.Admin) + "," + nameof(UserRole.FrontDesk))]
public class ReportController : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        return View();
    }

    [HttpGet]
    public IActionResult Revenue()
    {
        return View();
    }

    [HttpGet]
    public IActionResult MechanicWorkload()
    {
        return View();
    }

    [HttpGet]
    public IActionResult LowStock()
    {
        return View();
    }
}
