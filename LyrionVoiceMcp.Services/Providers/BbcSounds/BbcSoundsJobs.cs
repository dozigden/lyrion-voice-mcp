using System.Text.Json;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Abstractions.Providers;
using LyrionVoiceMcp.Abstractions.Providers.BbcSounds;
using Microsoft.Extensions.DependencyInjection;

namespace LyrionVoiceMcp.Services.Providers.BbcSounds;

internal sealed class BbcSoundsCatalogueContributor(IJobService jobs, TimeProvider time) : IProviderCatalogueContributor
{
    public async Task EnqueueRefreshAsync(string correlationId, CancellationToken cancellationToken) =>
        await jobs.EnqueueAsync(new CreateJob(BbcSoundsProvider.RefreshJob, "{}", time.GetUtcNow(),
            $"{BbcSoundsProvider.Id}:{correlationId}"), cancellationToken);
    public async Task EnqueueRestoreAsync(CancellationToken cancellationToken) =>
        await jobs.EnqueueAsync(new CreateJob(BbcSoundsProvider.RestoreJob, "{}", time.GetUtcNow(),
            $"{BbcSoundsProvider.Id}:startup:{Guid.NewGuid():N}"), cancellationToken);
}

internal sealed class BbcSoundsRefreshJob(
    IBbcSoundsClient client,
    BbcSubscriptionStore subscriptionStore,
    BbcStationStore stationStore,
    IBbcSubscriptionIndex subscriptionIndex,
    IBbcStationIndex stationIndex,
    IJobLogWriter logs)
    : JobHandlerBase<BbcSoundsRefreshJob.Payload>
{
    public override string Type => BbcSoundsProvider.RefreshJob;
    protected override async Task<JobHandlerResult> HandleAsync(JobContext context, Payload payload, CancellationToken cancellationToken)
    {
        try
        {
            var subscriptions = await client.ReadSubscriptionsAsync(cancellationToken);
            var stations = await client.ReadStationsAsync(cancellationToken);
            await subscriptionStore.ReplaceAsync(subscriptions, cancellationToken);
            await stationStore.ReplaceAsync(stations, cancellationToken);
            subscriptionIndex.Publish(subscriptions);
            stationIndex.Publish(stations);
            await subscriptionStore.CleanAsync(cancellationToken);
            await stationStore.CleanAsync(cancellationToken);
            return JobHandlerResult.Succeeded(JsonSerializer.Serialize(new
            {
                subscriptions.Available,
                ShowCount = subscriptions.Shows.Count,
                StationAvailable = stations.Available,
                StationCount = stations.Stations.Count
            }));
        }
        catch (LmsRequestException exception)
        {
            await logs.WriteAsync(context.JobId, JobLogLevel.Warning,
                "BBC Sounds refresh failed; the previous provider snapshot was retained.", null, cancellationToken);
            return JobHandlerResult.Failed(exception.Message);
        }
    }
    public sealed record Payload;
}

internal sealed class BbcSoundsRestoreJob(
    BbcSubscriptionStore subscriptionStore,
    BbcStationStore stationStore,
    IBbcSubscriptionIndex subscriptionIndex,
    IBbcStationIndex stationIndex)
    : JobHandlerBase<BbcSoundsRestoreJob.Payload>
{
    public override string Type => BbcSoundsProvider.RestoreJob;
    protected override async Task<JobHandlerResult> HandleAsync(JobContext context, Payload payload, CancellationToken cancellationToken)
    {
        var subscriptions = await subscriptionStore.ReadAsync(cancellationToken);
        var stations = await stationStore.ReadAsync(cancellationToken);
        if (subscriptions is not null) subscriptionIndex.Publish(subscriptions);
        if (stations is not null) stationIndex.Publish(stations);
        return JobHandlerResult.Succeeded("{}");
    }
    public sealed record Payload;
}

internal static class BbcSoundsServiceRegistration
{
    public static IServiceCollection AddBbcSoundsServices(this IServiceCollection services)
    {
        services.AddTransient<BbcSubscriptionStore>();
        services.AddTransient<BbcStationStore>();
        services.AddTransient<IProviderCatalogueContributor, BbcSoundsCatalogueContributor>();
        services.AddTransient<IProviderSearchSource, BbcSoundsSearchSource>();
        services.AddTransient<IProviderBrowseSource, BbcSoundsBrowseSource>();
        services.AddTransient<IProviderPlaybackSource, BbcSoundsPlaybackSource>();
        services.AddTransient<IJobHandler, BbcSoundsRefreshJob>();
        services.AddTransient<IJobHandler, BbcSoundsRestoreJob>();
        return services;
    }
}
