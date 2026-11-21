using System.Net;
using System.Text;
using System.Text.Json;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Abstractions.Providers.Qobuz;
using Microsoft.Extensions.DependencyInjection;

namespace LyrionVoiceMcp.Lms.Tests.Providers.Qobuz;

public sealed class QobuzClientTests
{
    [Fact]
    public async Task MissingPluginShouldSkipTheMenuCheck()
    {
        using var handler = new StubLms { Apps = """{"count":0}""" };
        using var services = CreateServices(handler);
        var client = services.GetRequiredService<IQobuzClient>();

        var status = await client.GetConfigurationStatusAsync(TestContext.Current.CancellationToken);

        Assert.Equal(QobuzConfigurationStatus.NotInstalled, status);
        Assert.Equal(new[] { "apps", "radios" }, handler.Commands);
    }

    [Theory]
    [InlineData("""{"count":0,"item_loop":[{}],"window":{"windowStyle":"text_list","textarea":"Configurez votre compte Qobuz."}}""",
        QobuzConfigurationStatus.NotConfigured)]
    [InlineData("""{"count":"2","item_loop":[{"text":"Search"},{"text":"Favourites"}]}""",
        QobuzConfigurationStatus.Configured)]
    public async Task RootMenuShouldDetermineConfigurationStatus(string menu, QobuzConfigurationStatus expected)
    {
        using var handler = new StubLms { Menu = menu };
        using var services = CreateServices(handler);
        var client = services.GetRequiredService<IQobuzClient>();

        var status = await client.GetConfigurationStatusAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expected, status);
        Assert.Equal(new[] { "apps", "players", "qobuz" }, handler.Commands);
    }

    [Theory]
    [InlineData("""{"count":0,"item_loop":[]}""")]
    [InlineData("""{"count":2,"item_loop":[{"text":"Search"}]}""")]
    [InlineData("""{"count":1,"item_loop":[{}]}""")]
    public async Task UnexpectedRootMenuShouldFail(string menu)
    {
        using var handler = new StubLms { Menu = menu };
        using var services = CreateServices(handler);
        var client = services.GetRequiredService<IQobuzClient>();

        await Assert.ThrowsAsync<LmsRequestException>(() =>
            client.GetConfigurationStatusAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PluginDiscoveryWithoutAConnectedPlayerShouldFailBeforeReadingTheMenu()
    {
        using var handler = new StubLms { Players = """{"count":0}""" };
        using var services = CreateServices(handler);
        var client = services.GetRequiredService<IQobuzClient>();

        var exception = await Assert.ThrowsAsync<LmsRequestException>(() =>
            client.GetConfigurationStatusAsync(TestContext.Current.CancellationToken));

        Assert.Contains("requires a connected LMS player", exception.Message);
        Assert.Equal(new[] { "apps", "players" }, handler.Commands);
    }

    [Fact]
    public async Task LmsFailureShouldPropagate()
    {
        using var handler = new StubLms();
        using var services = CreateServices(handler);
        var client = services.GetRequiredService<IQobuzClient>();

        var exception = await Assert.ThrowsAsync<LmsRequestException>(() =>
            client.GetConfigurationStatusAsync(TestContext.Current.CancellationToken));

        Assert.Equal("LMS returned HTTP 503.", exception.Message);
    }

    [Fact]
    public async Task AlbumBrowseReadsOnlyNavigationAndKeepsAudioRowsInMenuOrder()
    {
        using var handler = new StubLms { Browse = command => command.Last() switch
        {
            "menu:1" => """{"count":1,"offset":0,"item_loop":[{"text":"Nouveautés","actions":{"go":{"cmd":["qobuz","items"],"params":{"item_id":"7"}}}}]}""",
            "item_id:7" => """{"count":1,"offset":0,"item_loop":[{"text":"Paper Moons","params":{"item_id":"7.0","touchToPlay":1}}]}""",
            "item_id:7.0" => """
                {"count":3,"offset":0,"item_loop":[
                {"text":"Wax","presetParams":{"favorites_type":"audio","favorites_url":"qobuz://91.flac"}},
                {"text":"Description","type":"actions"},
                {"text":"Wane","goAction":"play","presetParams":{"favorites_type":"audio","favorites_url":"qobuz://12.mp3"}}]}
                """,
            _ => throw new InvalidOperationException("Unexpected navigation.")
        } };
        using var services = CreateServices(handler);
        var client = services.GetRequiredService<IQobuzClient>();
        var token = TestContext.Current.CancellationToken;
        var album = Assert.Single(await client.ReadReleasesAsync(token));

        var tracks = await client.ReadTracksAsync(album, token);

        Assert.Equal(["91", "12"], tracks.Select(track => track.Id));
        Assert.Equal("Wane", tracks[1].Title);
        Assert.DoesNotContain("playlist", handler.Commands);
    }

    private static ServiceProvider CreateServices(StubLms handler)
    {
        var services = new ServiceCollection();
        services.AddLyrionVoiceMcpLms(LmsConnectionSettings.FromValues("fiction", "http://lms.example.test", null), "1.0");
        services.AddHttpClient<LmsJsonRpcClient>().ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider();
    }

    private sealed class StubLms : HttpMessageHandler
    {
        public string Apps { get; init; } = """{"count":1,"appss_loop":[{"cmd":"qobuz"}]}""";
        public string Players { get; init; } = """
            {"count":3,"players_loop":[{"playerid":"z","connected":1},{"playerid":"a","connected":0},
            {"playerid":"fiction-player","connected":"1"}]}
            """;
        public Func<string[], string>? Browse { get; init; }
        public string? Menu { get; init; }
        public List<string> Commands { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var parameters = json.RootElement.GetProperty("params");
            var command = parameters[1].EnumerateArray().Select(part => part.ToString()).ToArray();
            Commands.Add(command[0]);
            var result = command[0] switch
            {
                "apps" => Apps,
                "radios" => """{"count":0}""",
                "players" => Players,
                "getstring" => """{"PLUGIN_QOBUZ_NEW_RELEASES":"Nouveautés"}""",
                "qobuz" when Browse is not null => Browse(command),
                "qobuz" => Menu,
                _ => throw new InvalidOperationException("Unexpected LMS command.")
            };
            if (command[0] == "qobuz" && Browse is null)
            {
                Assert.Equal("fiction-player", parameters[0].GetString());
                Assert.Equal(new[] { "qobuz", "items", "0", "50", "menu:1" }, command);
            }

            if (result is null) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""{"id":1,"result":{{result}}}""", Encoding.UTF8, "application/json")
            };
        }
    }
}
