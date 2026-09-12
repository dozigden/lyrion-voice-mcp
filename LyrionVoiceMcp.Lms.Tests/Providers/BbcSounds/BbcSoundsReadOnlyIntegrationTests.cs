using LyrionVoiceMcp.Lms.Providers.BbcSounds;

namespace LyrionVoiceMcp.Lms.Tests.Providers.BbcSounds;

public sealed class BbcSoundsReadOnlyIntegrationTests
{
    public static bool IsConfigured => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LVM_BBC_READONLY_INTEGRATION_URL"));

    [Fact(Skip = "Requires an explicitly configured read-only LMS.", SkipUnless = nameof(IsConfigured))]
    [Trait("Category", "Integration")]
    public async Task ConfiguredPluginShouldExposeSubscriptionsAndEpisodeAudio()
    {
        var settings = LmsConnectionSettings.FromValues("readonly-integration",
            Environment.GetEnvironmentVariable("LVM_BBC_READONLY_INTEGRATION_URL"), "30");
        using var http = new HttpClient { Timeout = settings.RequestTimeout };
        var rpc = new LmsJsonRpcClient(settings, http);
        var client = new BbcSoundsClient(new BbcSoundsMenuClient(rpc), rpc);

        var subscriptions = await client.ReadSubscriptionsAsync(TestContext.Current.CancellationToken);
        Assert.True(subscriptions.Available, "The integration environment must have the plugin configured.");
        if (subscriptions.Shows.Count > 0)
        {
            var page = await client.BrowseEpisodesAsync(subscriptions.Shows[0].Id, 0, TestContext.Current.CancellationToken);
            Assert.True(page.Episodes.All(episode => !string.IsNullOrWhiteSpace(episode.Id)
                && !string.IsNullOrWhiteSpace(episode.Title) && episode.AudioUrl.StartsWith("sounds://", StringComparison.Ordinal)),
                "Episode entries must expose provider-owned audio references.");
        }
    }
}
