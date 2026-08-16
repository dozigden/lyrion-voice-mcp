using System.Text.Json;
using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Services;

public sealed class JobService(
    IJobStore store,
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

    public Task<JobPage> BrowseAsync(JobQuery query, CancellationToken cancellationToken) =>
        store.BrowseAsync(query, cancellationToken);

    public Task<JobDetails?> GetAsync(long id, CancellationToken cancellationToken) =>
        store.GetAsync(id, cancellationToken);

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
        if (normalised.CorrelationId is { } correlationId
            && await store.ExistsByCorrelationIdAsync(correlationId, cancellationToken))
        {
            return new JobEnqueueRejected("A job with that correlation already exists.");
        }

        try
        {
            return new JobEnqueued(await store.CreateAsync(
                normalised,
                timeProvider.GetUtcNow(),
                cancellationToken));
        }
        catch (JobConflictException)
        {
            return new JobEnqueueRejected("A conflicting job is already queued or running.");
        }
    }

    public Task<JobCancellationOutcome> RequestCancellationAsync(
        long id,
        CancellationToken cancellationToken) =>
        lifecycleGate.ExecuteAsync<JobCancellationOutcome>(async gateCancellationToken =>
        {
            var details = await store.GetAsync(id, gateCancellationToken);
            if (details is null)
            {
                return new JobCancellationRejected("Job not found.");
            }

            if (details.Job.Status == JobStatus.Pending)
            {
                var now = timeProvider.GetUtcNow();
                var cancelled = await store.CancelAsync(
                    id,
                    JobStatus.Pending,
                    JsonSerializer.Serialize(new { message = "Job cancelled before it started." }),
                    now,
                    gateCancellationToken);
                if (!cancelled)
                {
                    return new JobCancellationRejected("Job is no longer cancellable.");
                }

                return new JobCancellationAccepted(
                    (await store.GetAsync(id, gateCancellationToken))!.Job);
            }

            if (details.Job.Status != JobStatus.Running
                || !cancellationRegistry.RequestCancellation(id))
            {
                return new JobCancellationRejected("Job is not cancellable.");
            }

            await store.AppendLogAsync(
                id,
                JobLogLevel.Warning,
                "Job cancellation requested.",
                null,
                timeProvider.GetUtcNow(),
                gateCancellationToken);
            return new JobCancellationAccepted(details.Job);
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
