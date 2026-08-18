using System.Text.Json;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Ef.Abstractions.DataAccess;
using LyrionVoiceMcp.Ef.Abstractions.Entities;
using LyrionVoiceMcp.Ef.Abstractions.Jobs;

namespace LyrionVoiceMcp.Services;

public sealed class JobService(
    IDbContextScopeFactory scopeFactory,
    IJobRepository jobRepository,
    IJobLogRepository jobLogRepository,
    IJobCancellationRegistry cancellationRegistry,
    IJobLifecycleGate lifecycleGate,
    OperationalPolicy policy,
    TimeProvider timeProvider) : IJobService
{
    private const int MaximumPageSize = 200;
    private const int MaximumTypeLength = 200;
    private const int MaximumCorrelationLength = 256;
    private const int MaximumJsonLength = 1_048_576;

    public int RetentionDays => policy.JobRetentionDays;

    public async Task<JobPage> BrowseAsync(JobQuery query, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateReadOnly();
        var page = await jobRepository.BrowseAsync(
            new EntityJobQuery(
                query.Offset,
                query.Limit,
                query.Type,
                query.Status is null ? null : OperationalEntityMapper.ToEntity(query.Status.Value)),
            cancellationToken);
        return new JobPage(
            page.Items.Select(OperationalEntityMapper.ToModel).ToArray(),
            page.Total,
            page.Offset,
            page.Limit);
    }

    public async Task<JobDetails?> GetAsync(long id, CancellationToken cancellationToken)
    {
        if (!OperationalEntityMapper.TryGetEntityId(id, out var entityId))
        {
            return null;
        }

        using var scope = scopeFactory.CreateReadOnly();
        var entity = await jobRepository.GetWithLogsAsync(entityId, cancellationToken);
        return entity is null
            ? null
            : new JobDetails(
                OperationalEntityMapper.ToModel(entity),
                entity.Logs.Select(OperationalEntityMapper.ToModel).ToArray());
    }

    public async Task<JobEnqueueOutcome> EnqueueAsync(
        CreateJob request,
        CancellationToken cancellationToken)
    {
        var rejection = Validate(request);
        if (rejection is not null)
        {
            return new JobEnqueueRejected(rejection);
        }

        var normalised = request with
        {
            Type = request.Type.Trim(),
            PayloadJson = NormaliseJson(request.PayloadJson),
            CorrelationId = NormaliseOptional(request.CorrelationId)
        };
        if (normalised.CorrelationId is { } correlationId)
        {
            using var readScope = scopeFactory.CreateReadOnly();
            if (await jobRepository.ExistsByCorrelationIdAsync(correlationId, cancellationToken))
            {
                return new JobEnqueueRejected("A job with that correlation already exists.");
            }
        }

        var nowUtc = OperationalEntityMapper.ToUtcDateTime(timeProvider.GetUtcNow());
        using var scope = scopeFactory.Create();
        var entity = new EntityJob
        {
            Type = normalised.Type,
            Status = EntityJobStatus.Pending,
            RunAfterUtc = OperationalEntityMapper.ToUtcDateTime(normalised.RunAfter),
            PayloadJson = normalised.PayloadJson,
            ResultJson = "{}",
            CorrelationId = normalised.CorrelationId
        };
        jobRepository.Add(entity);
        jobLogRepository.Add(new EntityJobLog
        {
            Job = entity,
            Level = EntityJobLogLevel.Information,
            Message = "Job enqueued.",
            LoggedAtUtc = nowUtc
        });

        try
        {
            await scope.SaveChangesAsync(cancellationToken);
            return new JobEnqueued(OperationalEntityMapper.ToModel(entity));
        }
        catch (PersistenceConflictException)
        {
            return new JobEnqueueRejected("A conflicting job is already queued or running.");
        }
    }

    public Task<JobCancellationOutcome> RequestCancellationAsync(
        long id,
        CancellationToken cancellationToken) =>
        lifecycleGate.ExecuteAsync<JobCancellationOutcome>(async gateCancellationToken =>
        {
            if (!OperationalEntityMapper.TryGetEntityId(id, out var entityId))
            {
                return new JobCancellationRejected("Job not found.");
            }

            using var scope = scopeFactory.Create();
            var job = await jobRepository.GetForUpdateAsync(entityId, gateCancellationToken);
            if (job is null)
            {
                return new JobCancellationRejected("Job not found.");
            }

            var nowUtc = OperationalEntityMapper.ToUtcDateTime(timeProvider.GetUtcNow());
            if (job.Status == EntityJobStatus.Pending)
            {
                job.Status = EntityJobStatus.Cancelled;
                job.ResultJson = JsonSerializer.Serialize(new
                {
                    message = "Job cancelled before it started."
                });
                job.ErrorMessage = null;
                job.CompletedAtUtc = nowUtc;
                AddLog(job.Id, EntityJobLogLevel.Warning, "Job cancelled.", job.ResultJson, nowUtc);
                await scope.SaveChangesAsync(gateCancellationToken);
                return new JobCancellationAccepted(OperationalEntityMapper.ToModel(job));
            }

            if (job.Status != EntityJobStatus.Running
                || !cancellationRegistry.RequestCancellation(job.Id))
            {
                return new JobCancellationRejected("Job is not cancellable.");
            }

            AddLog(
                job.Id,
                EntityJobLogLevel.Warning,
                "Job cancellation requested.",
                null,
                nowUtc);
            await scope.SaveChangesAsync(gateCancellationToken);
            return new JobCancellationAccepted(OperationalEntityMapper.ToModel(job));
        }, cancellationToken);

    public static string? ValidateQuery(JobQuery query)
    {
        if (query.Offset < 0)
        {
            return "Job offset must be zero or greater.";
        }

        if (query.Limit is < 1 or > MaximumPageSize)
        {
            return $"Job limit must be between 1 and {MaximumPageSize}.";
        }

        return null;
    }

    private void AddLog(
        int jobId,
        EntityJobLogLevel level,
        string message,
        string? dataJson,
        DateTime loggedAtUtc) => jobLogRepository.Add(new EntityJobLog
    {
        JobId = jobId,
        Level = level,
        Message = message,
        DataJson = dataJson,
        LoggedAtUtc = loggedAtUtc
    });

    private static string? Validate(CreateJob request)
    {
        if (string.IsNullOrWhiteSpace(request.Type))
        {
            return "Job type is required.";
        }

        if (request.Type.Trim().Length > MaximumTypeLength)
        {
            return $"Job type must be at most {MaximumTypeLength} characters.";
        }

        if (request.CorrelationId?.Trim().Length > MaximumCorrelationLength)
        {
            return $"Job correlation must be at most {MaximumCorrelationLength} characters.";
        }

        if (request.PayloadJson?.Length > MaximumJsonLength)
        {
            return "Job payload is too large.";
        }

        try
        {
            using var _ = JsonDocument.Parse(NormaliseJson(request.PayloadJson));
        }
        catch (JsonException)
        {
            return "Job payload must be valid JSON.";
        }

        return null;
    }

    private static string NormaliseJson(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "{}" : value.Trim();

    private static string? NormaliseOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
