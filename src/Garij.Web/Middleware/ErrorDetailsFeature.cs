namespace Garij.Web.Middleware;

/// <summary>
/// Carries the details of a failure from whatever detected it (the global exception
/// middleware, or a controller that refuses a request) across to ErrorController, so the
/// shared error view can show a message about what actually went wrong instead of a
/// single generic one. Stored on HttpContext.Features because the error page is rendered
/// by re-executing the pipeline on the same HttpContext rather than by redirecting.
/// </summary>
public sealed class ErrorDetailsFeature
{
    public int StatusCode { get; init; } = StatusCodes.Status500InternalServerError;

    public string? Title { get; init; }

    public string? Message { get; init; }

    /// <summary>The path (with query string) the user originally requested.</summary>
    public string? OriginalPath { get; init; }
}
