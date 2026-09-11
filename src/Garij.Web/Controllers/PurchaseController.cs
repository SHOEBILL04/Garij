using System.Security.Claims;
using Garij.Application.Configuration;
using Garij.Application.DTOs;
using Garij.Application.Interfaces;
using Garij.Domain.Entities;
using Garij.Domain.Enums;
using Garij.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Garij.Web.Controllers;

public class PurchaseController : Controller
{
    private readonly IProjectPurchaseService _purchaseService;
    private readonly LicenseSettings _settings;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly SignInManager<IdentityUser> _signInManager;
    private readonly GarijDbContext _context;

    public PurchaseController(
        IProjectPurchaseService purchaseService,
        IOptions<LicenseSettings> settings,
        UserManager<IdentityUser> userManager,
        SignInManager<IdentityUser> signInManager,
        GarijDbContext context)
    {
        _purchaseService = purchaseService;
        _settings = settings.Value;
        _userManager = userManager;
        _signInManager = signInManager;
        _context = context;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Index(string? returnUrl = null, bool requireLicense = false)
    {
        ViewBag.ReturnUrl = returnUrl;
        ViewBag.RequireLicense = requireLicense;
        ViewBag.Price = _settings.Price;
        ViewBag.Currency = _settings.Currency;
        ViewBag.ProductName = _settings.ProductName;

        var model = new CreateProjectPurchaseDto();

        if (User.Identity != null && User.Identity.IsAuthenticated)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var email = User.FindFirstValue(ClaimTypes.Email);

            var activeLicense = await _purchaseService.GetActiveLicenseAsync(userId, email);
            if (activeLicense != null)
            {
                // User already has an active license
                return RedirectToAction(nameof(Status));
            }

            model.BuyerEmail = email ?? string.Empty;
            var staffUser = _context.StaffUsers.FirstOrDefault(u => u.IdentityUserId == userId);
            if (staffUser != null)
            {
                model.BuyerName = staffUser.FullName;
            }
        }

        return View(model);
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Checkout(CreateProjectPurchaseDto model, string? returnUrl = null)
    {
        ViewBag.ReturnUrl = returnUrl;
        ViewBag.Price = _settings.Price;
        ViewBag.Currency = _settings.Currency;
        ViewBag.ProductName = _settings.ProductName;

        if (!ModelState.IsValid)
        {
            return View("Index", model);
        }

        string? currentUserId = null;

        var chosenRole = Enum.TryParse<UserRole>(model.AccountRole, true, out var r) ? r : UserRole.Admin;

        string? garageId = null;

        // 1. If user is currently signed in
        if (User.Identity != null && User.Identity.IsAuthenticated)
        {
            currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(currentUserId))
            {
                var authUser = await _userManager.FindByIdAsync(currentUserId);
                if (authUser != null)
                {
                    var authRoles = await _userManager.GetRolesAsync(authUser);
                    if (!authRoles.Contains(chosenRole.ToString()))
                    {
                        await _userManager.AddToRoleAsync(authUser, chosenRole.ToString());
                        await _signInManager.RefreshSignInAsync(authUser);
                    }
                    var staffMember = _context.StaffUsers.FirstOrDefault(s => s.IdentityUserId == authUser.Id);
                    if (staffMember != null)
                    {
                        staffMember.Role = chosenRole;
                        if (string.IsNullOrEmpty(staffMember.GarageId))
                        {
                            staffMember.GarageId = $"GRG-{authUser.Id[..8].ToUpperInvariant()}";
                        }
                        garageId = staffMember.GarageId;
                        await _context.SaveChangesAsync();
                    }
                    else
                    {
                        garageId = $"GRG-{authUser.Id[..8].ToUpperInvariant()}";
                    }
                }
            }
        }
        else
        {
            // 2. Unauthenticated user purchasing from landing page: create or link account
            var normalizedEmail = model.BuyerEmail.Trim().ToLowerInvariant();
            var existingUser = await _userManager.FindByEmailAsync(normalizedEmail);

            if (existingUser != null)
            {
                currentUserId = existingUser.Id;
                // Update role to chosen role if not assigned
                var existingRoles = await _userManager.GetRolesAsync(existingUser);
                if (!existingRoles.Contains(chosenRole.ToString()))
                {
                    await _userManager.AddToRoleAsync(existingUser, chosenRole.ToString());
                }
                var staffMember = _context.StaffUsers.FirstOrDefault(s => s.IdentityUserId == existingUser.Id || s.Email == normalizedEmail);
                if (staffMember != null)
                {
                    staffMember.Role = chosenRole;
                    if (string.IsNullOrEmpty(staffMember.GarageId))
                    {
                        staffMember.GarageId = $"GRG-{existingUser.Id[..8].ToUpperInvariant()}";
                    }
                    garageId = staffMember.GarageId;
                    await _context.SaveChangesAsync();
                }
                else
                {
                    garageId = $"GRG-{existingUser.Id[..8].ToUpperInvariant()}";
                }

                // Sign in if password is valid
                if (!string.IsNullOrWhiteSpace(model.Password))
                {
                    await _signInManager.PasswordSignInAsync(existingUser.UserName!, model.Password, isPersistent: false, lockoutOnFailure: false);
                }
            }
            else
            {
                // Create a new user account for the buyer with their selected role
                var password = string.IsNullOrWhiteSpace(model.Password) ? "Garij@2026!" : model.Password;
                var newUser = new IdentityUser
                {
                    UserName = normalizedEmail,
                    Email = normalizedEmail,
                    EmailConfirmed = true
                };

                var createResult = await _userManager.CreateAsync(newUser, password);
                if (createResult.Succeeded)
                {
                    await _userManager.AddToRoleAsync(newUser, chosenRole.ToString());

                    garageId = $"GRG-{newUser.Id[..8].ToUpperInvariant()}";

                    _context.StaffUsers.Add(new User
                    {
                        IdentityUserId = newUser.Id,
                        FullName = model.BuyerName,
                        Email = normalizedEmail,
                        Role = chosenRole,
                        GarageId = garageId,
                        CreatedAt = DateTime.UtcNow
                    });
                    await _context.SaveChangesAsync();

                    await _signInManager.SignInAsync(newUser, isPersistent: false);
                    currentUserId = newUser.Id;
                }
            }
        }

        model.GarageId = garageId;
        var result = await _purchaseService.ProcessPurchaseAsync(model, currentUserId);
        if (result.Success && result.Purchase != null)
        {
            TempData["SuccessMessage"] = result.Message ?? "Payment successful! Your lifetime license has been activated.";
            return RedirectToAction(nameof(Success), new { id = result.Purchase.Id, returnUrl });
        }

        ModelState.AddModelError(string.Empty, result.Message ?? "Payment processing encountered an error. Please try again.");
        return View("Index", model);
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Success(int id, string? returnUrl = null)
    {
        var purchase = await _purchaseService.GetPurchaseByIdAsync(id);
        if (purchase is null)
        {
            return RedirectToAction(nameof(Index));
        }

        ViewBag.ReturnUrl = returnUrl;
        return View(purchase);
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Status()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = User.FindFirstValue(ClaimTypes.Email);

        var license = await _purchaseService.GetActiveLicenseAsync(userId, email);
        return View(license);
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Activate(string licenseKey, string? returnUrl = null)
    {
        if (string.IsNullOrWhiteSpace(licenseKey))
        {
            TempData["ErrorMessage"] = "Please enter a valid license key.";
            return RedirectToAction(nameof(Index));
        }

        string? userId = null;
        string? email = null;

        if (User.Identity != null && User.Identity.IsAuthenticated)
        {
            userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            email = User.FindFirstValue(ClaimTypes.Email);
        }

        var result = await _purchaseService.ActivateLicenseKeyAsync(licenseKey, userId, email);
        if (result.Success)
        {
            TempData["SuccessMessage"] = "License key activated successfully! Your copy of Garij is now unlocked.";
            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }
            return RedirectToAction("Index", "Dashboard");
        }

        TempData["ErrorMessage"] = result.Message ?? "Invalid or expired license key.";
        return RedirectToAction(nameof(Index));
    }
}
