using LyrionVoiceMcp.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LyrionVoiceMcp.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLyrionVoiceMcpServices(this IServiceCollection services)
    {
        services.AddSingleton<IOperationalStatusService, OperationalStatusService>();
        services.AddTransient<ILmsConnectionStatusService, LmsConnectionStatusService>();
        services.AddTransient<IPlayerStatusService, PlayerStatusService>();
        services.AddTransient<ISearchService, SearchService>();
        services.AddSingleton<ISearchResultReferenceCodec, SearchResultReferenceCodec>();
        return services;
    }
}
