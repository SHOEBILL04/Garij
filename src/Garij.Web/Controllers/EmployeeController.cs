using System.Security.Claims;
using Garij.Domain.Entities;
using Garij.Domain.Enums;
using Garij.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Garij.Web.Controllers;

[Authorize(Roles = nameof(UserRole.Admin) + "," + nameof(UserRole.FrontDesk) + "," + nameof(UserRole.Mechanic))]
public class EmployeeController : Controller
{
    private readonly GarijDbContext _context;

    public EmployeeController(GarijDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? search, UserRole? role)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var currentUserEmail = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;
        var currentStaff = await _context.StaffUsers.FirstOrDefaultAsync(s => s.IdentityUserId == currentUserId || (currentUserEmail != null && s.Email == currentUserEmail));
        var currentGarageId = currentStaff?.GarageId ?? "default-garij-master";

        var query = _context.StaffUsers
            .Where(u => (u.GarageId ?? "default-garij-master") == currentGarageId)
            .Include(u => u.MechanicAssignments)
                .ThenInclude(ma => ma.ServiceJob)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(u => u.FullName.ToLower().Contains(s) ||
                                     u.Email.ToLower().Contains(s) ||
                                     u.PhoneNumber.Contains(s));
        }

        if (role.HasValue)
        {
            query = query.Where(u => u.Role == role.Value);
        }

        var employees = await query.OrderBy(u => u.FullName).ToListAsync();

        var allUsers = await _context.StaffUsers
            .Where(u => (u.GarageId ?? "default-garij-master") == currentGarageId)
            .ToListAsync();
        ViewBag.TotalCount = allUsers.Count;
        ViewBag.AdminCount = allUsers.Count(u => u.Role == UserRole.Admin);
        ViewBag.FrontDeskCount = allUsers.Count(u => u.Role == UserRole.FrontDesk);
        ViewBag.MechanicCount = allUsers.Count(u => u.Role == UserRole.Mechanic);

        ViewBag.Search = search;
        ViewBag.SelectedRole = role;

        return View(employees);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var currentUserEmail = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;
        var currentStaff = await _context.StaffUsers.FirstOrDefaultAsync(s => s.IdentityUserId == currentUserId || (currentUserEmail != null && s.Email == currentUserEmail));
        var currentGarageId = currentStaff?.GarageId ?? "default-garij-master";

        var employee = await _context.StaffUsers
            .Include(u => u.MechanicAssignments)
                .ThenInclude(ma => ma.ServiceJob)
                    .ThenInclude(j => j.Vehicle)
            .Include(u => u.MechanicAssignments)
                .ThenInclude(ma => ma.ServiceJob)
                    .ThenInclude(j => j.Customer)
            .FirstOrDefaultAsync(u => u.Id == id && (u.GarageId ?? "default-garij-master") == currentGarageId);

        if (employee == null)
        {
            return NotFound();
        }

        return View(employee);
    }
}
