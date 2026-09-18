using System.Net;
using System.Text.Json;
using Garij.Domain.Exceptions;

namespace Garij.Web.Middleware;

public class GlobalExceptionMiddleware
{
    /// <summary>Path of the MVC endpoint that renders the shared error view.</summary>
    private const string ErrorPath = "/Error";

    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unhandled exception occurred during request processing. Path: {Path}", context.Request.Path);
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        if (context.Response.HasStarted)
        {
            // The response is already on the wire; nothing can be changed about it now.
            _logger.LogWarning("The response had already started, so the error page could not be rendered for {Path}.", context.Request.Path);
            return;
        }

        var isApiOrJsonRequest = context.Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
                                 context.Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);

        var statusCode = exception switch
        {
            NotFoundException => HttpStatusCode.NotFound,
            ValidationException => HttpStatusCode.BadRequest,
            BusinessRuleException => HttpStatusCode.BadRequest,
            _ => HttpStatusCode.InternalServerError
        };

        if (isApiOrJsonRequest)
        {
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)statusCode;

            object responsePayload = exception switch
            {
                ValidationException valEx => new
                {
                    success = false,
                    message = valEx.Message,
                    errors = valEx.Errors,
                    statusCode = (int)statusCode
                },
                BusinessRuleException bizEx => new
                {
                    success = false,
                    message = bizEx.Message,
                    ruleCode = bizEx.RuleCode,
                    statusCode = (int)statusCode
                },
                NotFoundException nfEx => new
                {
                    success = false,
                    message = nfEx.Message,
                    entity = nfEx.EntityName,
                    key = nfEx.Key,
                    statusCode = (int)statusCode
                },
                _ => new
                {
                    success = false,
                    message = "An unexpected server error occurred.",
                    statusCode = (int)statusCode
                }
            };

            var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            await context.Response.WriteAsync(JsonSerializer.Serialize(responsePayload, jsonOptions));
            return;
        }

        await RenderErrorViewAsync(context, (int)statusCode, MessageFor(exception, (int)statusCode));
    }

    /// <summary>
    /// Only exceptions the application raises deliberately describe a condition the user can
    /// act on; anything else may carry internal detail, so it is replaced by the generic
    /// message for its status code.
    /// </summary>
    private static string MessageFor(Exception exception, int statusCode) => exception switch
    {
        NotFoundException or ValidationException or BusinessRuleException => exception.Message,
        _ => Models.ErrorViewModel.MessageFor(statusCode)
    };

    /// <summary>
    /// Renders the shared error view by re-running the pipeline against the /Error endpoint on
    /// the same request, so the calculated status code is the one the client actually receives.
    /// A redirect here would replace the failure with a fresh 200 response and lose both the
    /// status code and the context of what failed.
    /// </summary>
    private async Task RenderErrorViewAsync(HttpContext context, int statusCode, string message)
    {
        var originalPath = context.Request.Path;
        var originalQueryString = context.Request.QueryString;
        var originalMethod = context.Request.Method;
        var originalEndpoint = context.GetEndpoint();

        context.Features.Set(new ErrorDetailsFeature
        {
            StatusCode = statusCode,
            Title = Models.ErrorViewModel.TitleFor(statusCode),
            Message = message,
            OriginalPath = originalPath + originalQueryString
        });

        try
        {
            context.Response.Clear();
            context.Response.StatusCode = statusCode;

            // Re-route this request at the error endpoint: the endpoint and route values already
            // resolved for the failed request have to be cleared or routing will not run again.
            context.Request.Path = ErrorPath;
            context.Request.QueryString = QueryString.Empty;
            context.Request.Method = HttpMethods.Get;
            context.SetEndpoint(null);
            context.Request.RouteValues.Clear();

            await _next(context);
        }
        catch (Exception renderException)
        {
            // The error page itself failed (a broken layout, an unreachable database). Fall back to
            // a plain response rather than letting a second exception escape the pipeline.
            _logger.LogError(renderException, "Failed to render the error page for {Path}.", originalPath);

            if (!context.Response.HasStarted)
            {
                context.Response.Clear();
                context.Response.StatusCode = statusCode;
                context.Response.ContentType = "text/plain; charset=utf-8";
                await context.Response.WriteAsync(message);
            }
        }
        finally
        {
            context.Request.Path = originalPath;
            context.Request.QueryString = originalQueryString;
            context.Request.Method = originalMethod;
            context.SetEndpoint(originalEndpoint);
            context.Features.Set<ErrorDetailsFeature>(null);
        }
    }
}

public static class GlobalExceptionMiddlewareExtensions
{
    public static IApplicationBuilder UseGlobalExceptionMiddleware(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<GlobalExceptionMiddleware>();
    }
}
