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

    [Fact]
    public async Task StationsShouldUseLiveAudioForStableIdentityAndPreservePluginOrder()
    {
        using var fixture = new Fixture();

        var stations = await fixture.Client.ReadStationsAsync(TestContext.Current.CancellationToken);
        var menu = await fixture.Client.BrowseStationAsync("stationa", TestContext.Current.CancellationToken);

        Assert.Equal(new BbcStation("stationa", "Fictional Radio", "sounds://_LIVE_stationa"),
            Assert.Single(stations.Stations));
        Assert.Equal(["Listen Live", "Recent programmes", "Full 30 Day Schedule"],
            menu.Select(item => item.Title));
        Assert.Equal(BbcAudioKind.Station, menu[0].MediaTarget!.Kind);
        Assert.Equal(BbcStationMenuLevel.Programmes, menu[1].BrowseTarget!.Level);
        Assert.Equal(BbcStationMenuLevel.ScheduleDays, menu[2].BrowseTarget!.Level);
    }

    [Fact]
    public async Task MissingStationMenuShouldBeAConfirmedUnavailableSnapshot()
    {
        using var fixture = new Fixture { StationsAvailable = false };

        var stations = await fixture.Client.ReadStationsAsync(TestContext.Current.CancellationToken);

        Assert.False(stations.Available);
        Assert.Empty(stations.Stations);
    }

    [Fact]
    public async Task LiveOnlyStationDiscoveryShouldNotRequireAnAudioBrowsePath()
    {
        using var fixture = new Fixture { StationsAvailable = false, LiveOnlyStations = true };

        var stations = await fixture.Client.ReadStationsAsync(TestContext.Current.CancellationToken);
        var menu = await fixture.Client.BrowseStationAsync("stationa", TestContext.Current.CancellationToken);

        Assert.True(stations.Available);
        Assert.Equal("stationa", Assert.Single(stations.Stations).Id);
        Assert.Equal(BbcAudioKind.Station, Assert.Single(menu).MediaTarget!.Kind);
    }

    [Fact]
    public async Task FallbackProbeFailureShouldNotBecomeConfirmedStationAbsence()
    {
        using var fixture = new Fixture { StationsAvailable = false, StationFallbackFailure = true };

        await Assert.ThrowsAsync<LmsRequestException>(() =>
            fixture.Client.ReadStationsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task StationActionMenuShouldExposeAudioAndSafeNavigationOnly()
    {
        using var fixture = new Fixture();
        var programme = new BbcStationMenuTarget("stationa", "programme-action", BbcStationMenuLevel.PlayableActions);

        var menu = await fixture.Client.BrowseStationMenuAsync(programme, TestContext.Current.CancellationToken);

        Assert.Equal(["Play programme", "All Episodes", "Tracklist"], menu.Select(item => item.Title));
        Assert.DoesNotContain(menu, item => item.Title is "Bookmark" or "Subscribe" or "Remove Continue Listening" or "Synopsis");
        Assert.DoesNotContain(fixture.Commands, command => command.Contains("account-action"));
    }

    [Fact]
    public async Task LiveStationPlaybackShouldRevalidateAndConfirmTheQueueMutation()
    {
        using var fixture = new Fixture();
        var target = new BbcAudioTarget(BbcAudioKind.Station, "stationa", "sounds://_LIVE_stationa");

        await fixture.Client.SubmitAsync(
            "fiction-player", target, ProviderPlaybackCommand.Load, TestContext.Current.CancellationToken);

        Assert.Equal("sounds://_LIVE_stationa", Assert.Single(fixture.Queue));
        Assert.Single(fixture.Commands, command => command[0] == "playlist");
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
        public bool StationsAvailable { get; init; } = true;
        public bool LiveOnlyStations { get; init; }
        public bool StationFallbackFailure { get; init; }
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
                if (StationFallbackFailure && path == "8")
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                object show = new { text = "The Mira Vale Show\nA fictional description", type = "link", presetParams = new { favorites_url = "soundslist://_CONTAINER_showa" }, @params = new { item_id = "8.5.3", touchToPlay = 1 } };
                object[] items = path switch
                {
                    null when StationsAvailable => [new { text = "My Sounds", type = "link", presetParams = new { favorites_url = "soundslist://_MYSOUNDS" }, @params = new { item_id = "8" } },
                        Link("Stations & Schedules", "stations")],
                    null when LiveOnlyStations => [new { text = "My Sounds", type = "link", presetParams = new { favorites_url = "soundslist://_MYSOUNDS" }, @params = new { item_id = "8" } },
                        Link("Listen live", "live-only")],
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
                    "stations" => [Link("Fictional Radio", "stationa")],
                    "live-only" => [Audio("Fictional Radio", "sounds://_LIVE_stationa")],
                    "stationa" => [Audio("Listen Live", "sounds://_LIVE_stationa"),
                        Link("Recent programmes", "recent"), Link("Full 30 Day Schedule", "schedule")],
                    "recent" => [Link("The Paper Harbour", "programme-action")],
                    "schedule" => [Link("Fictional Monday", "schedule-day")],
                    "schedule-day" => [Link("The Paper Harbour", "programme-action")],
                    "programme-action" => [Audio("Play programme", "sounds://_versionb_episodeb"),
                        Link("All Episodes", "all-episodes"), Link("Tracklist", "tracklist"),
                        Link("Synopsis", "synopsis"), Link("Bookmark", "account-action"),
                        Link("Subscribe", "account-action"), Link("Remove Continue Listening", "account-action")],
                    "all-episodes" => [Audio("Earlier programme", "sounds://_versionc_episodec")],
                    "tracklist" => [Audio("Earlier segment", "sounds://_REWIND_123_LIVE_stationa")],
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
        private static object Audio(string title, string url) => new { text = title, type = "audio", presetParams = new { favorites_url = url } };
        private static object Show(string id, string title) => new { text = title, type = "link",
            presetParams = new { favorites_url = $"soundslist://_CONTAINER_{id}" }, @params = new { item_id = $"fictional-{id}" } };
        protected override void Dispose(bool disposing) { if (disposing) http.Dispose(); base.Dispose(disposing); }
    }
}
