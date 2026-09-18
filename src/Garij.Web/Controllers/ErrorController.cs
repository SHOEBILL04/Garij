using System.Diagnostics;
using Garij.Web.Middleware;
using Garij.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Garij.Web.Controllers;

[AllowAnonymous]
public class ErrorController : Controller
{
    /// <summary>
    /// Renders the shared error view for a failed request. Reached by re-execution (from the
    /// global exception middleware or from UseStatusCodePagesWithReExecute) rather than by a
    /// redirect, so the status code set on the response is preserved and returned to the client.
    /// </summary>
    [Route("/Error")]
    [Route("/Error/{statusCode:int}")]
    public IActionResult Index(int? statusCode = null)
    {
        // The exception middleware describes the failure it caught; status code re-execution only
        // carries the original code and path. Either may be absent for a direct hit on /Error.
        var details = HttpContext.Features.Get<ErrorDetailsFeature>();
        var reExecute = HttpContext.Features.Get<IStatusCodeReExecuteFeature>();

        var code = Normalise(details?.StatusCode ?? statusCode ?? Response.StatusCode);
        var originalPath = details?.OriginalPath
            ?? (reExecute is null ? null : reExecute.OriginalPathBase + reExecute.OriginalPath + reExecute.OriginalQueryString);

        var model = new ErrorViewModel
        {
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
            StatusCode = code,
            Title = details?.Title ?? ErrorViewModel.TitleFor(code),
            Message = details?.Message ?? ErrorViewModel.MessageFor(code),
            RequestedPath = string.IsNullOrWhiteSpace(originalPath) ? null : originalPath
        };

        // Set explicitly so the code survives however this action was reached, including a direct
        // request to /Error/404, where the response would otherwise still be a 200.
        Response.StatusCode = code;
        return View("Error", model);
    }

    /// <summary>Keeps an out-of-range or success code from being reported as an error code.</summary>
    private static int Normalise(int statusCode) =>
        statusCode is >= 400 and <= 599 ? statusCode : StatusCodes.Status500InternalServerError;
}
