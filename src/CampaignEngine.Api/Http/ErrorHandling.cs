using System.Text.Json;
using CampaignEngine.Core.Evaluation;
using CampaignEngine.Infrastructure.Platform;
using CampaignEngine.Infrastructure.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace CampaignEngine.Api.Http;

/// <summary>Turns known exceptions into RFC 9457 problem details, so every client sees one error format.</summary>
public sealed class ErrorHandler(IProblemDetailsService problems, ILogger<ErrorHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var problem = exception switch
        {
            ValidationException ex => Validation(ex.Errors),
            CartValidationException ex => Validation(ex.Errors),
            NotFoundException ex => Problem(StatusCodes.Status404NotFound, "Not found", ex.Message),
            AuthenticationFailedException ex => Problem(StatusCodes.Status401Unauthorized, "Authentication failed", ex.Message),
            ForbiddenException ex => Problem(StatusCodes.Status403Forbidden, "Forbidden", ex.Message),
            TenantRequiredException ex => Problem(StatusCodes.Status403Forbidden, "No organization", ex.Message),
            ActivationBlockedException ex => WithConflicts(Problem(StatusCodes.Status409Conflict, "Conflicting campaigns", ex.Message), ex),
            ConflictException ex => Problem(StatusCodes.Status409Conflict, "Conflict", ex.Message),
            BadHttpRequestException { InnerException: JsonException json } => Problem(StatusCodes.Status400BadRequest, "Invalid JSON", json.Message),
            JsonException ex => Problem(StatusCodes.Status400BadRequest, "Invalid JSON", ex.Message),
            BadHttpRequestException ex => Problem(ex.StatusCode, "Bad request", ex.Message),
            _ => null,
        };

        if (problem is null)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
            return false;
        }

        httpContext.Response.StatusCode = problem.Status!.Value;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    private static ProblemDetails Problem(int status, string title, string detail) =>
        new() { Status = status, Title = title, Detail = detail };

    private static ProblemDetails Validation(IReadOnlyList<string> errors)
    {
        var problem = Problem(StatusCodes.Status400BadRequest, "Validation failed", string.Join(" ", errors));
        problem.Extensions["errors"] = errors;
        return problem;
    }

    private static ProblemDetails WithConflicts(ProblemDetails problem, ActivationBlockedException ex)
    {
        problem.Extensions["conflicts"] = ex.Conflicts;
        return problem;
    }
}
