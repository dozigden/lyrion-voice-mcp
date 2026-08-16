using LyrionVoiceMcp.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LyrionVoiceMcp.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLyrionVoiceMcpServices(this IServiceCollection services)
    {
        services.AddSingleton<IOperationalStatusService, OperationalStatusService>();
        services.AddSingleton<ICatalogueRefreshService, CatalogueRefreshService>();
        services.AddSingleton<ISearchIndexService, SearchIndexService>();
        services.AddSingleton<IJobLifecycleGate, JobLifecycleGate>();
        services.AddSingleton<IJobCancellationRegistry, JobCancellationRegistry>();
        services.AddTransient<IJobService, JobService>();
        services.AddTransient<IJobRunner, JobRunner>();
        services.AddTransient<IJobLogWriter, JobLogWriter>();
        services.AddTransient<IErrorLogService, ErrorLogService>();
        services.AddTransient<IToolCallHistoryService, ToolCallHistoryService>();
        services.AddTransient<IScheduledJobService, ScheduledJobService>();
        services.AddTransient<ICronOccurrenceCalculator, CronOccurrenceCalculator>();
        services.AddTransient<IJobHandler, CatalogueRefreshJobHandler>();
        services.AddTransient<IJobHandler, SearchIndexRebuildJobHandler>();
        services.AddTransient<IJobHandler, ErrorLogPurgeJobHandler>();
        services.AddTransient<IJobHandler, JobHistoryPurgeJobHandler>();
        services.AddTransient<IJobHandler, ToolCallHistoryPurgeJobHandler>();
        services.AddTransient<IScheduledJobDefinition, CatalogueRefreshSchedule>();
        services.AddTransient<IScheduledJobDefinition, ErrorLogPurgeSchedule>();
        services.AddTransient<IScheduledJobDefinition, JobHistoryPurgeSchedule>();
        services.AddTransient<IScheduledJobDefinition, ToolCallHistoryPurgeSchedule>();
        services.AddHostedService<JobSchedulerService>();
        services.AddTransient<IBrowseService, BrowseService>();
        services.AddTransient<ILmsConnectionStatusService, LmsConnectionStatusService>();
        services.AddTransient<IPlaybackService, PlaybackService>();
        services.AddTransient<IPlayerControlService, PlayerControlService>();
        services.AddTransient<IPlayerStatusService, PlayerStatusService>();
        services.AddTransient<IQueueService, QueueService>();
        services.AddTransient<IQueueManagementService, QueueManagementService>();
        services.AddTransient<ISearchService, SearchService>();
        services.AddTransient<ISearchObservationReviewService, SearchObservationReviewService>();
        services.AddSingleton<IBrowseReferenceCodec, BrowseReferenceCodec>();
        services.AddSingleton<IPlayableReferenceResolver, PlayableReferenceResolver>();
        services.AddSingleton<ISearchResultReferenceCodec, SearchResultReferenceCodec>();
        services.AddSingleton(TimeProvider.System);
        return services;
    }
}
