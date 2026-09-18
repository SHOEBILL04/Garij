namespace Garij.Web.Models;

public class ErrorViewModel
{
    public string? RequestId { get; set; }

    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

    /// <summary>The HTTP status code actually returned with this page (404, 400, 500, ...).</summary>
    public int StatusCode { get; set; } = StatusCodes.Status500InternalServerError;

    /// <summary>Short headline for the failure, e.g. "Page not found".</summary>
    public string Title { get; set; } = "Something went wrong";

    /// <summary>Human-readable explanation of what failed, shown to the user.</summary>
    public string Message { get; set; } = "An error occurred while processing your request.";

    /// <summary>The path the user originally asked for, before the error page was rendered.</summary>
    public string? RequestedPath { get; set; }

    public bool ShowRequestedPath => !string.IsNullOrWhiteSpace(RequestedPath);

    /// <summary>Default headline for a status code, used when the failure carries no specific title.</summary>
    public static string TitleFor(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "Bad request",
        StatusCodes.Status401Unauthorized => "Sign in required",
        StatusCodes.Status403Forbidden => "Access denied",
        StatusCodes.Status404NotFound => "Page not found",
        StatusCodes.Status405MethodNotAllowed => "Action not allowed",
        StatusCodes.Status409Conflict => "Conflicting request",
        _ => statusCode >= 500 ? "Something went wrong" : "Request could not be completed"
    };

    /// <summary>Default explanation for a status code, used when the failure carries no specific message.</summary>
    public static string MessageFor(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "The details sent with this request were not valid, so it could not be processed. Please go back, correct them and try again.",
        StatusCodes.Status401Unauthorized => "You need to sign in before you can view this page.",
        StatusCodes.Status403Forbidden => "Your account does not have permission to view this page.",
        StatusCodes.Status404NotFound => "We could not find the page or record you were looking for. It may have been removed, or the link may be incorrect.",
        StatusCodes.Status405MethodNotAllowed => "That action is not allowed on this page.",
        StatusCodes.Status409Conflict => "This change conflicts with the current state of the record. Reload the page and try again.",
        _ => statusCode >= 500
            ? "An unexpected error occurred on our side while processing your request. The problem has been logged for the team to investigate."
            : "Your request could not be completed."
    };
}
