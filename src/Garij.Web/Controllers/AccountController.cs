using Garij.Domain.Entities;
using Garij.Domain.Enums;
using Garij.Infrastructure.Persistence;
using Garij.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Garij.Web.Controllers;

[AllowAnonymous]
public class AccountController : Controller
{
    private readonly SignInManager<IdentityUser> _signInManager;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly GarijDbContext _context;

    public AccountController(
        SignInManager<IdentityUser> signInManager,
        UserManager<IdentityUser> userManager,
        GarijDbContext context)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _context = context;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _signInManager.PasswordSignInAsync(model.Email, model.Password, model.RememberMe, lockoutOnFailure: false);
        if (result.Succeeded)
        {
            if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            {
                return Redirect(model.ReturnUrl);
            }

            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user != null)
            {
                var roles = await _userManager.GetRolesAsync(user);
                if (roles.Contains("Admin"))
                {
                    return RedirectToAction("Index", "Admin");
                }
                if (roles.Contains("Mechanic"))
                {
                    return RedirectToAction("Index", "Mechanic");
                }
            }

            return RedirectToAction("Index", "Dashboard");
        }

        ModelState.AddModelError(string.Empty, "Invalid login credentials. Please verify your email and password.");
        return View(model);
    }

    [HttpGet]
    public IActionResult Register()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = new IdentityUser { UserName = model.Email, Email = model.Email, EmailConfirmed = true };
        var result = await _userManager.CreateAsync(user, model.Password);
        if (result.Succeeded)
        {
            // Public self-registration is never trusted with role selection - every
            // self-registered account starts as FrontDesk. Promoting to Admin or
            // Mechanic is an administrative action, not something the registrant chooses.
            const UserRole selfRegisteredRole = UserRole.FrontDesk;

            await _userManager.AddToRoleAsync(user, selfRegisteredRole.ToString());

            _context.StaffUsers.Add(new User
            {
                IdentityUserId = user.Id,
                FullName = model.FullName,
                Email = model.Email,
                PhoneNumber = model.PhoneNumber,
                Role = selfRegisteredRole,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            await _signInManager.SignInAsync(user, isPersistent: false);
            return RedirectToAction("Index", "Dashboard");
        }

        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }

        return View(model);
    }

    [HttpGet]
    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction("Login", "Account");
    }

    [HttpGet]
    public IActionResult AccessDenied()
    {
        return View();
    }
}
