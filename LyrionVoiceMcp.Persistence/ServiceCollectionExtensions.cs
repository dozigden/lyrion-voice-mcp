using LyrionVoiceMcp.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LyrionVoiceMcp.Persistence;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCataloguePersistence(
        this IServiceCollection services,
        CatalogueSettings settings)
    {
        services.AddSingleton(settings);
        services.AddSingleton<IMediaCatalogueStore, SqliteMediaCatalogueStore>();
        return services;
    }

    public static IServiceCollection AddOperationalPersistence(
        this IServiceCollection services,
        OperationalSettings settings,
        OperationalSchedulePolicy schedulePolicy)
    {
        services.AddSingleton(settings);
        services.AddSingleton(settings.ToPolicy());
        services.AddSingleton(schedulePolicy);
        services.AddSingleton<SqliteOperationalStore>();
        services.AddSingleton<IOperationalStoreInitialiser>(provider =>
            provider.GetRequiredService<SqliteOperationalStore>());
        services.AddSingleton<IJobStore>(provider =>
            provider.GetRequiredService<SqliteOperationalStore>());
        services.AddSingleton<IErrorLogStore>(provider =>
            provider.GetRequiredService<SqliteOperationalStore>());
        services.AddSingleton<IToolCallStore>(provider =>
            provider.GetRequiredService<SqliteOperationalStore>());
        return services;
    }

    public static IServiceCollection AddSearchObservationPersistence(
        this IServiceCollection services,
        SearchObservationSettings settings)
    {
        services.AddSingleton(settings);
        services.AddSingleton<ISearchObservationStore, SqliteSearchObservationStore>();
        return services;
    }
}
