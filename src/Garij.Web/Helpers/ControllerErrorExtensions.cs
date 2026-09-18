using Garij.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace Garij.Web.Helpers;

public static class ControllerErrorExtensions
{
    /// <summary>
    /// Refuses a request with the given status code and renders the shared error view, so the
    /// user gets the site layout and a readable explanation instead of the framework's bare
    /// status page. Use in place of a parameterless BadRequest()/NotFound() on actions that
    /// return HTML and have no form of their own to redisplay.
    /// </summary>
    public static IActionResult ErrorView(this Controller controller, int statusCode, string message, string? title = null)
    {
        var request = controller.HttpContext.Request;

        // Set on the response as well as the result: the status code is the point of this helper,
        // and it has to be the code the client actually receives.
        controller.Response.StatusCode = statusCode;

        return new ViewResult
        {
            ViewName = "Error",
            StatusCode = statusCode,
            ViewData = new Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary<ErrorViewModel>(
                controller.ViewData,
                new ErrorViewModel
                {
                    RequestId = controller.HttpContext.TraceIdentifier,
                    StatusCode = statusCode,
                    Title = title ?? ErrorViewModel.TitleFor(statusCode),
                    Message = message,
                    RequestedPath = request.Path + request.QueryString
                }),
            TempData = controller.TempData
        };
    }

    /// <summary>Shorthand for <see cref="ErrorView"/> with a 400 status code.</summary>
    public static IActionResult BadRequestView(this Controller controller, string message) =>
        controller.ErrorView(StatusCodes.Status400BadRequest, message);

    /// <summary>Shorthand for <see cref="ErrorView"/> with a 404 status code.</summary>
    public static IActionResult NotFoundView(this Controller controller, string message) =>
        controller.ErrorView(StatusCodes.Status404NotFound, message);
}
