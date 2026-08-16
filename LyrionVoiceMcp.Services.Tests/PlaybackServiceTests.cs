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
        var codecs = new ReferenceCodecTestContext();
        var codec = codecs.Search;
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
        var service = CreateService(playerClient, playbackClient, codecs);

        // Act
        var outcome = await service.PlayAsync(
            PlayerId,
            identities.Select((identity, index) => Reference(codec, identity, index)).ToArray(),
            TestContext.Current.CancellationToken);

        // Assert
        var result = Assert.IsType<PlaybackSucceeded>(outcome);
        var player = Assert.IsType<LmsPlayerStatus>(result.Player);
        Assert.Equal(PlayerPlaybackState.Playing, player.PlaybackState);
        Assert.Equal(3, result.RequestedItemCount);
        Assert.Equal(3, result.CompletedItemCount);
        Assert.Empty(result.SkippedItems);
        Assert.Null(result.StateRefreshError);
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
    public async Task TenShortHandlesShouldRemainResolvableInOnePlaybackRequest()
    {
        // Arrange
        var codecs = new ReferenceCodecTestContext();
        var codec = codecs.Search;
        var references = Enumerable.Range(1, 10)
            .Select(index => Reference(
                codec,
                new MediaIdentity(MediaEntityKind.Track, index.ToString()),
                index))
            .ToArray();
        var playbackClient = new StubPlaybackClient();
        var service = CreateService(
            new StubPlayerClient(
                Player(true, PlayerPlaybackState.Stopped),
                Player(true, PlayerPlaybackState.Playing)),
            playbackClient,
            codecs);

        // Act
        var outcome = await service.PlayAsync(
            PlayerId,
            references,
            TestContext.Current.CancellationToken);

        // Assert
        var succeeded = Assert.IsType<PlaybackSucceeded>(outcome);
        Assert.Equal(10, succeeded.RequestedItemCount);
        Assert.Equal(10, succeeded.CompletedItemCount);
        Assert.Empty(succeeded.SkippedItems);
        Assert.All(references, reference => Assert.Equal(23, reference.Length));
        Assert.Equal(10, playbackClient.Operations.Count(operation =>
            operation.StartsWith("check:", StringComparison.Ordinal)));
        Assert.Equal(1, playbackClient.Operations.Count(operation =>
            operation.StartsWith("load:", StringComparison.Ordinal)));
        Assert.Equal(9, playbackClient.Operations.Count(operation =>
            operation.StartsWith("add:", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task OneInvalidItemInTenShouldNotPreventTheOtherNinePlaying()
    {
        // Arrange
        var codecs = new ReferenceCodecTestContext();
        var references = Enumerable.Range(1, 10)
            .Select(index => index == 6
                ? "invalid-reference"
                : Reference(
                    codecs.Search,
                    new MediaIdentity(MediaEntityKind.Track, index.ToString()),
                    index))
            .ToArray();
        var playbackClient = new StubPlaybackClient();
        var service = CreateService(
            new StubPlayerClient(
                Player(true, PlayerPlaybackState.Stopped),
                Player(true, PlayerPlaybackState.Playing)),
            playbackClient,
            codecs);

        // Act
        var outcome = await service.PlayAsync(
            PlayerId,
            references,
            TestContext.Current.CancellationToken);

        // Assert
        var succeeded = Assert.IsType<PlaybackSucceeded>(outcome);
        Assert.Equal(10, succeeded.RequestedItemCount);
        Assert.Equal(9, succeeded.CompletedItemCount);
        var skipped = Assert.Single(succeeded.SkippedItems);
        Assert.Equal(6, skipped.Index);
        Assert.Equal(MediaItemSkipReason.InvalidReference, skipped.Reason);
        Assert.Equal(1, playbackClient.Operations.Count(operation =>
            operation.StartsWith("load:", StringComparison.Ordinal)));
        Assert.Equal(8, playbackClient.Operations.Count(operation =>
            operation.StartsWith("add:", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task InvalidFirstItemShouldBeSkippedAndTheFirstUsableItemLoaded()
    {
        // Arrange
        var codecs = new ReferenceCodecTestContext();
        var playbackClient = new StubPlaybackClient();
        var service = CreateService(
            new StubPlayerClient(
                Player(true, PlayerPlaybackState.Stopped),
                Player(true, PlayerPlaybackState.Playing)),
            playbackClient,
            codecs);
        var second = Reference(
            codecs.Search,
            new MediaIdentity(MediaEntityKind.Track, "12"),
            1);
        var third = Reference(
            codecs.Search,
            new MediaIdentity(MediaEntityKind.Album, "13"),
            2);

        // Act
        var outcome = await service.PlayAsync(
            PlayerId,
            ["invalid-reference", second, third],
            TestContext.Current.CancellationToken);

        // Assert
        var succeeded = Assert.IsType<PlaybackSucceeded>(outcome);
        Assert.Equal(3, succeeded.RequestedItemCount);
        Assert.Equal(2, succeeded.CompletedItemCount);
        var skipped = Assert.Single(succeeded.SkippedItems);
        Assert.Equal(1, skipped.Index);
        Assert.Equal(MediaItemSkipReason.InvalidReference, skipped.Reason);
        Assert.Equal(
            ["check:Track:12", "check:Album:13", "load:Track:12", "add:Album:13"],
            playbackClient.Operations);
    }

    [Fact]
    public async Task AddFailureShouldReturnCompletedAndNotAttemptedItemsAndSelectOnlyCompletedMedia()
    {
        // Arrange
        var codecs = new ReferenceCodecTestContext();
        var store = new RecordingSearchObservationStore();
        var playbackClient = new StubPlaybackClient
        {
            MutationExceptionById =
            {
                ["82"] = new LmsRequestException("Synthetic add failure.")
            }
        };
        var service = CreateService(
            new StubPlayerClient(
                Player(true, PlayerPlaybackState.Stopped),
                Player(true, PlayerPlaybackState.Playing)),
            playbackClient,
            codecs,
            store);
        var references = new[]
        {
            Reference(codecs.Search, new MediaIdentity(MediaEntityKind.Track, "81"), 0),
            Reference(codecs.Search, new MediaIdentity(MediaEntityKind.Track, "82"), 1),
            Reference(codecs.Search, new MediaIdentity(MediaEntityKind.Track, "83"), 2)
        };

        // Act
        var outcome = await service.PlayAsync(
            PlayerId,
            references,
            TestContext.Current.CancellationToken);

        // Assert
        var succeeded = Assert.IsType<PlaybackSucceeded>(outcome);
        Assert.Equal(3, succeeded.RequestedItemCount);
        Assert.Equal(1, succeeded.CompletedItemCount);
        Assert.Collection(
            succeeded.SkippedItems,
            item =>
            {
                Assert.Equal(2, item.Index);
                Assert.Equal(MediaItemSkipReason.LmsError, item.Reason);
            },
            item =>
            {
                Assert.Equal(3, item.Index);
                Assert.Equal(MediaItemSkipReason.NotAttempted, item.Reason);
            });
        Assert.Equal(
            [
                "check:Track:81",
                "check:Track:82",
                "check:Track:83",
                "load:Track:81",
                "add:Track:82"
            ],
            playbackClient.Operations);
        Assert.Equal(
            ["00000000000000000000000000000001"],
            store.SelectedCorrelationIds);
    }

    [Fact]
    public async Task LoadFailureShouldReturnStructuredCurrentStateAndStopTheBatch()
    {
        // Arrange
        var codecs = new ReferenceCodecTestContext();
        var playbackClient = new StubPlaybackClient
        {
            MutationExceptionById =
            {
                ["84"] = new LmsRequestException("Synthetic load failure.")
            }
        };
        var service = CreateService(
            new StubPlayerClient(Player(true, PlayerPlaybackState.Stopped)),
            playbackClient,
            codecs);

        // Act
        var outcome = await service.PlayAsync(
            PlayerId,
            [
                Reference(codecs.Search, new MediaIdentity(MediaEntityKind.Track, "84"), 0),
                Reference(codecs.Search, new MediaIdentity(MediaEntityKind.Track, "85"), 1)
            ],
            TestContext.Current.CancellationToken);

        // Assert
        var failed = Assert.IsType<PlaybackFailed>(outcome);
        Assert.NotNull(failed.Player);
        Assert.Equal(2, failed.RequestedItemCount);
        Assert.Null(failed.StateRefreshError);
        Assert.Contains("Item 1: lms_error", failed.Message, StringComparison.Ordinal);
        Assert.Contains("Item 2: not_attempted", failed.Message, StringComparison.Ordinal);
        Assert.Collection(
            failed.SkippedItems,
            item => Assert.Equal(MediaItemSkipReason.LmsError, item.Reason),
            item => Assert.Equal(MediaItemSkipReason.NotAttempted, item.Reason));
        Assert.Equal(
            ["check:Track:84", "check:Track:85", "load:Track:84"],
            playbackClient.Operations);
    }

    [Fact]
    public async Task PlayerRefreshFailureShouldNotHideCompletedPlayback()
    {
        // Arrange
        var codecs = new ReferenceCodecTestContext();
        var playerClient = new StubPlayerClient(Player(true, PlayerPlaybackState.Stopped))
        {
            ExceptionAfterFirstCall = new LmsRequestException("Synthetic status failure.")
        };
        var service = CreateService(playerClient, new StubPlaybackClient(), codecs);

        // Act
        var outcome = await service.PlayAsync(
            PlayerId,
            [Reference(codecs.Search, new MediaIdentity(MediaEntityKind.Track, "86"), 0)],
            TestContext.Current.CancellationToken);

        // Assert
        var succeeded = Assert.IsType<PlaybackSucceeded>(outcome);
        Assert.Equal(1, succeeded.CompletedItemCount);
        Assert.Null(succeeded.Player);
        Assert.Equal("Synthetic status failure.", succeeded.StateRefreshError);
    }

    [Fact]
    public async Task OffPlayerShouldPowerOnBeforeReplacingTheQueue()
    {
        // Arrange
        var codecs = new ReferenceCodecTestContext();
        var codec = codecs.Search;
        var identities = new[]
        {
            new MediaIdentity(MediaEntityKind.Artist, "41"),
            new MediaIdentity(MediaEntityKind.Album, "42")
        };
        var playerClient = new StubPlayerClient(
            Player(false, PlayerPlaybackState.Stopped),
            Player(true, PlayerPlaybackState.Playing));
        var playbackClient = new StubPlaybackClient();
        var service = CreateService(playerClient, playbackClient, codecs);

        // Act
        var outcome = await service.PlayAsync(
            PlayerId,
            identities.Select((identity, index) => Reference(codec, identity, index)).ToArray(),
            TestContext.Current.CancellationToken);

        // Assert
        var result = Assert.IsType<PlaybackSucceeded>(outcome);
        var player = Assert.IsType<LmsPlayerStatus>(result.Player);
        Assert.True(player.PoweredOn);
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
    public async Task UniquePlayerNameShouldUseTheCanonicalIdForPlayback()
    {
        // Arrange
        var codecs = new ReferenceCodecTestContext();
        var playbackClient = new StubPlaybackClient();
        var service = CreateService(
            new StubPlayerClient(
                Player(true, PlayerPlaybackState.Stopped),
                Player(true, PlayerPlaybackState.Playing)),
            playbackClient,
            codecs);
        var reference = Reference(
            codecs.Search,
            new MediaIdentity(MediaEntityKind.Track, "43"),
            0);

        // Act
        var outcome = await service.PlayAsync(
            "  NORTH room ",
            [reference],
            TestContext.Current.CancellationToken);

        // Assert
        var succeeded = Assert.IsType<PlaybackSucceeded>(outcome);
        var player = Assert.IsType<LmsPlayerStatus>(succeeded.Player);
        Assert.Equal(PlayerId, player.Id);
        Assert.Equal(["check:Track:43", "load:Track:43"], playbackClient.Operations);
    }

    [Fact]
    public async Task SuccessfulPlaybackShouldMarkItsSearchCorrelationSelected()
    {
        // Arrange
        var codecs = new ReferenceCodecTestContext();
        var codec = codecs.Search;
        var store = new RecordingSearchObservationStore();
        var service = CreateService(
            new StubPlayerClient(Player(true, PlayerPlaybackState.Playing), Player(true, PlayerPlaybackState.Playing)),
            new StubPlaybackClient(),
            codecs,
            store);

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
        var codecs = new ReferenceCodecTestContext();
        var searchCodec = codecs.Search;
        var browseCodec = codecs.Browse;
        var store = new RecordingSearchObservationStore();
        var playbackClient = new StubPlaybackClient();
        var service = new PlaybackService(
            new StubPlayerClient(
                Player(true, PlayerPlaybackState.Stopped),
                Player(true, PlayerPlaybackState.Playing)),
            playbackClient,
            new PlayerSelectorResolver(),
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
        var codecs = new ReferenceCodecTestContext();
        var searchCodec = codecs.Search;
        var browseCodec = codecs.Browse;
        var store = new RecordingSearchObservationStore();
        var service = new PlaybackService(
            new StubPlayerClient(
                Player(true, PlayerPlaybackState.Stopped),
                Player(true, PlayerPlaybackState.Playing)),
            new StubPlaybackClient(),
            new PlayerSelectorResolver(),
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
        var service = CreateService(playerClient, playbackClient);

        // Act
        var outcome = await service.PlayAsync(
            PlayerId,
            ["not-a-result-reference"],
            TestContext.Current.CancellationToken);

        // Assert
        var rejection = Assert.IsType<PlaybackRejected>(outcome);
        Assert.Equal(PlaybackRejectionReason.NoUsableItems, rejection.Reason);
        Assert.Contains("invalid_reference", rejection.Message, StringComparison.Ordinal);
        Assert.Equal(0, playerClient.CallCount);
        Assert.Empty(playbackClient.Operations);
    }

    [Fact]
    public async Task EmptyItemsShouldReturnARejectionWithoutCallingLms()
    {
        // Arrange
        var playerClient = new StubPlayerClient(Player(true, PlayerPlaybackState.Playing));
        var playbackClient = new StubPlaybackClient();
        var service = CreateService(playerClient, playbackClient);

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
    public async Task MissingMediaShouldBeSkippedWhilePlayableItemsContinue()
    {
        // Arrange
        var codecs = new ReferenceCodecTestContext();
        var codec = codecs.Search;
        var first = new MediaIdentity(MediaEntityKind.Track, "61");
        var second = new MediaIdentity(MediaEntityKind.Album, "62");
        var playerClient = new StubPlayerClient(
            Player(true, PlayerPlaybackState.Stopped),
            Player(true, PlayerPlaybackState.Playing));
        var playbackClient = new StubPlaybackClient
        {
            PlayableCountById = { ["62"] = 0 }
        };
        var service = CreateService(playerClient, playbackClient, codecs);

        // Act
        var outcome = await service.PlayAsync(
            PlayerId,
            [Reference(codec, first, 0), Reference(codec, second, 1)],
            TestContext.Current.CancellationToken);

        // Assert
        var succeeded = Assert.IsType<PlaybackSucceeded>(outcome);
        Assert.Equal(2, succeeded.RequestedItemCount);
        Assert.Equal(1, succeeded.CompletedItemCount);
        var skipped = Assert.Single(succeeded.SkippedItems);
        Assert.Equal(2, skipped.Index);
        Assert.Equal(MediaItemSkipReason.MediaUnavailable, skipped.Reason);
        Assert.Equal(
            ["check:Track:61", "check:Album:62", "load:Track:61"],
            playbackClient.Operations);
    }

    [Fact]
    public async Task MissingPlayerShouldFailBeforePowerOrQueueMutation()
    {
        // Arrange
        var codecs = new ReferenceCodecTestContext();
        var codec = codecs.Search;
        var identity = new MediaIdentity(MediaEntityKind.Track, "63");
        var playerClient = new StubPlayerClient(new LmsPlayerStatus(
            "66:77:88:99:aa:bb",
            "South Room",
            false,
            PlayerPlaybackState.Stopped));
        var playbackClient = new StubPlaybackClient();
        var service = CreateService(playerClient, playbackClient, codecs);

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
    public async Task PowerFailureShouldReturnStructuredCurrentStateWithoutMutatingQueue()
    {
        // Arrange
        var codecs = new ReferenceCodecTestContext();
        var codec = codecs.Search;
        var identity = new MediaIdentity(MediaEntityKind.Track, "71");
        var playerClient = new StubPlayerClient(Player(false, PlayerPlaybackState.Stopped));
        var playbackClient = new StubPlaybackClient
        {
            PowerOnException = new LmsRequestException("Synthetic power failure.")
        };
        var service = CreateService(playerClient, playbackClient, codecs);

        // Act
        var outcome = await service.PlayAsync(
            PlayerId,
            [Reference(codec, identity, 0)],
            TestContext.Current.CancellationToken);

        // Assert
        var failed = Assert.IsType<PlaybackFailed>(outcome);
        var player = Assert.IsType<LmsPlayerStatus>(failed.Player);
        Assert.False(player.PoweredOn);
        Assert.Contains("Synthetic power failure.", failed.Message, StringComparison.Ordinal);
        var skipped = Assert.Single(failed.SkippedItems);
        Assert.Equal(1, skipped.Index);
        Assert.Equal(MediaItemSkipReason.NotAttempted, skipped.Reason);
        Assert.Equal(
            ["check:Track:71", "power-on"],
            playbackClient.Operations);
    }

    private static LmsPlayerStatus Player(
        bool poweredOn,
        PlayerPlaybackState playbackState) =>
        new(PlayerId, "North Room", poweredOn, playbackState);

    private static PlaybackService CreateService(
        ILmsPlayerClient playerClient,
        ILmsPlaybackClient playbackClient,
        ReferenceCodecTestContext? codecs = null,
        ISearchObservationStore? observationStore = null)
    {
        codecs ??= new ReferenceCodecTestContext();
        return new PlaybackService(
            playerClient,
            playbackClient,
            new PlayerSelectorResolver(),
            codecs.Resolver,
            observationStore ?? NullSearchObservationStore.Instance,
            TimeProvider.System,
            NullLogger<PlaybackService>.Instance);
    }

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

        public Exception? ExceptionAfterFirstCall { get; init; }

        public Task<IReadOnlyList<LmsPlayerStatus>> GetPlayersAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (CallCount > 0 && ExceptionAfterFirstCall is not null)
            {
                CallCount++;
                return Task.FromException<IReadOnlyList<LmsPlayerStatus>>(
                    ExceptionAfterFirstCall);
            }

            var result = results[Math.Min(CallCount, results.Length - 1)];
            CallCount++;
            return Task.FromResult<IReadOnlyList<LmsPlayerStatus>>([result]);
        }
    }

    private sealed class StubPlaybackClient : ILmsPlaybackClient
    {
        public List<string> Operations { get; } = [];

        public Dictionary<string, int> PlayableCountById { get; } = [];

        public Dictionary<string, Exception> MutationExceptionById { get; } = [];

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
            return MutationExceptionById.TryGetValue(media.Identity.Id, out var exception)
                ? Task.FromException(exception)
                : Task.CompletedTask;
        }

        public Task AddAsync(
            string playerId,
            PlayableMedia media,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(PlayerId, playerId);
            Operations.Add($"add:{Describe(media)}");
            return MutationExceptionById.TryGetValue(media.Identity.Id, out var exception)
                ? Task.FromException(exception)
                : Task.CompletedTask;
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
            media.ArtistScope is { } scope
                ? $"{media.Identity.Kind}:{media.Identity.Id}:{scope}"
                : $"{media.Identity.Kind}:{media.Identity.Id}";

    }
}
