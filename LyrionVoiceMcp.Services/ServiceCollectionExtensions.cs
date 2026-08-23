using LyrionVoiceMcp.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LyrionVoiceMcp.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLyrionVoiceMcpServices(this IServiceCollection services)
    {
        services.AddSingleton<IOperationalStatusService, OperationalStatusService>();
        services.AddTransient<ICatalogueRefreshService, CatalogueRefreshService>();
        services.AddTransient<ICatalogueLifecycleService, CatalogueLifecycleService>();
        services.AddTransient<ICatalogueImportWriter, CatalogueImportWriter>();
        services.AddSingleton<ICatalogueSearchDocumentSource, CatalogueSearchDocumentSource>();
        services.AddTransient<ISearchIndexService, SearchIndexService>();
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
        services.AddSingleton<IPlayerSelectorResolver, PlayerSelectorResolver>();
        services.AddTransient<IPlayerStatusService, PlayerStatusService>();
        services.AddTransient<IQueueService, QueueService>();
        services.AddTransient<IQueueManagementService, QueueManagementService>();
        services.AddTransient<SearchObservationRecorder>();
        services.AddTransient<SearchCandidateSelector>();
        services.AddTransient<ISearchObservationStore, EfSearchObservationStore>();
        services.AddTransient<ISearchService, SearchService>();
        services.AddTransient<ISearchObservationReviewService, SearchObservationReviewService>();
        services.AddSingleton<ReferenceHandleRegistry>();
        services.AddSingleton<IBrowseReferenceCodec>(provider =>
            new BrowseReferenceCodec(
                provider.GetRequiredService<ReferenceHandleRegistry>()));
        services.AddSingleton<IPlayableReferenceResolver, PlayableReferenceResolver>();
        services.AddSingleton<ISearchResultReferenceCodec>(provider =>
            new SearchResultReferenceCodec(
                provider.GetRequiredService<ReferenceHandleRegistry>()));
        services.AddSingleton(TimeProvider.System);
        return services;
    }
}
