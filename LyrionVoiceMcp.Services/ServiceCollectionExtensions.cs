using LyrionVoiceMcp.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LyrionVoiceMcp.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLyrionVoiceMcpServices(this IServiceCollection services)
    {
        services.AddSingleton<IOperationalStatusService, OperationalStatusService>();
        services.AddTransient<ILmsConnectionStatusService, LmsConnectionStatusService>();
        services.AddTransient<IPlaybackService, PlaybackService>();
        services.AddTransient<IPlayerControlService, PlayerControlService>();
        services.AddTransient<IPlayerStatusService, PlayerStatusService>();
        services.AddTransient<IQueueService, QueueService>();
        services.AddTransient<ISearchService, SearchService>();
        services.AddTransient<ISearchObservationReviewService, SearchObservationReviewService>();
        services.AddSingleton<ISearchResultReferenceCodec, SearchResultReferenceCodec>();
        services.AddSingleton(TimeProvider.System);
        return services;
    }
}
