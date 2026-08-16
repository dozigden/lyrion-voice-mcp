using System.Text.Json;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Contracts;

namespace LyrionVoiceMcp.Api;

public sealed class ApiExceptionLoggingMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        IErrorLogService errors,
        ILogger<ApiExceptionLoggingMiddleware> logger)
    {
        if (!context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        try
        {
            await next(context);
        }
        catch (Exception exception) when (!IsCancellation(exception, context.RequestAborted))
        {
            logger.LogError(exception, "Unhandled API request failure for {Method} {Path}.",
                context.Request.Method, context.Request.Path);
            var errorId = await errors.LogExceptionAsync(
                exception,
                new ErrorLogContext(
                    ErrorLogSources.Backend,
                    ErrorLogAreas.ApiRequest,
                    context.TraceIdentifier,
                    context.Request.Method,
                    context.Request.Path.Value,
                    ContextJson: JsonSerializer.Serialize(new { context.Request.QueryString })),
                CancellationToken.None);
            if (context.Response.HasStarted)
            {
                throw;
            }

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new ApiErrorResponse(
                errorId is null
                    ? "The request failed unexpectedly."
                    : $"The request failed unexpectedly. Error reference: {errorId}."));
        }
    }

    private static bool IsCancellation(Exception exception, CancellationToken requestAborted) =>
        exception is OperationCanceledException && requestAborted.IsCancellationRequested;
}
