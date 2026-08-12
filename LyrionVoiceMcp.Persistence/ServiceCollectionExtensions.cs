using LyrionVoiceMcp.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LyrionVoiceMcp.Persistence;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSearchObservationPersistence(
        this IServiceCollection services,
        SearchObservationSettings settings)
    {
        services.AddSingleton(settings);
        services.AddSingleton<ISearchObservationStore, SqliteSearchObservationStore>();
        return services;
    }
}
