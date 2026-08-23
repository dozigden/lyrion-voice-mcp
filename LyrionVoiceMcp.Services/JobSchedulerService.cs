using System.Text.Json;
using LyrionVoiceMcp.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LyrionVoiceMcp.Services;

public sealed class JobSchedulerService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<JobSchedulerService> logger) : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ScheduledJobPollDelay = TimeSpan.FromMinutes(1);
    private readonly string schedulerId = $"{Environment.MachineName}:{Guid.NewGuid():N}";
    private DateTimeOffset nextScheduledJobPoll = DateTimeOffset.MinValue;
    private bool startupReadinessChecked;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Lyrion Voice MCP job scheduler {SchedulerId} starting.", schedulerId);
        await MarkInterruptedJobsFailedAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await EnqueueScheduledJobsIfDueAsync(scope.ServiceProvider, stoppingToken);
                if (!startupReadinessChecked)
                {
                    await scope.ServiceProvider.GetRequiredService<StartupReadinessService>()
                        .CheckAsync(stoppingToken);
                    startupReadinessChecked = true;
                }

                var runner = scope.ServiceProvider.GetRequiredService<IJobRunner>();
                if (!await runner.RunNextDueAsync(stoppingToken))
                {
                    await Task.Delay(IdleDelay, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "The job scheduler loop failed.");
                await TryLogSchedulerExceptionAsync(exception);
                try
                {
                    await Task.Delay(ErrorDelay, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        logger.LogInformation("Lyrion Voice MCP job scheduler {SchedulerId} stopped.", schedulerId);
    }

    private async Task MarkInterruptedJobsFailedAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IJobRunner>()
            .MarkRunningJobsFailedAsync(cancellationToken);
    }

    private async Task EnqueueScheduledJobsIfDueAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (now < nextScheduledJobPoll)
        {
            return;
        }

        nextScheduledJobPoll = now.Add(ScheduledJobPollDelay);
        var results = await services.GetRequiredService<IScheduledJobService>()
            .EnqueueDueJobsAsync(cancellationToken);
        var count = results.Count(result => result.Enqueued);
        if (count > 0)
        {
            logger.LogInformation(
                "Job scheduler {SchedulerId} enqueued {Count} scheduled job(s).",
                schedulerId,
                count);
        }
    }

    private async Task TryLogSchedulerExceptionAsync(Exception exception)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IErrorLogService>()
                .LogExceptionAsync(
                    exception,
                    new ErrorLogContext(
                        ErrorLogSources.Backend,
                        ErrorLogAreas.JobScheduler,
                        ContextJson: JsonSerializer.Serialize(new { schedulerId })),
                    CancellationToken.None);
        }
        catch (Exception logException)
        {
            logger.LogError(logException, "Failed to persist job scheduler exception details.");
        }
    }
}
