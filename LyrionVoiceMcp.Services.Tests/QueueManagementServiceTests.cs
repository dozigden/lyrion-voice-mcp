using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace LyrionVoiceMcp.Services.Tests;

public sealed class QueueManagementServiceTests
{
    private const string PlayerId = "00:11:22:33:44:55";

    [Fact]
    public async Task ClearShouldValidateThePlayerAndClearWithoutItems()
    {
        // Arrange
        var playbackClient = new StubPlaybackClient();
        var service = CreateService(playbackClient);

        // Act
        var outcome = await service.ManageAsync(
            PlayerId,
            QueueManagementCommand.Clear,
            null,
            TestContext.Current.CancellationToken);

        // Assert
        var succeeded = Assert.IsType<QueueManagementSucceeded>(outcome);
        Assert.Equal(PlayerId, succeeded.PlayerId);
        Assert.Equal(0, succeeded.QueueLength);
        Assert.Equal(["clear"], playbackClient.Mutations);
        Assert.Single(playbackClient.QueueCountRequests);
        Assert.Empty(playbackClient.CheckedItems);
    }

    [Fact]
    public async Task UniquePlayerNameShouldUseTheCanonicalIdForQueueManagement()
    {
        // Arrange
        var playbackClient = new StubPlaybackClient();
        var service = CreateService(playbackClient);

        // Act
        var outcome = await service.ManageAsync(
            " north ROOM ",
            QueueManagementCommand.Clear,
            null,
            TestContext.Current.CancellationToken);

        // Assert
        var succeeded = Assert.IsType<QueueManagementSucceeded>(outcome);
        Assert.Equal(PlayerId, succeeded.PlayerId);
        Assert.Equal(["clear"], playbackClient.Mutations);
    }

    [Fact]
    public async Task AppendShouldPreserveCallerOrderAndReturnTheUpdatedLength()
    {
        // Arrange
        var codecs = new ReferenceCodecTestContext();
        var codec = codecs.Search;
        var store = new RecordingSearchObservationStore();
        var first = new MediaIdentity(MediaEntityKind.Album, "21");
        var second = new MediaIdentity(MediaEntityKind.Track, "22");
        var playbackClient = new StubPlaybackClient
        {
            PlayableCountById = { ["21"] = 2, ["22"] = 1 },
            QueueCounts = new Queue<int>([4, 7])
        };
        var service = new QueueManagementService(
            new StubPlayerClient(Player()),
            playbackClient,
            new PlayerSelectorResolver(),
            codecs.Resolver,
            store,
            TimeProvider.System,
            NullLogger<QueueManagementService>.Instance);

        // Act
        var outcome = await service.ManageAsync(
            PlayerId,
            QueueManagementCommand.Append,
            [Reference(codec, first, 0), Reference(codec, second, 1)],
            TestContext.Current.CancellationToken);

        // Assert
        var succeeded = Assert.IsType<QueueManagementSucceeded>(outcome);
        Assert.Equal(7, succeeded.QueueLength);
        Assert.Equal(
            ["add:Album:21", "add:Track:22"],
            playbackClient.Mutations);
        Assert.Equal(
            ["00000000000000000000000000000001", "00000000000000000000000000000002"],
            store.SelectedCorrelationIds);
    }

    [Fact]
    public async Task InsertNextShouldReverseSubmissionsToPreserveCallerOrderInLms()
    {
        // Arrange
        var codecs = new ReferenceCodecTestContext();
        var codec = codecs.Search;
        var first = new MediaIdentity(MediaEntityKind.Track, "31");
        var second = new MediaIdentity(MediaEntityKind.Playlist, "32");
        var playbackClient = new StubPlaybackClient
        {
            QueueCounts = new Queue<int>([5, 7])
        };
        var service = CreateService(playbackClient, codecs);

        // Act
        var outcome = await service.ManageAsync(
            PlayerId,
            QueueManagementCommand.InsertNext,
            [
                Reference(codec, first, 0),
                "invalid-reference",
                Reference(codec, second, 2)
            ],
            TestContext.Current.CancellationToken);

        // Assert
        var succeeded = Assert.IsType<QueueManagementSucceeded>(outcome);
        Assert.Equal(3, succeeded.RequestedItemCount);
        Assert.Equal(2, succeeded.CompletedItemCount);
        var skipped = Assert.Single(succeeded.SkippedItems);
        Assert.Equal(2, skipped.Index);
        Assert.Equal(MediaItemSkipReason.InvalidReference, skipped.Reason);
        Assert.Equal(
            ["insert:Playlist:32", "insert:Track:31"],
            playbackClient.Mutations);
    }

    [Fact]
    public async Task AppendShouldUseRemainingCapacityAndReportItemsThatDoNotFit()
    {
        // Arrange
        var codecs = new ReferenceCodecTestContext();
        var playbackClient = new StubPlaybackClient
        {
            PlayableCountById =
            {
                ["34"] = 2,
                ["35"] = 2,
                ["36"] = 1
            },
            QueueCounts = new Queue<int>([297, 300])
        };
        var service = CreateService(playbackClient, codecs);

        // Act
        var outcome = await service.ManageAsync(
            PlayerId,
            QueueManagementCommand.Append,
            [
                Reference(codecs.Search, new MediaIdentity(MediaEntityKind.Album, "34"), 0),
                Reference(codecs.Search, new MediaIdentity(MediaEntityKind.Album, "35"), 1),
                Reference(codecs.Search, new MediaIdentity(MediaEntityKind.Track, "36"), 2)
            ],
            TestContext.Current.CancellationToken);

        // Assert
        var succeeded = Assert.IsType<QueueManagementSucceeded>(outcome);
        Assert.Equal(3, succeeded.RequestedItemCount);
        Assert.Equal(2, succeeded.CompletedItemCount);
        Assert.Equal(300, succeeded.QueueLength);
        var skipped = Assert.Single(succeeded.SkippedItems);
        Assert.Equal(2, skipped.Index);
        Assert.Equal(MediaItemSkipReason.QueueCapacity, skipped.Reason);
        Assert.Equal(
            ["add:Album:34", "add:Track:36"],
            playbackClient.Mutations);
    }

    [Fact]
    public async Task AppendFailureShouldStopAndSelectOnlyCompletedMedia()
    {
        // Arrange
        var codecs = new ReferenceCodecTestContext();
        var store = new RecordingSearchObservationStore();
        var playbackClient = new StubPlaybackClient
        {
            QueueCounts = new Queue<int>([0, 1]),
            MutationExceptionById =
            {
                ["38"] = new LmsRequestException("Synthetic queue failure.")
            }
        };
        var service = new QueueManagementService(
            new StubPlayerClient(Player()),
            playbackClient,
            new PlayerSelectorResolver(),
            codecs.Resolver,
            store,
            TimeProvider.System,
            NullLogger<QueueManagementService>.Instance);

        // Act
        var outcome = await service.ManageAsync(
            PlayerId,
            QueueManagementCommand.Append,
            [
                Reference(codecs.Search, new MediaIdentity(MediaEntityKind.Track, "37"), 0),
                Reference(codecs.Search, new MediaIdentity(MediaEntityKind.Track, "38"), 1),
                Reference(codecs.Search, new MediaIdentity(MediaEntityKind.Track, "39"), 2)
            ],
            TestContext.Current.CancellationToken);

        // Assert
        var succeeded = Assert.IsType<QueueManagementSucceeded>(outcome);
        Assert.Equal(1, succeeded.CompletedItemCount);
        Assert.Equal(1, succeeded.QueueLength);
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
        Assert.Equal(["add:Track:37", "add:Track:38"], playbackClient.Mutations);
        Assert.Equal(
            ["00000000000000000000000000000001"],
            store.SelectedCorrelationIds);
    }

    [Fact]
    public async Task FirstQueueMutationFailureShouldReturnStructuredCurrentStateAndStopTheBatch()
    {
        // Arrange
        var codecs = new ReferenceCodecTestContext();
        var playbackClient = new StubPlaybackClient
        {
            QueueCounts = new Queue<int>([0, 0]),
            MutationExceptionById =
            {
                ["40"] = new LmsRequestException("Synthetic first-add failure.")
            }
        };
        var service = CreateService(playbackClient, codecs);

        // Act
        var outcome = await service.ManageAsync(
            PlayerId,
            QueueManagementCommand.Append,
            [
                Reference(codecs.Search, new MediaIdentity(MediaEntityKind.Track, "40"), 0),
                Reference(codecs.Search, new MediaIdentity(MediaEntityKind.Track, "41"), 1)
            ],
            TestContext.Current.CancellationToken);

        // Assert
        var failed = Assert.IsType<QueueManagementFailed>(outcome);
        Assert.Equal(PlayerId, failed.PlayerId);
        Assert.Equal(0, failed.QueueLength);
        Assert.Equal(2, failed.RequestedItemCount);
        Assert.Null(failed.StateRefreshError);
        Assert.Contains("Item 1: lms_error", failed.Message, StringComparison.Ordinal);
        Assert.Contains("Item 2: not_attempted", failed.Message, StringComparison.Ordinal);
        Assert.Collection(
            failed.SkippedItems,
            item => Assert.Equal(MediaItemSkipReason.LmsError, item.Reason),
            item => Assert.Equal(MediaItemSkipReason.NotAttempted, item.Reason));
        Assert.Equal(["add:Track:40"], playbackClient.Mutations);
    }

    [Fact]
    public async Task QueueRefreshFailureShouldNotHideCompletedAdditions()
    {
        // Arrange
        var codecs = new ReferenceCodecTestContext();
        var playbackClient = new StubPlaybackClient
        {
            QueueCounts = new Queue<int>([0]),
            QueueCountExceptionAfterFirstCall =
                new LmsRequestException("Synthetic queue refresh failure.")
        };
        var service = CreateService(playbackClient, codecs);

        // Act
        var outcome = await service.ManageAsync(
            PlayerId,
            QueueManagementCommand.Append,
            [Reference(codecs.Search, new MediaIdentity(MediaEntityKind.Track, "42"), 0)],
            TestContext.Current.CancellationToken);

        // Assert
        var succeeded = Assert.IsType<QueueManagementSucceeded>(outcome);
        Assert.Equal(1, succeeded.CompletedItemCount);
        Assert.Null(succeeded.QueueLength);
        Assert.Equal(
            "Synthetic queue refresh failure.",
            succeeded.StateRefreshError);
    }

    [Fact]
    public async Task ItemThatCannotFitShouldReturnNoUsableItemsWithoutMutation()
    {
        // Arrange
        var codecs = new ReferenceCodecTestContext();
        var codec = codecs.Search;
        var playbackClient = new StubPlaybackClient
        {
            PlayableCountById = { ["41"] = 2 },
            QueueCounts = new Queue<int>([299])
        };
        var service = CreateService(playbackClient, codecs);

        // Act
        var outcome = await service.ManageAsync(
            PlayerId,
            QueueManagementCommand.Append,
            [Reference(codec, new MediaIdentity(MediaEntityKind.Album, "41"), 0)],
            TestContext.Current.CancellationToken);

        // Assert
        var rejected = Assert.IsType<QueueManagementRejected>(outcome);
        Assert.Equal(
            QueueManagementRejectionReason.NoUsableItems,
            rejected.Reason);
        Assert.Contains("queue_capacity", rejected.Message, StringComparison.Ordinal);
        Assert.Empty(playbackClient.Mutations);
    }

    [Fact]
    public async Task MissingMediaShouldBeSkippedWhileAvailableItemsAreAdded()
    {
        // Arrange
        var codecs = new ReferenceCodecTestContext();
        var codec = codecs.Search;
        var playbackClient = new StubPlaybackClient
        {
            PlayableCountById = { ["52"] = 0 }
        };
        var service = CreateService(playbackClient, codecs);

        // Act
        var outcome = await service.ManageAsync(
            PlayerId,
            QueueManagementCommand.Append,
            [
                Reference(codec, new MediaIdentity(MediaEntityKind.Track, "51"), 0),
                Reference(codec, new MediaIdentity(MediaEntityKind.Album, "52"), 1)
            ],
            TestContext.Current.CancellationToken);

        // Assert
        var succeeded = Assert.IsType<QueueManagementSucceeded>(outcome);
        Assert.Equal(2, succeeded.RequestedItemCount);
        Assert.Equal(1, succeeded.CompletedItemCount);
        var skipped = Assert.Single(succeeded.SkippedItems);
        Assert.Equal(2, skipped.Index);
        Assert.Equal(MediaItemSkipReason.MediaUnavailable, skipped.Reason);
        Assert.Equal(["add:Track:51"], playbackClient.Mutations);
    }

    [Fact]
    public async Task InvalidReferenceShouldRejectBeforeAnyLmsCall()
    {
        // Arrange
        var playerClient = new StubPlayerClient(Player());
        var playbackClient = new StubPlaybackClient();
        var service = CreateService(playbackClient, playerClient: playerClient);

        // Act
        var outcome = await service.ManageAsync(
            PlayerId,
            QueueManagementCommand.Append,
            ["invalid-reference"],
            TestContext.Current.CancellationToken);

        // Assert
        var rejected = Assert.IsType<QueueManagementRejected>(outcome);
        Assert.Equal(QueueManagementRejectionReason.NoUsableItems, rejected.Reason);
        Assert.Contains("invalid_reference", rejected.Message, StringComparison.Ordinal);
        Assert.Equal(0, playerClient.CallCount);
        Assert.Empty(playbackClient.CheckedItems);
        Assert.Empty(playbackClient.Mutations);
    }

    [Fact]
    public async Task MissingPlayerShouldRejectBeforeReadingOrMutatingTheQueue()
    {
        // Arrange
        var codecs = new ReferenceCodecTestContext();
        var codec = codecs.Search;
        var playbackClient = new StubPlaybackClient();
        var service = CreateService(
            playbackClient,
            codecs,
            new StubPlayerClient());

        // Act
        var outcome = await service.ManageAsync(
            PlayerId,
            QueueManagementCommand.Append,
            [Reference(codec, new MediaIdentity(MediaEntityKind.Track, "61"), 0)],
            TestContext.Current.CancellationToken);

        // Assert
        var rejected = Assert.IsType<QueueManagementRejected>(outcome);
        Assert.Equal(QueueManagementRejectionReason.PlayerNotFound, rejected.Reason);
        Assert.Empty(playbackClient.QueueCountRequests);
        Assert.Empty(playbackClient.Mutations);
    }

    [Fact]
    public async Task InvalidActionShouldRejectBeforeAnyLmsCall()
    {
        // Arrange
        var playerClient = new StubPlayerClient(Player());
        var playbackClient = new StubPlaybackClient();
        var service = CreateService(playbackClient, playerClient: playerClient);

        // Act
        var outcome = await service.ManageAsync(
            PlayerId,
            (QueueManagementCommand)99,
            null,
            TestContext.Current.CancellationToken);

        // Assert
        var rejected = Assert.IsType<QueueManagementRejected>(outcome);
        Assert.Equal(QueueManagementRejectionReason.InvalidAction, rejected.Reason);
        Assert.Equal(0, playerClient.CallCount);
        Assert.Empty(playbackClient.CheckedItems);
        Assert.Empty(playbackClient.Mutations);
    }

    [Theory]
    [InlineData(QueueManagementCommand.Append)]
    [InlineData(QueueManagementCommand.InsertNext)]
    public async Task AddingWithoutItemsShouldRejectBeforeAnyLmsCall(
        QueueManagementCommand command)
    {
        // Arrange
        var playerClient = new StubPlayerClient(Player());
        var playbackClient = new StubPlaybackClient();
        var service = CreateService(playbackClient, playerClient: playerClient);

        // Act
        var outcome = await service.ManageAsync(
            PlayerId,
            command,
            [],
            TestContext.Current.CancellationToken);

        // Assert
        var rejected = Assert.IsType<QueueManagementRejected>(outcome);
        Assert.Equal(QueueManagementRejectionReason.EmptyItems, rejected.Reason);
        Assert.Equal(0, playerClient.CallCount);
        Assert.Empty(playbackClient.Mutations);
    }

    [Fact]
    public async Task ClearWithItemsShouldRejectBeforeAnyLmsCall()
    {
        // Arrange
        var playerClient = new StubPlayerClient(Player());
        var playbackClient = new StubPlaybackClient();
        var service = CreateService(playbackClient, playerClient: playerClient);

        // Act
        var outcome = await service.ManageAsync(
            PlayerId,
            QueueManagementCommand.Clear,
            ["unused-reference"],
            TestContext.Current.CancellationToken);

        // Assert
        var rejected = Assert.IsType<QueueManagementRejected>(outcome);
        Assert.Equal(QueueManagementRejectionReason.ItemsNotAllowed, rejected.Reason);
        Assert.Equal(0, playerClient.CallCount);
        Assert.Empty(playbackClient.Mutations);
    }

    private static QueueManagementService CreateService(
        StubPlaybackClient playbackClient,
        ReferenceCodecTestContext? codecs = null,
        StubPlayerClient? playerClient = null) =>
        new(
            playerClient ?? new StubPlayerClient(Player()),
            playbackClient,
            new PlayerSelectorResolver(),
            (codecs ?? new ReferenceCodecTestContext()).Resolver,
            NullSearchObservationStore.Instance,
            TimeProvider.System,
            NullLogger<QueueManagementService>.Instance);

    private static LmsPlayerStatus Player() =>
        new(PlayerId, "North Room", true, PlayerPlaybackState.Stopped);

    private static string Reference(
        ISearchResultReferenceCodec codec,
        MediaIdentity identity,
        int occurrence) =>
        codec.Encode(new SearchResultReferenceValue(
            $"{occurrence + 1:x32}",
            identity));

    private sealed class StubPlayerClient(params LmsPlayerStatus[] players)
        : ILmsPlayerClient
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<LmsPlayerStatus>> GetPlayersAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult<IReadOnlyList<LmsPlayerStatus>>(players);
        }
    }

    private sealed class StubPlaybackClient : ILmsPlaybackClient
    {
        public Dictionary<string, int> PlayableCountById { get; } = [];

        public Dictionary<string, Exception> MutationExceptionById { get; } = [];

        public Queue<int> QueueCounts { get; init; } = new([0, 0]);

        public Exception? QueueCountExceptionAfterFirstCall { get; init; }

        public List<string> CheckedItems { get; } = [];

        public List<string> QueueCountRequests { get; } = [];

        public List<string> Mutations { get; } = [];

        public Task<int> GetPlayableItemCountAsync(
            PlayableMedia media,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CheckedItems.Add($"{media.Identity.Kind}:{media.Identity.Id}");
            return Task.FromResult(
                PlayableCountById.TryGetValue(media.Identity.Id, out var count) ? count : 1);
        }

        public Task PowerOnAsync(
            string playerId,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Queue management must not power on a player.");

        public Task<int> GetQueueCountAsync(
            string playerId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(PlayerId, playerId);
            QueueCountRequests.Add(playerId);
            if (QueueCountRequests.Count > 1
                && QueueCountExceptionAfterFirstCall is not null)
            {
                return Task.FromException<int>(QueueCountExceptionAfterFirstCall);
            }

            return Task.FromResult(QueueCounts.Dequeue());
        }

        public Task LoadAsync(
            string playerId,
            PlayableMedia media,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Queue management must not load media.");

        public Task AddAsync(
            string playerId,
            PlayableMedia media,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(PlayerId, playerId);
            Mutations.Add($"add:{media.Identity.Kind}:{media.Identity.Id}");
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
            Mutations.Add($"insert:{media.Identity.Kind}:{media.Identity.Id}");
            return MutationExceptionById.TryGetValue(media.Identity.Id, out var exception)
                ? Task.FromException(exception)
                : Task.CompletedTask;
        }

        public Task ClearAsync(
            string playerId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(PlayerId, playerId);
            Mutations.Add("clear");
            return Task.CompletedTask;
        }

    }
}
