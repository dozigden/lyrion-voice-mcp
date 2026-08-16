using System.Text.Json;
using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Services;

public sealed class JobRunner(
    IJobStore store,
    IEnumerable<IJobHandler> handlers,
    IJobCancellationRegistry cancellationRegistry,
    IJobLifecycleGate lifecycleGate,
    IErrorLogService errorLogService,
    TimeProvider timeProvider) : IJobRunner
{
    private readonly IReadOnlyDictionary<string, IJobHandler> handlersByType = handlers
        .GroupBy(handler => handler.Type, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);

    public async Task MarkRunningJobsFailedAsync(CancellationToken cancellationToken)
    {
        const string message = "Job was interrupted by server startup.";
        await lifecycleGate.ExecuteAsync(
            token => store.MarkRunningInterruptedAsync(
                timeProvider.GetUtcNow(),
                message,
                token),
            cancellationToken);
    }

    public async Task<bool> RunNextDueAsync(CancellationToken cancellationToken)
    {
        Exception? registrationException = null;
        Job? failedRegistrationJob = null;
        var startedJob = await lifecycleGate.ExecuteAsync(async token =>
        {
            var job = await store.TryStartNextDueAsync(timeProvider.GetUtcNow(), token);
            if (job is null)
            {
                return null;
            }

            try
            {
                return new StartedJob(
                    job,
                    cancellationRegistry.Register(job.Id, cancellationToken));
            }
            catch (Exception exception)
            {
                registrationException = exception;
                failedRegistrationJob = job;
                await store.FailAsync(
                    job.Id,
                    "The job cancellation scope could not be registered.",
                    "{}",
                    timeProvider.GetUtcNow(),
                    token);
                return null;
            }
        }, cancellationToken);
        if (registrationException is not null)
        {
            await errorLogService.LogExceptionAsync(
                registrationException,
                new ErrorLogContext(
                    ErrorLogSources.Backend,
                    ErrorLogAreas.JobRunner,
                    JobId: failedRegistrationJob?.Id,
                    ContextJson: JsonSerializer.Serialize(new
                    {
                        jobId = failedRegistrationJob?.Id,
                        type = failedRegistrationJob?.Type
                    })),
                cancellationToken);
            return true;
        }

        if (startedJob is null)
        {
            return false;
        }

        var job = startedJob.Job;
        var jobCancellationToken = startedJob.CancellationToken;
        try
        {
            if (!handlersByType.TryGetValue(job.Type, out var handler))
            {
                await MarkFailedAsync(
                    job.Id,
                    $"No job handler is registered for type '{job.Type}'.",
                    "{}",
                    cancellationToken);
                return true;
            }

            JobHandlerResult result;
            try
            {
                result = await handler.HandleAsync(
                    new JobContext(job.Id, job.Type, job.PayloadJson),
                    jobCancellationToken);
            }
            catch (OperationCanceledException) when (
                cancellationRegistry.IsCancellationRequested(job.Id)
                && !cancellationToken.IsCancellationRequested)
            {
                await MarkCancelledAsync(job.Id, JobStatus.Running, cancellationToken);
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                await errorLogService.LogExceptionAsync(
                    exception,
                    new ErrorLogContext(
                        ErrorLogSources.Backend,
                        ErrorLogAreas.JobRunner,
                        JobId: job.Id,
                        ContextJson: JsonSerializer.Serialize(new { job.Id, job.Type })),
                    cancellationToken);
                result = JobHandlerResult.Failed(exception.Message);
            }

            if (!result.ShouldFinalise)
            {
                if (cancellationRegistry.IsCancellationRequested(job.Id))
                {
                    await MarkCancelledAsync(job.Id, JobStatus.Running, cancellationToken);
                    return true;
                }

                await lifecycleGate.ExecuteAsync(
                    token => store.RequeueAsync(
                        job.Id,
                        NormaliseJson(result.ResultJson),
                        result.RunAfter ?? timeProvider.GetUtcNow(),
                        timeProvider.GetUtcNow(),
                        token),
                    cancellationToken);
                return true;
            }

            if (result.Success)
            {
                await lifecycleGate.ExecuteAsync(
                    token => store.CompleteAsync(
                        job.Id,
                        NormaliseJson(result.ResultJson),
                        timeProvider.GetUtcNow(),
                        token),
                    cancellationToken);
                return true;
            }

            await MarkFailedAsync(
                job.Id,
                result.ErrorMessage ?? "Job failed.",
                result.ResultJson,
                cancellationToken);
            return true;
        }
        finally
        {
            cancellationRegistry.Unregister(job.Id);
        }
    }

    private Task MarkFailedAsync(
        long jobId,
        string errorMessage,
        string resultJson,
        CancellationToken cancellationToken) =>
        lifecycleGate.ExecuteAsync(
            token => store.FailAsync(
                jobId,
                errorMessage,
                NormaliseJson(resultJson),
                timeProvider.GetUtcNow(),
                token),
            cancellationToken);

    private Task MarkCancelledAsync(
        long jobId,
        JobStatus expectedStatus,
        CancellationToken cancellationToken) =>
        lifecycleGate.ExecuteAsync(
            token => store.CancelAsync(
                jobId,
                expectedStatus,
                JsonSerializer.Serialize(new { message = "Job cancelled." }),
                timeProvider.GetUtcNow(),
                token),
            cancellationToken);

    private static string NormaliseJson(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "{}";
        }

        try
        {
            using var _ = JsonDocument.Parse(value);
            return value.Trim();
        }
        catch (JsonException)
        {
            return JsonSerializer.Serialize(new { message = value });
        }
    }

    private sealed record StartedJob(Job Job, CancellationToken CancellationToken);
}
