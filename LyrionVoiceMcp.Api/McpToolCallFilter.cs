using System.Text.Json;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Api.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace LyrionVoiceMcp.Api;

public static class McpToolCallFilter
{
    public static McpRequestFilter<CallToolRequestParams, CallToolResult> Create() => next =>
        async (context, cancellationToken) =>
        {
            var services = context.Services
                ?? throw new InvalidOperationException("MCP request services are unavailable.");
            var history = services.GetRequiredService<IToolCallHistoryService>();
            var errors = services.GetRequiredService<IErrorLogService>();
            var logger = services.GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(McpToolCallFilter));
            var argumentsJson = SerializeForHistory(
                context.Params?.Arguments,
                "arguments",
                logger);
            var traceIdentifier = context.JsonRpcRequest.Id.ToString();
            var recording = await history.StartAsync(
                context.Params?.Name ?? "unknown",
                argumentsJson,
                traceIdentifier,
                cancellationToken);

            try
            {
                var result = await next(context, cancellationToken);
                if (recording is not null)
                {
                    await history.CompleteAsync(
                        recording.Id,
                        result.IsError == true ? ToolCallStatus.ToolError : ToolCallStatus.Succeeded,
                        recording.StartedAt,
                        SerializeForHistory(result, "result", logger),
                        result.IsError == true ? "Tool returned an error result." : null,
                        null,
                        CancellationToken.None);
                }

                return result;
            }
            catch (ArgumentException exception) when (
                string.Equals(exception.ParamName, "arguments", StringComparison.Ordinal))
            {
                var result = new CallToolResult
                {
                    Content = [new TextContentBlock { Text = exception.Message }],
                    IsError = true
                };
                if (recording is not null)
                {
                    await history.CompleteAsync(
                        recording.Id,
                        ToolCallStatus.ToolError,
                        recording.StartedAt,
                        SerializeForHistory(result, "result", logger),
                        exception.Message,
                        null,
                        CancellationToken.None);
                }

                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                if (recording is not null)
                {
                    await history.CompleteAsync(
                        recording.Id,
                        ToolCallStatus.Cancelled,
                        recording.StartedAt,
                        null,
                        "Tool call was cancelled.",
                        null,
                        CancellationToken.None);
                }

                throw;
            }
            catch (Exception exception)
            {
                var errorId = await errors.LogExceptionAsync(
                    exception,
                    new ErrorLogContext(
                        ErrorLogSources.Mcp,
                        ErrorLogAreas.McpToolCall,
                        traceIdentifier,
                        ContextJson: JsonSerializer.Serialize(new
                        {
                            toolName = context.Params?.Name,
                            toolCallId = recording?.Id
                        })),
                    CancellationToken.None);
                if (recording is not null)
                {
                    await history.CompleteAsync(
                        recording.Id,
                        ToolCallStatus.Failed,
                        recording.StartedAt,
                        null,
                        exception.Message,
                        errorId,
                        CancellationToken.None);
                }

                throw;
            }
        };

    private static string SerializeForHistory(
        object? value,
        string valueName,
        ILogger logger)
    {
        try
        {
            return JsonSerializer.Serialize(value, McpToolJson.Options);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to serialise MCP tool-call {ValueName} for durable history.",
                valueName);
            return JsonSerializer.Serialize(new
            {
                serialisationFailed = true,
                valueName,
                exceptionType = exception.GetType().FullName,
                message = exception.Message
            });
        }
    }
}
