using System.Data;
using System.Text.Json;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Ef.Abstractions.DataAccess;
using LyrionVoiceMcp.Ef.Abstractions.Entities;
using LyrionVoiceMcp.Ef.Abstractions.ToolCalls;
using Microsoft.Extensions.Logging;

namespace LyrionVoiceMcp.Services;

public sealed class ToolCallHistoryService(
    IDbContextScopeFactory scopeFactory,
    IToolCallRepository repository,
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
        var toolCallId = Guid.NewGuid().ToString("N");
        var startedAt = timeProvider.GetUtcNow();
        try
        {
            var bounded = BoundJson(argumentsJson);
            using var suppression = scopeFactory.SuppressAmbientContext();
            using var scope = scopeFactory.Create(DbContextScopeOption.ForceCreateNew);
            repository.Add(new EntityToolCall
            {
                ToolCallId = toolCallId,
                ToolName = toolName,
                Status = EntityToolCallStatus.Running,
                StartedAtUtc = OperationalEntityMapper.ToUtcDateTime(startedAt),
                ArgumentsJson = bounded.Json,
                ArgumentsTruncated = bounded.Truncated,
                TraceIdentifier = traceIdentifier
            });
            await scope.SaveChangesAsync(cancellationToken);
            return new ToolCallRecording(toolCallId, startedAt);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to start the durable MCP tool-call record for {ToolName}.",
                toolName);
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
            using var suppression = scopeFactory.SuppressAmbientContext();
            using var scope = scopeFactory.Create(DbContextScopeOption.ForceCreateNew);
            var call = await repository.GetForUpdateAsync(id, cancellationToken);
            if (call is null || call.Status != EntityToolCallStatus.Running)
            {
                return;
            }

            var completedAt = timeProvider.GetUtcNow();
            var bounded = resultJson is null ? null : BoundJson(resultJson);
            call.Status = OperationalEntityMapper.ToEntity(status);
            call.CompletedAtUtc = OperationalEntityMapper.ToUtcDateTime(completedAt);
            call.DurationMilliseconds = Math.Max(
                0,
                (long)(completedAt - startedAt).TotalMilliseconds);
            call.ResultJson = bounded?.Json;
            call.ResultTruncated = bounded?.Truncated ?? false;
            call.ErrorMessage = errorMessage;
            call.ErrorLogId = OperationalEntityMapper.TryGetEntityId(errorLogId ?? 0, out var entityId)
                ? entityId
                : null;
            await scope.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to complete durable MCP tool-call record {ToolCallId}.", id);
        }
    }

    public async Task MarkRunningInterruptedAsync(CancellationToken cancellationToken)
    {
        using var suppression = scopeFactory.SuppressAmbientContext();
        using var scope = scopeFactory.Create(DbContextScopeOption.ForceCreateNew);
        var running = await repository.ListRunningForUpdateAsync(cancellationToken);
        if (running.Count == 0)
        {
            return;
        }

        var completedAt = timeProvider.GetUtcNow();
        var completedAtUtc = OperationalEntityMapper.ToUtcDateTime(completedAt);
        foreach (var call in running)
        {
            call.Status = EntityToolCallStatus.Interrupted;
            call.CompletedAtUtc = completedAtUtc;
            call.DurationMilliseconds = Math.Max(
                0,
                (long)(completedAtUtc - call.StartedAtUtc).TotalMilliseconds);
            call.ErrorMessage = "Tool call was interrupted by server startup.";
        }

        await scope.SaveChangesAsync(cancellationToken);
    }

    public async Task<ToolCallPage> BrowseAsync(
        ToolCallQuery query,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateReadOnly();
        var page = await repository.BrowseAsync(
            new EntityToolCallQuery(
                query.Offset,
                query.Limit,
                query.ToolName,
                query.Status is null
                    ? null
                    : OperationalEntityMapper.ToEntity(query.Status.Value)),
            cancellationToken);
        return new ToolCallPage(
            page.Items.Select(OperationalEntityMapper.ToModel).ToArray(),
            page.Total,
            page.Offset,
            page.Limit);
    }

    public async Task<ToolCall?> GetAsync(string id, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateReadOnly();
        var entity = await repository.GetAsync(id, cancellationToken);
        return entity is null ? null : OperationalEntityMapper.ToModel(entity);
    }

    public async Task<int> PurgeOlderThanAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken)
    {
        var total = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var scope = scopeFactory.CreateWithTransaction(IsolationLevel.Serializable);
            var deleted = await repository.DeleteOlderThanBatchAsync(
                OperationalEntityMapper.ToUtcDateTime(cutoff),
                DeleteBatchSize,
                cancellationToken);
            await scope.SaveChangesAsync(cancellationToken);
            total += deleted;
            if (deleted < DeleteBatchSize)
            {
                return total;
            }
        }
    }
}
