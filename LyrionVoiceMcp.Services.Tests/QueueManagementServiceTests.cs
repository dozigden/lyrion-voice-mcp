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
            [Reference(codec, first, 0), Reference(codec, second, 1)],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<QueueManagementSucceeded>(outcome);
        Assert.Equal(
            ["insert:Playlist:32", "insert:Track:31"],
            playbackClient.Mutations);
    }

    [Fact]
    public async Task QueueLimitShouldRejectTheWholeRequestBeforeMutation()
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
            QueueManagementRejectionReason.QueueLimitExceeded,
            rejected.Reason);
        Assert.Empty(playbackClient.Mutations);
    }

    [Fact]
    public async Task MissingMediaShouldRejectAllItemsBeforeReadingOrMutatingTheQueue()
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
        var rejected = Assert.IsType<QueueManagementRejected>(outcome);
        Assert.Equal(QueueManagementRejectionReason.MediaNotFound, rejected.Reason);
        Assert.Contains("item 2", rejected.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(playbackClient.QueueCountRequests);
        Assert.Empty(playbackClient.Mutations);
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
        Assert.Equal(QueueManagementRejectionReason.InvalidReference, rejected.Reason);
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

        public Queue<int> QueueCounts { get; init; } = new([0, 0]);

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
            return Task.CompletedTask;
        }

        public Task InsertAsync(
            string playerId,
            PlayableMedia media,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(PlayerId, playerId);
            Mutations.Add($"insert:{media.Identity.Kind}:{media.Identity.Id}");
            return Task.CompletedTask;
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
