using System.Net;
using System.Text;
using System.Text.Json;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Abstractions.Providers;
using LyrionVoiceMcp.Abstractions.Providers.BbcSounds;
using LyrionVoiceMcp.Lms.Providers.BbcSounds;

namespace LyrionVoiceMcp.Lms.Tests.Providers.BbcSounds;

public sealed class BbcSoundsClientTests
{
    [Fact]
    public async Task MissingPluginShouldNotReadPlayersOrMenus()
    {
        using var fixture = new Fixture { Installed = false };
        var result = await fixture.Client.ReadSubscriptionsAsync(TestContext.Current.CancellationToken);
        Assert.False(result.Available);
        Assert.Equal(new[] { "apps", "radios" }, fixture.Commands.Select(x => x[0]));
    }

    [Fact]
    public async Task SubscriptionsShouldUseReturnedPathsAndStableProgrammeIdentity()
    {
        using var fixture = new Fixture();
        var result = await fixture.Client.ReadSubscriptionsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(new BbcShow("showa", "The Mira Vale Show"), Assert.Single(result.Shows));
        Assert.DoesNotContain(fixture.Commands, x => x.Contains("touchToPlay:1"));
        Assert.Contains(fixture.Commands, x => x.Contains("item_id:8.5"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task InconsistentDiscoveryMustNotBeTreatedAsConfirmedAbsence(int count)
    {
        using var fixture = new Fixture { DiscoveryCount = count };

        await Assert.ThrowsAsync<LmsRequestException>(() =>
            fixture.Client.ReadSubscriptionsAsync(TestContext.Current.CancellationToken));

        Assert.DoesNotContain(fixture.Commands, x => x[0] == "players" || x[0] == "bbcsounds");
    }

    [Fact]
    public async Task BrowseShouldFlattenAudioWithoutFollowingAccountActions()
    {
        using var fixture = new Fixture();
        var page = await fixture.Client.BrowseEpisodesAsync("showa", 0, TestContext.Current.CancellationToken);
        Assert.Equal(new BbcEpisode("episodea", "Fictional episode", "sounds://_versiona_episodea"), Assert.Single(page.Episodes));
        Assert.Null(page.NextOffset);
        Assert.DoesNotContain(fixture.Commands, x => x.Contains("item_id:account-action") || x[0] == "playlist");
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task UnreadableOrDuplicateSubscriptionsShouldFailInsteadOfBecomingEmpty(bool noPlayer, bool duplicate)
    {
        using var fixture = new Fixture { NoPlayer = noPlayer, DuplicateShow = duplicate };
        await Assert.ThrowsAsync<LmsRequestException>(() => fixture.Client.ReadSubscriptionsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MissingEpisodeShouldBeUnavailableWithoutMutation()
    {
        using var fixture = new Fixture();
        Assert.False(await fixture.Client.IsAvailableAsync(new BbcEpisodeTarget("showa", "missing", "sounds://_versionb_missing"),
            TestContext.Current.CancellationToken));
        Assert.DoesNotContain(fixture.Commands, x => x[0] == "playlist");
    }

    [Theory]
    [InlineData(ProviderPlaybackCommand.Load)]
    [InlineData(ProviderPlaybackCommand.Add)]
    [InlineData(ProviderPlaybackCommand.Insert)]
    public async Task PlaybackShouldSubmitOnlyTheResolvedEpisodeAndConfirmTheNewOccurrence(ProviderPlaybackCommand command)
    {
        using var fixture = new Fixture();
        fixture.Queue.Add("sounds://_old_existing");

        await fixture.Client.SubmitAsync("fiction-player", new BbcEpisodeTarget("showa", "episodea", "sounds://_versiona_episodea"),
            command, TestContext.Current.CancellationToken);

        Assert.Equal(command == ProviderPlaybackCommand.Load ? 1 : 2, fixture.Queue.Count);
        Assert.Contains("sounds://_versiona_episodea", fixture.Queue);
        var mutation = Assert.Single(fixture.Commands, x => x[0] == "playlist");
        Assert.Equal("sounds://_versiona_episodea", mutation[2]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ExistingEpisodeMustNotMakeAnUnconfirmedAdditionLookSuccessful(int shuffleMode)
    {
        using var fixture = new Fixture { IgnoreMutation = true, ShuffleMode = shuffleMode };
        fixture.Queue.Add("sounds://_versiona_episodea");

        await Assert.ThrowsAsync<LmsRequestException>(() => fixture.Client.SubmitAsync("fiction-player",
            new BbcEpisodeTarget("showa", "episodea", "sounds://_versiona_episodea"), ProviderPlaybackCommand.Add,
            TestContext.Current.CancellationToken));

        Assert.Single(fixture.Commands, x => x[0] == "playlist");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ShuffledAppendShouldConfirmAnAdditionalEpisodeAwayFromTheEnd(int shuffleMode)
    {
        using var fixture = new Fixture { ShuffleMode = shuffleMode };
        fixture.Queue.AddRange(["sounds://_olda_existinga", "sounds://_oldversion_episodea", "sounds://_oldb_existingb"]);

        await fixture.Client.SubmitAsync("fiction-player", new BbcEpisodeTarget("showa", "episodea", "sounds://_versiona_episodea"),
            ProviderPlaybackCommand.Add, TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "sounds://_olda_existinga", "sounds://_versiona_episodea", "sounds://_oldversion_episodea", "sounds://_oldb_existingb" }, fixture.Queue);
        Assert.Single(fixture.Commands, x => x[0] == "playlist");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ShuffledQueueGrowthMustNotConfirmAnExistingEpisodeWhenAnotherWasAdded(int shuffleMode)
    {
        using var fixture = new Fixture { ShuffleMode = shuffleMode, SubmittedAudioUrl = "sounds://_other_wrong" };
        fixture.Queue.AddRange(["sounds://_olda_existinga", "sounds://_oldversion_episodea"]);

        await Assert.ThrowsAsync<LmsRequestException>(() => fixture.Client.SubmitAsync("fiction-player",
            new BbcEpisodeTarget("showa", "episodea", "sounds://_versiona_episodea"), ProviderPlaybackCommand.Add,
            TestContext.Current.CancellationToken));

        Assert.Equal(3, fixture.Queue.Count);
        Assert.Single(fixture.Commands, x => x[0] == "playlist");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SubscriptionCollectionShouldFollowProviderContinuationLinks(bool thirdPage)
    {
        using var fixture = new Fixture { MoreShows = true,
            FirstContinuationTitle = thirdPage ? "Next - 1 to 2 of 3" : "Next - 1 to 2 of 2",
            SecondContinuationTitle = thirdPage ? "Next - 2 to 3 of 3" : null };
        var result = await fixture.Client.ReadSubscriptionsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(thirdPage ? ["showa", "showb", "showc"] : new[] { "showa", "showb" }, result.Shows.Select(x => x.Id));
    }

    [Theory]
    [InlineData("Next - 1 to 2 of 2", null, true)]
    [InlineData("Next - 1 to 3 of 3", null, false)]
    [InlineData("Next - 1 to 2 of 3", null, false)]
    [InlineData("Next - 2 to 3 of 3", null, false)]
    [InlineData("Next - 1 to 1 of 2", null, false)]
    [InlineData("Next - 1 to 3 of 2", null, false)]
    [InlineData("Next - 1 to 2 of 3", "Next - 2 to 3 of 4", false)]
    [InlineData("Next - 1 to 2 of 21474836480", null, false)]
    public async Task IncompleteOrInconsistentContinuationMustRejectTheSubscriptionRead(
        string firstTitle, string? secondTitle, bool emptyContinuation)
    {
        using var fixture = new Fixture { MoreShows = true, FirstContinuationTitle = firstTitle,
            SecondContinuationTitle = secondTitle, EmptyContinuation = emptyContinuation };

        await Assert.ThrowsAsync<LmsRequestException>(() =>
            fixture.Client.ReadSubscriptionsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnEmptyFirstPageShouldStillConfirmEmptySubscriptions()
    {
        using var fixture = new Fixture { EmptySubscriptions = true };

        var result = await fixture.Client.ReadSubscriptionsAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Available);
        Assert.Empty(result.Shows);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-2)]
    [InlineData(1)]
    public async Task InsertShouldRejectAnUnknownQueuePositionBeforeMutation(int? currentIndex)
    {
        using var fixture = new Fixture { CurrentIndex = currentIndex };
        fixture.Queue.Add("sounds://_old_existing");

        await Assert.ThrowsAsync<LmsRequestException>(() => fixture.Client.SubmitAsync("fiction-player",
            new BbcEpisodeTarget("showa", "episodea", "sounds://_versiona_episodea"), ProviderPlaybackCommand.Insert,
            TestContext.Current.CancellationToken));

        Assert.DoesNotContain(fixture.Commands, x => x[0] == "playlist");
    }

    private sealed class Fixture : HttpMessageHandler
    {
        public bool Installed { get; init; } = true;
        public int? DiscoveryCount { get; init; }
        public bool NoPlayer { get; init; }
        public bool DuplicateShow { get; init; }
        public bool MoreShows { get; init; }
        public string FirstContinuationTitle { get; init; } = "Next - 1 to 2 of 2";
        public string? SecondContinuationTitle { get; init; }
        public bool EmptyContinuation { get; init; }
        public bool EmptySubscriptions { get; init; }
        public bool IgnoreMutation { get; init; }
        public int ShuffleMode { get; init; }
        public string? SubmittedAudioUrl { get; init; }
        public int? CurrentIndex { get; init; } = 0;
        public List<string> Queue { get; } = [];
        public List<string[]> Commands { get; } = [];
        public BbcSoundsClient Client { get; }
        private readonly HttpClient http;
        public Fixture()
        {
            http = new HttpClient(this, disposeHandler: false);
            var rpc = new LmsJsonRpcClient(new LmsConnectionSettings("fiction", new Uri("http://lms.example.test"), TimeSpan.FromSeconds(5)), http);
            Client = new BbcSoundsClient(new BbcSoundsMenuClient(rpc), rpc);
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => RespondAsync(request, cancellationToken);
        private async Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var cmd = json.RootElement.GetProperty("params")[1].EnumerateArray().Select(x => x.ToString()).ToArray();
            Commands.Add(cmd);
            object result;
            if (cmd[0] == "apps" && DiscoveryCount is { } discoveryCount)
                result = new { count = discoveryCount, appss_loop = cmd[1] == "0" ? new[] { new { cmd = "fictional-app" } } : [] };
            else if (cmd[0] == "apps") result = new { count = Installed ? 1 : 0, appss_loop = Installed ? new[] { new { cmd = "bbcsounds" } } : [] };
            else if (cmd[0] == "radios") result = new { count = 0 };
            else if (cmd[0] == "players") result = new { count = 1, players_loop = new[] { new { playerid = "fiction-player", connected = NoPlayer ? 0 : 1 } } };
            else if (cmd[0] == "status") result = new Dictionary<string, object?>
            {
                ["playlist_tracks"] = Queue.Count, ["playlist_cur_index"] = CurrentIndex,
                ["playlist shuffle"] = ShuffleMode, ["playlist_loop"] = Queue.Select(url => new { url }).ToArray()
            };
            else if (cmd[0] == "playlist")
            {
                if (!IgnoreMutation)
                {
                    if (cmd[1] == "play") { Queue.Clear(); Queue.Add(cmd[2]); }
                    else if (cmd[1] == "add" && ShuffleMode > 0) Queue.Insert(Math.Min(1, Queue.Count), SubmittedAudioUrl ?? cmd[2]);
                    else if (cmd[1] == "add") Queue.Add(SubmittedAudioUrl ?? cmd[2]);
                    else if (cmd[1] == "insert") Queue.Insert(Math.Min(1, Queue.Count), cmd[2]);
                    else throw new InvalidOperationException("Unexpected mutation");
                }
                result = new { };
            }
            else if (cmd[0] == "bbcsounds")
            {
                var path = cmd.FirstOrDefault(x => x.StartsWith("item_id:", StringComparison.Ordinal))?[8..];
                object show = new { text = "The Mira Vale Show\nA fictional description", type = "link", presetParams = new { favorites_url = "soundslist://_CONTAINER_showa" }, @params = new { item_id = "8.5.3", touchToPlay = 1 } };
                object[] items = path switch
                {
                    null => [new { text = "My Sounds", type = "link", presetParams = new { favorites_url = "soundslist://_MYSOUNDS" }, @params = new { item_id = "8" } }],
                    "8" => [Link("Subscribed", "8.5")],
                    "8.5" when DuplicateShow => [show, show],
                    "8.5" when EmptySubscriptions => [],
                    "8.5" when MoreShows => [show, Link(FirstContinuationTitle, "next-page")],
                    "8.5" => [show],
                    "next-page" when EmptyContinuation => [],
                    "next-page" when SecondContinuationTitle is not null => [Show("showb", "The Orchard Hour"), Link(SecondContinuationTitle, "last-page")],
                    "next-page" => [Show("showb", "The Orchard Hour")],
                    "last-page" => [Show("showc", "The Lantern Programme")],
                    "8.5.3" => [Link("Fictional episode", "8.5.3.0")],
                    "8.5.3.0" => [new { text = "Play", type = "audio", presetParams = new { favorites_url = "sounds://_versiona_episodea" } }, Link("Bookmark", "account-action")],
                    _ => throw new InvalidOperationException("Unexpected menu navigation")
                };
                result = new { count = items.Length, item_loop = items };
            }
            else throw new InvalidOperationException("Unexpected command");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { result }), Encoding.UTF8, "application/json") };
        }
        private static object Link(string title, string path) => new { text = title, type = "link", actions = new { go = new { cmd = new[] { "bbcsounds", "items" }, @params = new { item_id = path } } } };
        private static object Show(string id, string title) => new { text = title, type = "link",
            presetParams = new { favorites_url = $"soundslist://_CONTAINER_{id}" }, @params = new { item_id = $"fictional-{id}" } };
        protected override void Dispose(bool disposing) { if (disposing) http.Dispose(); base.Dispose(disposing); }
    }
}
