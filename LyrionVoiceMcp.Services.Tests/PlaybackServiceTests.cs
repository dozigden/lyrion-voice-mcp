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
            PlaybackQueueMode.Replace,
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
    public async Task AppendToOffPlayerShouldPreflightQueuePowerOnAddAndStartFirstNewItem()
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
        var playbackClient = new StubPlaybackClient { QueueCount = 6 };
        var service = new PlaybackService(playerClient, playbackClient, codec);

        // Act
        var outcome = await service.PlayAsync(
            PlayerId,
            identities.Select((identity, index) => Reference(codec, identity, index)).ToArray(),
            PlaybackQueueMode.Append,
            TestContext.Current.CancellationToken);

        // Assert
        var result = Assert.IsType<PlaybackSucceeded>(outcome);
        Assert.True(result.Player.PoweredOn);
        Assert.Equal(
            [
                "check:Artist:41",
                "check:Album:42",
                "queue-count",
                "power-on",
                "add:Artist:41",
                "add:Album:42",
                "start:6"
            ],
            playbackClient.Operations);
    }

    [Fact]
    public async Task AppendToPlayingPlayerShouldNotInterruptPlayback()
    {
        // Arrange
        var codec = new SearchResultReferenceCodec();
        var identity = new MediaIdentity(MediaEntityKind.Track, "51");
        var playerClient = new StubPlayerClient(
            Player(true, PlayerPlaybackState.Playing),
            Player(true, PlayerPlaybackState.Playing));
        var playbackClient = new StubPlaybackClient();
        var service = new PlaybackService(playerClient, playbackClient, codec);

        // Act
        await service.PlayAsync(
            PlayerId,
            [Reference(codec, identity, 0)],
            PlaybackQueueMode.Append,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["check:Track:51", "add:Track:51"], playbackClient.Operations);
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
            codec,
            store,
            TimeProvider.System,
            NullLogger<PlaybackService>.Instance);

        // Act
        await service.PlayAsync(
            PlayerId,
            [Reference(codec, new MediaIdentity(MediaEntityKind.Track, "51"), 0)],
            PlaybackQueueMode.Replace,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["00000000000000000000000000000001"], store.SelectedCorrelationIds);
    }

    [Fact]
    public async Task AppendToPoweredOnStoppedPlayerShouldStartAtFirstNewItemWithoutPowerCommand()
    {
        // Arrange
        var codec = new SearchResultReferenceCodec();
        var identity = new MediaIdentity(MediaEntityKind.Track, "52");
        var playerClient = new StubPlayerClient(
            Player(true, PlayerPlaybackState.Stopped),
            Player(true, PlayerPlaybackState.Playing));
        var playbackClient = new StubPlaybackClient { QueueCount = 3 };
        var service = new PlaybackService(playerClient, playbackClient, codec);

        // Act
        await service.PlayAsync(
            PlayerId,
            [Reference(codec, identity, 0)],
            PlaybackQueueMode.Append,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            ["check:Track:52", "queue-count", "add:Track:52", "start:3"],
            playbackClient.Operations);
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
            PlaybackQueueMode.Replace,
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
            PlaybackQueueMode.Replace,
            TestContext.Current.CancellationToken);

        // Assert
        var rejection = Assert.IsType<PlaybackRejected>(outcome);
        Assert.Equal(PlaybackRejectionReason.EmptyItems, rejection.Reason);
        Assert.Equal(0, playerClient.CallCount);
        Assert.Empty(playbackClient.Operations);
    }

    [Fact]
    public async Task InvalidModeShouldReturnARejectionWithoutCallingLms()
    {
        // Arrange
        var codec = new SearchResultReferenceCodec();
        var playerClient = new StubPlayerClient(Player(true, PlayerPlaybackState.Playing));
        var playbackClient = new StubPlaybackClient();
        var service = new PlaybackService(playerClient, playbackClient, codec);

        // Act
        var outcome = await service.PlayAsync(
            PlayerId,
            [Reference(codec, new MediaIdentity(MediaEntityKind.Track, "59"), 0)],
            (PlaybackQueueMode)99,
            TestContext.Current.CancellationToken);

        // Assert
        var rejection = Assert.IsType<PlaybackRejected>(outcome);
        Assert.Equal(PlaybackRejectionReason.InvalidMode, rejection.Reason);
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
            PlayableById = { ["62"] = false }
        };
        var service = new PlaybackService(playerClient, playbackClient, codec);

        // Act
        var outcome = await service.PlayAsync(
            PlayerId,
            [Reference(codec, first, 0), Reference(codec, second, 1)],
            PlaybackQueueMode.Replace,
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
            PlaybackQueueMode.Replace,
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
            QueueCount = 2,
            PowerOnException = new LmsRequestException("Synthetic power failure.")
        };
        var service = new PlaybackService(playerClient, playbackClient, codec);

        // Act
        var exception = await Assert.ThrowsAsync<LmsRequestException>(() => service.PlayAsync(
            PlayerId,
            [Reference(codec, identity, 0)],
            PlaybackQueueMode.Append,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("Synthetic power failure.", exception.Message);
        Assert.Equal(
            ["check:Track:71", "queue-count", "power-on"],
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

        public Dictionary<string, bool> PlayableById { get; } = [];

        public int QueueCount { get; init; }

        public Exception? PowerOnException { get; init; }

        public Task<bool> HasPlayableItemAsync(
            MediaIdentity identity,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Operations.Add($"check:{identity.Kind}:{identity.Id}");
            return Task.FromResult(
                !PlayableById.TryGetValue(identity.Id, out var playable) || playable);
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
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(PlayerId, playerId);
            Operations.Add("queue-count");
            return Task.FromResult(QueueCount);
        }

        public Task LoadAsync(
            string playerId,
            MediaIdentity identity,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(PlayerId, playerId);
            Operations.Add($"load:{identity.Kind}:{identity.Id}");
            return Task.CompletedTask;
        }

        public Task AddAsync(
            string playerId,
            MediaIdentity identity,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(PlayerId, playerId);
            Operations.Add($"add:{identity.Kind}:{identity.Id}");
            return Task.CompletedTask;
        }

        public Task StartAtAsync(
            string playerId,
            int queueIndex,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(PlayerId, playerId);
            Operations.Add($"start:{queueIndex}");
            return Task.CompletedTask;
        }
    }
}
