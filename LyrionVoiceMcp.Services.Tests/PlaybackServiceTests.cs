using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace LyrionVoiceMcp.Services.Tests;

public sealed class PlaybackServiceTests
{
    private const string PlayerId = "00:11:22:33:44:55";

    [Fact]
    public async Task ReplaceShouldLoadFirstReferenceAndAddLaterReferencesInOrder()
    {
        // Arrange
        var codec = new SearchResultReferenceCodec();
        var identities = new[]
        {
            new MediaIdentity(MediaEntityKind.Track, "31"),
            new MediaIdentity(MediaEntityKind.Album, "32"),
            new MediaIdentity(MediaEntityKind.Playlist, "33")
        };
        var playerClient = new StubPlayerClient(
            Player(true, PlayerPlaybackState.Playing),
            Player(true, PlayerPlaybackState.Playing));
        var playbackClient = new StubPlaybackClient();
        var service = new PlaybackService(playerClient, playbackClient, codec);

        // Act
        var outcome = await service.PlayAsync(
            PlayerId,
            identities.Select((identity, index) => Reference(codec, identity, index)).ToArray(),
            TestContext.Current.CancellationToken);

        // Assert
        var result = Assert.IsType<PlaybackSucceeded>(outcome);
        Assert.Equal(PlayerPlaybackState.Playing, result.Player.PlaybackState);
        Assert.Equal(
            [
                "check:Track:31",
                "check:Album:32",
                "check:Playlist:33",
                "load:Track:31",
                "add:Album:32",
                "add:Playlist:33"
            ],
            playbackClient.Operations);
    }

    [Fact]
    public async Task OffPlayerShouldPowerOnBeforeReplacingTheQueue()
    {
        // Arrange
        var codec = new SearchResultReferenceCodec();
        var identities = new[]
        {
            new MediaIdentity(MediaEntityKind.Artist, "41"),
            new MediaIdentity(MediaEntityKind.Album, "42")
        };
        var playerClient = new StubPlayerClient(
            Player(false, PlayerPlaybackState.Stopped),
            Player(true, PlayerPlaybackState.Playing));
        var playbackClient = new StubPlaybackClient();
        var service = new PlaybackService(playerClient, playbackClient, codec);

        // Act
        var outcome = await service.PlayAsync(
            PlayerId,
            identities.Select((identity, index) => Reference(codec, identity, index)).ToArray(),
            TestContext.Current.CancellationToken);

        // Assert
        var result = Assert.IsType<PlaybackSucceeded>(outcome);
        Assert.True(result.Player.PoweredOn);
        Assert.Equal(
            [
                "check:Artist:41",
                "check:Album:42",
                "power-on",
                "load:Artist:41",
                "add:Album:42"
            ],
            playbackClient.Operations);
    }

    [Fact]
    public async Task SuccessfulPlaybackShouldMarkItsSearchCorrelationSelected()
    {
        // Arrange
        var codec = new SearchResultReferenceCodec();
        var store = new RecordingSearchObservationStore();
        var service = new PlaybackService(
            new StubPlayerClient(Player(true, PlayerPlaybackState.Playing), Player(true, PlayerPlaybackState.Playing)),
            new StubPlaybackClient(),
            new PlayableReferenceResolver(codec, new BrowseReferenceCodec()),
            store,
            TimeProvider.System,
            NullLogger<PlaybackService>.Instance);

        // Act
        await service.PlayAsync(
            PlayerId,
            [Reference(codec, new MediaIdentity(MediaEntityKind.Track, "51"), 0)],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["00000000000000000000000000000001"], store.SelectedCorrelationIds);
    }

    [Fact]
    public async Task BrowseReferenceShouldPlayWithoutInventingASearchSelection()
    {
        // Arrange
        var searchCodec = new SearchResultReferenceCodec();
        var browseCodec = new BrowseReferenceCodec();
        var store = new RecordingSearchObservationStore();
        var playbackClient = new StubPlaybackClient();
        var service = new PlaybackService(
            new StubPlayerClient(
                Player(true, PlayerPlaybackState.Stopped),
                Player(true, PlayerPlaybackState.Playing)),
            playbackClient,
            new PlayableReferenceResolver(searchCodec, browseCodec),
            store,
            TimeProvider.System,
            NullLogger<PlaybackService>.Instance);
        var reference = browseCodec.Encode(new BrowseReferenceValue(
            new BrowseTarget(LmsBrowseQueryKind.AlbumTracks, "52", 0),
            new PlayableMedia(new MediaIdentity(MediaEntityKind.Album, "52"))));

        // Act
        var outcome = await service.PlayAsync(
            PlayerId,
            [reference],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<PlaybackSucceeded>(outcome);
        Assert.Equal(["check:Album:52", "load:Album:52"], playbackClient.Operations);
        Assert.Null(store.SelectedCorrelationIds);
    }

    [Fact]
    public async Task SearchDerivedBrowseReferenceShouldMarkTheOriginalSearchSelection()
    {
        // Arrange
        var searchCodec = new SearchResultReferenceCodec();
        var browseCodec = new BrowseReferenceCodec();
        var store = new RecordingSearchObservationStore();
        var service = new PlaybackService(
            new StubPlayerClient(
                Player(true, PlayerPlaybackState.Stopped),
                Player(true, PlayerPlaybackState.Playing)),
            new StubPlaybackClient(),
            new PlayableReferenceResolver(searchCodec, browseCodec),
            store,
            TimeProvider.System,
            NullLogger<PlaybackService>.Instance);
        var correlationId = "123456781234123412341234567890ab";
        var reference = browseCodec.Encode(new BrowseReferenceValue(
            new BrowseTarget(LmsBrowseQueryKind.AlbumTracks, "52", 0),
            new PlayableMedia(new MediaIdentity(MediaEntityKind.Album, "52")),
            correlationId));

        // Act
        var outcome = await service.PlayAsync(
            PlayerId,
            [reference],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<PlaybackSucceeded>(outcome);
        Assert.Equal([correlationId], store.SelectedCorrelationIds);
    }

    [Fact]
    public async Task InvalidReferenceShouldFailBeforeAnyLmsCall()
    {
        // Arrange
        var playerClient = new StubPlayerClient(Player(true, PlayerPlaybackState.Playing));
        var playbackClient = new StubPlaybackClient();
        var service = new PlaybackService(
            playerClient,
            playbackClient,
            new SearchResultReferenceCodec());

        // Act
        var outcome = await service.PlayAsync(
            PlayerId,
            ["not-a-result-reference"],
            TestContext.Current.CancellationToken);

        // Assert
        var rejection = Assert.IsType<PlaybackRejected>(outcome);
        Assert.Equal(PlaybackRejectionReason.InvalidReference, rejection.Reason);
        Assert.Equal(0, playerClient.CallCount);
        Assert.Empty(playbackClient.Operations);
    }

    [Fact]
    public async Task EmptyItemsShouldReturnARejectionWithoutCallingLms()
    {
        // Arrange
        var playerClient = new StubPlayerClient(Player(true, PlayerPlaybackState.Playing));
        var playbackClient = new StubPlaybackClient();
        var service = new PlaybackService(
            playerClient,
            playbackClient,
            new SearchResultReferenceCodec());

        // Act
        var outcome = await service.PlayAsync(
            PlayerId,
            [],
            TestContext.Current.CancellationToken);

        // Assert
        var rejection = Assert.IsType<PlaybackRejected>(outcome);
        Assert.Equal(PlaybackRejectionReason.EmptyItems, rejection.Reason);
        Assert.Equal(0, playerClient.CallCount);
        Assert.Empty(playbackClient.Operations);
    }

    [Fact]
    public async Task MissingMediaShouldFailAfterAllChecksWithoutMutation()
    {
        // Arrange
        var codec = new SearchResultReferenceCodec();
        var first = new MediaIdentity(MediaEntityKind.Track, "61");
        var second = new MediaIdentity(MediaEntityKind.Album, "62");
        var playerClient = new StubPlayerClient(Player(false, PlayerPlaybackState.Stopped));
        var playbackClient = new StubPlaybackClient
        {
            PlayableCountById = { ["62"] = 0 }
        };
        var service = new PlaybackService(playerClient, playbackClient, codec);

        // Act
        var outcome = await service.PlayAsync(
            PlayerId,
            [Reference(codec, first, 0), Reference(codec, second, 1)],
            TestContext.Current.CancellationToken);

        // Assert
        var rejection = Assert.IsType<PlaybackRejected>(outcome);
        Assert.Equal(PlaybackRejectionReason.MediaNotFound, rejection.Reason);
        Assert.Contains("item 2", rejection.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["check:Track:61", "check:Album:62"], playbackClient.Operations);
    }

    [Fact]
    public async Task MissingPlayerShouldFailBeforePowerOrQueueMutation()
    {
        // Arrange
        var codec = new SearchResultReferenceCodec();
        var identity = new MediaIdentity(MediaEntityKind.Track, "63");
        var playerClient = new StubPlayerClient(new LmsPlayerStatus(
            "66:77:88:99:aa:bb",
            "South Room",
            false,
            PlayerPlaybackState.Stopped));
        var playbackClient = new StubPlaybackClient();
        var service = new PlaybackService(playerClient, playbackClient, codec);

        // Act
        var outcome = await service.PlayAsync(
            PlayerId,
            [Reference(codec, identity, 0)],
            TestContext.Current.CancellationToken);

        // Assert
        var rejection = Assert.IsType<PlaybackRejected>(outcome);
        Assert.Equal(PlaybackRejectionReason.PlayerNotFound, rejection.Reason);
        Assert.Equal(["check:Track:63"], playbackClient.Operations);
    }

    [Fact]
    public async Task PowerFailureShouldNotMutateQueue()
    {
        // Arrange
        var codec = new SearchResultReferenceCodec();
        var identity = new MediaIdentity(MediaEntityKind.Track, "71");
        var playerClient = new StubPlayerClient(Player(false, PlayerPlaybackState.Stopped));
        var playbackClient = new StubPlaybackClient
        {
            PowerOnException = new LmsRequestException("Synthetic power failure.")
        };
        var service = new PlaybackService(playerClient, playbackClient, codec);

        // Act
        var exception = await Assert.ThrowsAsync<LmsRequestException>(() => service.PlayAsync(
            PlayerId,
            [Reference(codec, identity, 0)],
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("Synthetic power failure.", exception.Message);
        Assert.Equal(
            ["check:Track:71", "power-on"],
            playbackClient.Operations);
    }

    private static LmsPlayerStatus Player(
        bool poweredOn,
        PlayerPlaybackState playbackState) =>
        new(PlayerId, "North Room", poweredOn, playbackState);

    private static string Reference(
        ISearchResultReferenceCodec codec,
        MediaIdentity identity,
        int occurrence) =>
        codec.Encode(new SearchResultReferenceValue(
            $"{occurrence + 1:x32}",
            identity));

    private sealed class StubPlayerClient(params LmsPlayerStatus[] results)
        : ILmsPlayerClient
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<LmsPlayerStatus>> GetPlayersAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = results[Math.Min(CallCount, results.Length - 1)];
            CallCount++;
            return Task.FromResult<IReadOnlyList<LmsPlayerStatus>>([result]);
        }
    }

    private sealed class StubPlaybackClient : ILmsPlaybackClient
    {
        public List<string> Operations { get; } = [];

        public Dictionary<string, int> PlayableCountById { get; } = [];

        public Exception? PowerOnException { get; init; }

        public Task<int> GetPlayableItemCountAsync(
            PlayableMedia media,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Operations.Add($"check:{Describe(media)}");
            return Task.FromResult(
                PlayableCountById.TryGetValue(media.Identity.Id, out var count) ? count : 1);
        }

        public Task PowerOnAsync(
            string playerId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(PlayerId, playerId);
            Operations.Add("power-on");
            return PowerOnException is null
                ? Task.CompletedTask
                : Task.FromException(PowerOnException);
        }

        public Task<int> GetQueueCountAsync(
            string playerId,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Playback must not inspect the existing queue.");

        public Task LoadAsync(
            string playerId,
            PlayableMedia media,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(PlayerId, playerId);
            Operations.Add($"load:{Describe(media)}");
            return Task.CompletedTask;
        }

        public Task AddAsync(
            string playerId,
            PlayableMedia media,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(PlayerId, playerId);
            Operations.Add($"add:{Describe(media)}");
            return Task.CompletedTask;
        }

        public Task InsertAsync(
            string playerId,
            PlayableMedia media,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(PlayerId, playerId);
            Operations.Add($"insert:{Describe(media)}");
            return Task.CompletedTask;
        }

        public Task ClearAsync(
            string playerId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(PlayerId, playerId);
            Operations.Add("clear");
            return Task.CompletedTask;
        }

        private static string Describe(PlayableMedia media) =>
            media.ContributorRole is { } role
                ? $"{media.Identity.Kind}:{media.Identity.Id}:{role}"
                : $"{media.Identity.Kind}:{media.Identity.Id}";

    }
}
