using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Garij.Web.Controllers;

[AllowAnonymous]
public class StatusLookupController : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        return Redirect("/#status-tracker");
    }

    [HttpGet]
    public IActionResult Result(string? query, string? plateNumber, string? bookingReference)
    {
        var lookupValue = FirstProvided(query, bookingReference, plateNumber);
        if (string.IsNullOrWhiteSpace(lookupValue))
        {
            return Redirect("/#status-tracker");
        }

        return Redirect($"/?query={Uri.EscapeDataString(lookupValue)}#status-tracker");
    }

    private static string FirstProvided(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}
