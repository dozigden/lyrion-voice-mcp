using LyrionVoiceMcp.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LyrionVoiceMcp.Lms;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLyrionVoiceMcpLms(
        this IServiceCollection services,
        LmsConnectionSettings settings,
        string applicationVersion)
    {
        services.AddSingleton(settings);
        services.AddHttpClient<LmsJsonRpcClient>(client =>
        {
            client.Timeout = settings.RequestTimeout;
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                $"LyrionVoiceMcp/{applicationVersion}");
        });
        services.AddTransient<ILmsConnectionProbe, LmsConnectionProbe>();
        services.AddTransient<ICatalogueSourceReader, LmsCatalogueReader>();
        services.AddTransient<ILmsBrowseClient, LmsBrowseClient>();
        services.AddTransient<ILmsPlaybackClient, LmsPlaybackClient>();
        services.AddTransient<ILmsPlayerControlClient, LmsPlayerControlClient>();
        services.AddTransient<ILmsPlayerClient, LmsPlayerClient>();
        services.AddTransient<ILmsQueueClient, LmsQueueClient>();
        services.AddTransient<ILmsPlaylistSearchClient, LmsSearchClient>();
        return services;
    }
}
