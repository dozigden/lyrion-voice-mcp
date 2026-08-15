using System.Threading.Channels;
using LyrionVoiceMcp.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LyrionVoiceMcp.Services;

public sealed class CatalogueRefreshService(
    IServiceScopeFactory scopeFactory,
    IMediaCatalogueStore store,
    TimeProvider timeProvider,
    ILogger<CatalogueRefreshService> logger) : BackgroundService, ICatalogueRefreshService
{
    private readonly Channel<RefreshRequest> requests = Channel.CreateBounded<RefreshRequest>(
        new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = false
        });
    private int pending;

    public async Task<CatalogueStatus> GetStatusAsync(CancellationToken cancellationToken) => new(
        await store.GetSummaryAsync(cancellationToken),
        await store.GetLatestRefreshRunAsync(cancellationToken));

    public async Task<CatalogueRefreshOutcome> RefreshAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref pending, 1, 0) != 0)
        {
            return new CatalogueRefreshAlreadyRunning(await GetStatusAsync(cancellationToken));
        }

        var request = new RefreshRequest(
            Guid.NewGuid().ToString("N"),
            timeProvider.GetUtcNow());
        try
        {
            await store.BeginRefreshAsync(request.Id, request.StartedAt, cancellationToken);
            if (!requests.Writer.TryWrite(request))
            {
                throw new InvalidOperationException("The catalogue refresh queue did not accept the request.");
            }

            return new CatalogueRefreshStarted(await GetStatusAsync(cancellationToken));
        }
        catch
        {
            Interlocked.Exchange(ref pending, 0);
            throw;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in requests.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ExecuteRefreshAsync(request, stoppingToken);
            }
            finally
            {
                Interlocked.Exchange(ref pending, 0);
            }
        }
    }

    private async Task ExecuteRefreshAsync(
        RefreshRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var sourceReader = scope.ServiceProvider.GetRequiredService<ICatalogueSourceReader>();
            var source = await sourceReader.ReadAsync(request.Id, store, cancellationToken);
            var completedAt = timeProvider.GetUtcNow();
            await store.CompleteRefreshAsync(
                request.Id,
                source,
                completedAt,
                DurationMilliseconds(request.StartedAt, completedAt),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var completedAt = timeProvider.GetUtcNow();
            await TryCompleteFailedRefreshAsync(
                request.Id,
                CatalogueRefreshRunStatus.Cancelled,
                completedAt,
                DurationMilliseconds(request.StartedAt, completedAt),
                "Catalogue refresh was cancelled.");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Catalogue refresh {RefreshId} failed.", request.Id);
            var completedAt = timeProvider.GetUtcNow();
            await TryCompleteFailedRefreshAsync(
                request.Id,
                CatalogueRefreshRunStatus.Failed,
                completedAt,
                DurationMilliseconds(request.StartedAt, completedAt),
                "Catalogue refresh failed. See the service logs for details.");
        }
    }

    private async Task TryCompleteFailedRefreshAsync(
        string refreshId,
        CatalogueRefreshRunStatus status,
        DateTimeOffset completedAt,
        long durationMilliseconds,
        string failureMessage)
    {
        try
        {
            await store.CompleteFailedRefreshAsync(
                refreshId,
                status,
                completedAt,
                durationMilliseconds,
                failureMessage,
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Catalogue refresh {RefreshId} could not record its terminal state.",
                refreshId);
        }
    }

    private static long DurationMilliseconds(DateTimeOffset startedAt, DateTimeOffset completedAt) =>
        Math.Max(0, (long)(completedAt - startedAt).TotalMilliseconds);

    private sealed record RefreshRequest(string Id, DateTimeOffset StartedAt);
}
