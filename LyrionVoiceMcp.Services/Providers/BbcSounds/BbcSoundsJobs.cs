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
    IBbcSoundsClient client, BbcSubscriptionStore store, IBbcSubscriptionIndex index, IJobLogWriter logs)
    : JobHandlerBase<BbcSoundsRefreshJob.Payload>
{
    public override string Type => BbcSoundsProvider.RefreshJob;
    protected override async Task<JobHandlerResult> HandleAsync(JobContext context, Payload payload, CancellationToken cancellationToken)
    {
        try
        {
            var subscriptions = await client.ReadSubscriptionsAsync(cancellationToken);
            await store.ReplaceAsync(subscriptions, cancellationToken);
            index.Publish(subscriptions);
            await store.CleanAsync(cancellationToken);
            return JobHandlerResult.Succeeded(JsonSerializer.Serialize(new { subscriptions.Available, ShowCount = subscriptions.Shows.Count }));
        }
        catch (LmsRequestException exception)
        {
            await logs.WriteAsync(context.JobId, JobLogLevel.Warning,
                "BBC Sounds refresh failed; the previous subscription snapshot was retained.", null, cancellationToken);
            return JobHandlerResult.Failed(exception.Message);
        }
    }
    public sealed record Payload;
}

internal sealed class BbcSoundsRestoreJob(BbcSubscriptionStore store, IBbcSubscriptionIndex index)
    : JobHandlerBase<BbcSoundsRestoreJob.Payload>
{
    public override string Type => BbcSoundsProvider.RestoreJob;
    protected override async Task<JobHandlerResult> HandleAsync(JobContext context, Payload payload, CancellationToken cancellationToken)
    {
        var subscriptions = await store.ReadAsync(cancellationToken);
        if (subscriptions is not null) index.Publish(subscriptions);
        return JobHandlerResult.Succeeded("{}");
    }
    public sealed record Payload;
}

internal static class BbcSoundsServiceRegistration
{
    public static IServiceCollection AddBbcSoundsServices(this IServiceCollection services)
    {
        services.AddTransient<BbcSubscriptionStore>();
        services.AddTransient<IProviderCatalogueContributor, BbcSoundsCatalogueContributor>();
        services.AddTransient<IProviderSearchSource, BbcSoundsSearchSource>();
        services.AddTransient<IProviderBrowseSource, BbcSoundsBrowseSource>();
        services.AddTransient<IProviderPlaybackSource, BbcSoundsPlaybackSource>();
        services.AddTransient<IJobHandler, BbcSoundsRefreshJob>();
        services.AddTransient<IJobHandler, BbcSoundsRestoreJob>();
        return services;
    }
}
