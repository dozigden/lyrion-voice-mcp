using System.Text.Json;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Ef.Abstractions.DataAccess;
using LyrionVoiceMcp.Ef.Abstractions.Jobs;

namespace LyrionVoiceMcp.Services;

public sealed class CatalogueChangeCheckJobHandler(
    ICatalogueSourceChangeTokenReader changeTokens,
    ICatalogueLifecycleService catalogue,
    CatalogueInitialisationPolicy initialisationPolicy,
    IDbContextScopeFactory scopeFactory,
    IJobRepository jobRepository,
    IJobService jobs,
    IJobLogWriter logs,
    TimeProvider timeProvider) : JobHandlerBase<CatalogueChangeCheckJobHandler.Payload>
{
    public override string Type => JobTypes.CatalogueChangeCheck;

    protected override async Task<JobHandlerResult> HandleAsync(
        JobContext context,
        Payload payload,
        CancellationToken cancellationToken)
    {
        if (!initialisationPolicy.SourceConfigured)
        {
            await WriteInformationAsync(
                context.JobId,
                "Catalogue change check skipped because LMS is not configured.",
                cancellationToken);
            return Result(checkedSource: false, refreshEnqueued: false);
        }

        if (await HasActiveRefreshAsync(cancellationToken))
        {
            await WriteInformationAsync(
                context.JobId,
                "Catalogue change check skipped while a refresh is active.",
                cancellationToken);
            return Result(checkedSource: false, refreshEnqueued: false);
        }

        var state = await catalogue.GetStateAsync(cancellationToken);
        var baseline = state?.SourceChangeToken;
        string? token;
        try
        {
            token = await changeTokens.ReadChangeTokenAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await logs.WriteAsync(
                context.JobId,
                JobLogLevel.Warning,
                "Catalogue change token could not be read.",
                new { error = exception.Message },
                cancellationToken);
            return JobHandlerResult.Failed(
                "The LMS catalogue change check failed.",
                ResultJson(checkedSource: true, refreshEnqueued: false));
        }

        if (token is null)
        {
            await WriteInformationAsync(
                context.JobId,
                "No usable LMS catalogue change token is available.",
                cancellationToken);
            return Result(checkedSource: true, refreshEnqueued: false);
        }

        if (string.Equals(token, baseline, StringComparison.Ordinal))
        {
            await WriteInformationAsync(
                context.JobId,
                "LMS catalogue change token matches the successful refresh.",
                cancellationToken);
            return Result(checkedSource: true, refreshEnqueued: false);
        }

        if (await HasActiveRefreshAsync(cancellationToken))
        {
            await WriteInformationAsync(
                context.JobId,
                "Catalogue refresh was already requested while checking LMS changes.",
                cancellationToken);
            return Result(checkedSource: true, refreshEnqueued: false);
        }

        var outcome = await jobs.EnqueueAsync(
            new CreateJob(
                JobTypes.CatalogueRefresh,
                "{}",
                timeProvider.GetUtcNow(),
                $"change:catalogue.refresh:{context.JobId}"),
            cancellationToken);
        if (outcome is not JobEnqueued enqueued)
        {
            var message = outcome is JobEnqueueRejected rejected
                ? rejected.Message
                : "Catalogue refresh could not be queued.";
            await logs.WriteAsync(
                context.JobId,
                JobLogLevel.Warning,
                "Catalogue change was detected but its refresh could not be queued.",
                new { error = message },
                cancellationToken);
            return JobHandlerResult.Failed(
                message,
                ResultJson(checkedSource: true, refreshEnqueued: false));
        }

        await logs.WriteAsync(
            context.JobId,
            JobLogLevel.Information,
            baseline is null
                ? "Catalogue refresh queued to establish an LMS change-check baseline."
                : "Catalogue refresh queued after detecting an LMS library scan change.",
            new { jobId = enqueued.Job.Id },
            cancellationToken);
        return Result(checkedSource: true, refreshEnqueued: true);
    }

    private async Task<bool> HasActiveRefreshAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateReadOnly();
        return await jobRepository.GetLatestActiveByTypeAsync(
            JobTypes.CatalogueRefresh,
            cancellationToken) is not null;
    }

    private Task WriteInformationAsync(
        long jobId,
        string message,
        CancellationToken cancellationToken) =>
        logs.WriteAsync(
            jobId,
            JobLogLevel.Information,
            message,
            null,
            cancellationToken);

    private static JobHandlerResult Result(bool checkedSource, bool refreshEnqueued) =>
        JobHandlerResult.Succeeded(ResultJson(checkedSource, refreshEnqueued));

    private static string ResultJson(bool checkedSource, bool refreshEnqueued) =>
        JsonSerializer.Serialize(new { checkedSource, refreshEnqueued });

    public sealed record Payload;
}
