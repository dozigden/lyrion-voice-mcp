using LyrionVoiceMcp.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LyrionVoiceMcp.Search;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLyrionVoiceMcpProductionSearch(
        this IServiceCollection services,
        ProductionSearchSettings settings)
    {
        services.AddSingleton(settings);
        services.AddSingleton<ProductionCatalogueSearchService>();
        services.AddSingleton<ISearchIndexBuilder>(provider =>
            provider.GetRequiredService<ProductionCatalogueSearchService>());
        services.AddSingleton<ICatalogueSearchResolver>(provider =>
            provider.GetRequiredService<ProductionCatalogueSearchService>());
        services.AddSingleton<ICatalogueArtistTrackResolver>(provider =>
            provider.GetRequiredService<ProductionCatalogueSearchService>());
        services.AddSingleton<IDiagnosticSearchResolver>(provider =>
            provider.GetRequiredService<ProductionCatalogueSearchService>());
        services.AddSingleton<IRatingBrowseResolver>(provider =>
            provider.GetRequiredService<ProductionCatalogueSearchService>());
        return services;
    }
}
