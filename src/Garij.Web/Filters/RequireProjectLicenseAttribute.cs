using System.Security.Claims;
using Garij.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Garij.Web.Filters;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public class RequireProjectLicenseAttribute : ActionFilterAttribute
{
    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        // 1. Bypass if the action or controller allows anonymous access
        var endpoint = context.HttpContext.GetEndpoint();
        if (endpoint?.Metadata?.GetMetadata<IAllowAnonymous>() != null)
        {
            await next();
            return;
        }

        var controllerName = context.RouteData.Values["controller"]?.ToString() ?? string.Empty;

        // 2. Explicitly bypass account, purchase, home, and error controllers
        if (controllerName.Equals("Purchase", StringComparison.OrdinalIgnoreCase) ||
            controllerName.Equals("Account", StringComparison.OrdinalIgnoreCase) ||
            controllerName.Equals("Home", StringComparison.OrdinalIgnoreCase) ||
            controllerName.Equals("Error", StringComparison.OrdinalIgnoreCase))
        {
            await next();
            return;
        }

        var user = context.HttpContext.User;

        // 3. If the user is not authenticated yet, let ASP.NET Core Identity [Authorize] handle redirection to Login
        if (user?.Identity == null || !user.Identity.IsAuthenticated)
        {
            await next();
            return;
        }

        // 4. Admin role has access (and has seeded lifetime license)
        if (user.IsInRole("Admin"))
        {
            await next();
            return;
        }

        // 5. Check if user holds an active lifetime license
        var purchaseService = context.HttpContext.RequestServices.GetRequiredService<IProjectPurchaseService>();
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = user.FindFirstValue(ClaimTypes.Email);

        var hasLicense = await purchaseService.HasActiveLicenseAsync(userId, email);
        if (!hasLicense)
        {
            // Check if this is an AJAX request
            var isAjax = string.Equals(context.HttpContext.Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase)
                         || context.HttpContext.Request.Headers.Accept.Any(h => h != null && h.Contains("application/json"));

            if (isAjax)
            {
                context.Result = new JsonResult(new
                {
                    success = false,
                    requiresLicense = true,
                    message = "A valid lifetime license is required to access workshop management tools.",
                    redirectUrl = "/Purchase"
                })
                {
                    StatusCode = StatusCodes.Status402PaymentRequired
                };
                return;
            }

            if (context.Controller is Controller mvcController)
            {
                mvcController.TempData["LicenseAlert"] = "A lifetime license is required to access the workshop system. Complete your one-time purchase below to unlock full access.";
            }

            context.Result = new RedirectToActionResult("Index", "Purchase", new { returnUrl = context.HttpContext.Request.Path });
            return;
        }

        await next();
    }
}
