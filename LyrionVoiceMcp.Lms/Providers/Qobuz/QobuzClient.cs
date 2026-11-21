using System.Text.Json;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Abstractions.Providers.Qobuz;
using Microsoft.Extensions.DependencyInjection;

namespace LyrionVoiceMcp.Lms.Providers.Qobuz;

internal sealed partial class QobuzClient(LmsJsonRpcClient rpc) : IQobuzClient, IQobuzSearchClient
{
    private const string PluginCommand = "qobuz";
    private const int PageSize = 50;
    private readonly LmsPluginDiscovery discovery = new(rpc,
        () => new LmsRequestException("LMS returned an incomplete or invalid Qobuz discovery response."));

    public async Task<QobuzConfigurationStatus> GetConfigurationStatusAsync(CancellationToken cancellationToken)
    {
        if (!await discovery.IsInstalledAsync(PluginCommand, cancellationToken))
        {
            return QobuzConfigurationStatus.NotInstalled;
        }

        var player = await discovery.FindConnectedPlayerAsync(cancellationToken)
            ?? throw new LmsRequestException("Checking Qobuz configuration requires a connected LMS player.");
        var menu = await rpc.SendAsync(player, [PluginCommand, "items", 0, PageSize, "menu:1"], cancellationToken);
        var count = LmsJson.ReadInt(menu, "count");
        if (!menu.TryGetProperty("item_loop", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            throw InvalidMenu();
        }

        var hasTextArea = menu.TryGetProperty("window", out var window) && window.ValueKind == JsonValueKind.Object
            && HasText(window, "textarea");
        // The plugin's root feed emits a textarea only when no account has been added.
        // LMS renders it outside the item count and leaves an empty placeholder in the loop.
        if (count == 0 && hasTextArea && LmsJson.ReadString(window, "windowStyle") == "text_list"
            && items.GetArrayLength() == 1 && items[0].ValueKind == JsonValueKind.Object
            && !items[0].EnumerateObject().Any())
        {
            return QobuzConfigurationStatus.NotConfigured;
        }

        if (count is int total && total > 0 && !hasTextArea && items.GetArrayLength() == Math.Min(total, PageSize)
            && items.EnumerateArray().All(item => HasText(item, "text")))
        {
            return QobuzConfigurationStatus.Configured;
        }

        throw InvalidMenu();
    }

    private static bool HasText(JsonElement item, string property) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(property, out var text)
        && text.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(text.GetString());

    private static LmsRequestException InvalidMenu() =>
        new("Qobuz returned an incomplete or unsupported root menu.");
}

internal static class QobuzLmsRegistration
{
    public static IServiceCollection AddQobuzLms(this IServiceCollection services) =>
        services.AddTransient<IQobuzClient, QobuzClient>()
            .AddTransient<IQobuzSearchClient, QobuzClient>()
            .AddTransient<LyrionVoiceMcp.Abstractions.Providers.IExplicitSearchSource, QobuzClient>();
}
