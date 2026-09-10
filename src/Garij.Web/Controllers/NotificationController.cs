using Garij.Application.Interfaces;
using Garij.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Garij.Web.Controllers;

[Authorize(Roles = nameof(UserRole.Admin) + "," + nameof(UserRole.FrontDesk))]
public class NotificationController : Controller
{
    private readonly INotificationService _notificationService;

    public NotificationController(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var notifications = await _notificationService.GetPendingNotificationsAsync();
        return View(notifications);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var notification = await _notificationService.GetNotificationByIdAsync(id);
        if (notification is null)
        {
            return NotFound();
        }

        return View(notification);
    }

    [HttpGet]
    public async Task<IActionResult> Respond(int id)
    {
        var notification = await _notificationService.GetNotificationByIdAsync(id);
        if (notification is null)
        {
            return NotFound();
        }

        return View(notification);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Respond(int id, NotificationStatus status)
    {
        if (status != NotificationStatus.Approved && status != NotificationStatus.Rejected)
        {
            return BadRequest("Status must be either Approved or Rejected.");
        }

        await _notificationService.RespondToNotificationAsync(id, status);
        return RedirectToAction(nameof(Index));
    }
}
