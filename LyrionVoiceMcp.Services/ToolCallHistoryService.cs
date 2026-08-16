using System.Text.Json;
using LyrionVoiceMcp.Abstractions;
using Microsoft.Extensions.Logging;

namespace LyrionVoiceMcp.Services;

public sealed class ToolCallHistoryService(
    IToolCallStore store,
    OperationalPolicy policy,
    TimeProvider timeProvider,
    ILogger<ToolCallHistoryService> logger) : IToolCallHistoryService
{
    private const int DeleteBatchSize = 200;

    public int RetentionDays => policy.ToolCallRetentionDays;

    public BoundedJson BoundJson(string json)
    {
        if (json.Length <= policy.ToolCallJsonMaximumCharacters)
        {
            return new BoundedJson(json, false);
        }

        var prefixLength = Math.Max(0, policy.ToolCallJsonMaximumCharacters - 256);
        return new BoundedJson(
            JsonSerializer.Serialize(new
            {
                truncated = true,
                originalCharacterCount = json.Length,
                prefix = json[..prefixLength]
            }),
            true);
    }

    public async Task<ToolCallRecording?> StartAsync(
        string toolName,
        string argumentsJson,
        string? traceIdentifier,
        CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid().ToString("N");
        var startedAt = timeProvider.GetUtcNow();
        try
        {
            var bounded = BoundJson(argumentsJson);
            await store.StartAsync(new ToolCallStart(
                id,
                toolName,
                startedAt,
                bounded.Json,
                bounded.Truncated,
                traceIdentifier), cancellationToken);
            return new ToolCallRecording(id, startedAt);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to start the durable MCP tool-call record for {ToolName}.", toolName);
            return null;
        }
    }

    public async Task CompleteAsync(
        string id,
        ToolCallStatus status,
        DateTimeOffset startedAt,
        string? resultJson,
        string? errorMessage,
        long? errorLogId,
        CancellationToken cancellationToken)
    {
        try
        {
            var completedAt = timeProvider.GetUtcNow();
            var bounded = resultJson is null ? null : BoundJson(resultJson);
            await store.CompleteAsync(new ToolCallCompletion(
                id,
                status,
                completedAt,
                Math.Max(0, (long)(completedAt - startedAt).TotalMilliseconds),
                bounded?.Json,
                bounded?.Truncated ?? false,
                errorMessage,
                errorLogId), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to complete durable MCP tool-call record {ToolCallId}.", id);
        }
    }

    public Task<ToolCallPage> BrowseAsync(
        ToolCallQuery query,
        CancellationToken cancellationToken) =>
        store.BrowseAsync(query, cancellationToken);

    public Task<ToolCall?> GetAsync(string id, CancellationToken cancellationToken) =>
        store.GetAsync(id, cancellationToken);

    public async Task<int> PurgeOlderThanAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken)
    {
        var total = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var deleted = await store.DeleteOlderThanAsync(cutoff, DeleteBatchSize, cancellationToken);
            total += deleted;
            if (deleted < DeleteBatchSize)
            {
                return total;
            }
        }
    }
}
