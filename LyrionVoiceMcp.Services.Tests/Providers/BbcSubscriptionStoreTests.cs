using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Abstractions.Providers.BbcSounds;
using LyrionVoiceMcp.Ef;
using LyrionVoiceMcp.Ef.Abstractions.DataAccess;
using LyrionVoiceMcp.Ef.Abstractions.Providers.BbcSounds;
using LyrionVoiceMcp.Services.Providers.BbcSounds;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace LyrionVoiceMcp.Services.Tests.Providers;

public sealed class BbcSubscriptionStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"lyrion-subscriptions-{Guid.NewGuid():N}");
    private readonly ServiceProvider services;
    private readonly BbcSubscriptionStore store;
    private readonly BbcStationStore stationStore;
    private readonly IDbContextScopeFactory scopes;
    private readonly IBbcSubscriptionRepository repository;
    public BbcSubscriptionStoreTests()
    {
        services = new ServiceCollection().AddLyrionVoiceMcpEf(new ApplicationDatabaseSettings(Path.Combine(directory, "test.db"))).BuildServiceProvider();
        services.InitialiseLyrionVoiceMcpEfAsync().GetAwaiter().GetResult();
        scopes = services.GetRequiredService<IDbContextScopeFactory>();
        repository = services.GetRequiredService<IBbcSubscriptionRepository>();
        store = new BbcSubscriptionStore(scopes, repository);
        stationStore = new BbcStationStore(scopes, services.GetRequiredService<IBbcStationRepository>());
    }

    [Fact]
    public async Task StagedAndInvalidReadsMustNotReplaceTheActiveSnapshot()
    {
        await store.ReplaceAsync(new BbcSubscriptions(true, [new("first", "The Mira Vale Show")]), TestContext.Current.CancellationToken);
        using (var scope = scopes.Create())
        {
            repository.AddShows([new EntityBbcSubscribedShow { SnapshotId = "unfinished", ProgrammeId = "second", Title = "The Orchard Hour" }]);
            await scope.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReplaceAsync(new BbcSubscriptions(true,
            [new("duplicate", "One"), new("duplicate", "Two")]), TestContext.Current.CancellationToken));

        var restored = await store.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal("first", Assert.Single(restored!.Shows).Id);
        Assert.True(restored.Available);
    }

    [Fact]
    public async Task CompleteRefreshMustReconcileAndAllowAnEmptySubscriptionList()
    {
        await store.ReplaceAsync(new BbcSubscriptions(true, Enumerable.Range(1, 501)
            .Select(i => new BbcShow(i.ToString(), $"Fictional Programme {i}")).ToArray()), TestContext.Current.CancellationToken);
        Assert.Equal(501, (await store.ReadAsync(TestContext.Current.CancellationToken))!.Shows.Count);

        await store.ReplaceAsync(new BbcSubscriptions(true, []), TestContext.Current.CancellationToken);
        await store.CleanAsync(TestContext.Current.CancellationToken);

        var restored = await store.ReadAsync(TestContext.Current.CancellationToken);
        Assert.True(restored!.Available);
        Assert.Empty(restored.Shows);
    }

    [Fact]
    public async Task UpstreamFailureMustKeepPublishedSubscriptions()
    {
        var snapshot = new BbcSubscriptions(true, [new("first", "The Mira Vale Show")]);
        await store.ReplaceAsync(snapshot, TestContext.Current.CancellationToken);
        var index = new StubIndex(snapshot);
        var job = new BbcSoundsRefreshJob(
            new FailingClient(), store, stationStore, index, new StubStationIndex(), new Log());

        var result = await job.HandleAsync(new JobContext(7, BbcSoundsProvider.RefreshJob, "{}"), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("first", Assert.Single(index.Shows).Id);
        Assert.Equal("first", Assert.Single((await store.ReadAsync(TestContext.Current.CancellationToken))!.Shows).Id);
    }

    [Fact]
    public async Task StationDiscoveryFailureMustRetainBothProviderSnapshots()
    {
        var subscriptions = new BbcSubscriptions(true, [new("first", "The Mira Vale Show")]);
        var stations = new BbcStations(true,
            [new("stationa", "Fictional Radio", "sounds://_LIVE_stationa")]);
        await store.ReplaceAsync(subscriptions, TestContext.Current.CancellationToken);
        await stationStore.ReplaceAsync(stations, TestContext.Current.CancellationToken);
        var subscriptionIndex = new StubIndex(subscriptions);
        var stationIndex = new StubStationIndex(stations);
        var job = new BbcSoundsRefreshJob(new StationFailingClient(), store, stationStore,
            subscriptionIndex, stationIndex, new Log());

        var result = await job.HandleAsync(
            new JobContext(8, BbcSoundsProvider.RefreshJob, "{}"), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("first", Assert.Single(subscriptionIndex.Shows).Id);
        Assert.Equal("stationa", Assert.Single(stationIndex.Stations).Id);
        Assert.Equal("first", Assert.Single((await store.ReadAsync(TestContext.Current.CancellationToken))!.Shows).Id);
        Assert.Equal("stationa", Assert.Single((await stationStore.ReadAsync(TestContext.Current.CancellationToken))!.Stations).Id);
    }

    public void Dispose()
    {
        services.Dispose();
        SqliteConnection.ClearAllPools();
        Directory.Delete(directory, true);
    }

    private sealed class StubIndex(BbcSubscriptions snapshot) : IBbcSubscriptionIndex
    {
        public bool Available => snapshot.Available;
        public IReadOnlyList<BbcShow> Shows => snapshot.Shows;
        public void Publish(BbcSubscriptions value) => snapshot = value;
        public IReadOnlyList<BbcShowMatch> Search(string name, CancellationToken token) => [];
    }
    private sealed class FailingClient : IBbcSoundsClient
    {
        public Task<BbcSubscriptions> ReadSubscriptionsAsync(CancellationToken token) => throw new LmsRequestException("Fictional upstream failure.");
        public Task<BbcStations> ReadStationsAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<BbcEpisodePage> BrowseEpisodesAsync(string id, int offset, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<BbcStationMenuItem>> BrowseStationAsync(string id, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<BbcStationMenuItem>> BrowseStationMenuAsync(BbcStationMenuTarget target, CancellationToken token) => throw new NotSupportedException();
        public Task<bool> IsAvailableAsync(BbcEpisodeTarget target, CancellationToken token) => throw new NotSupportedException();
        public Task<bool> IsAvailableAsync(BbcAudioTarget target, CancellationToken token) => throw new NotSupportedException();
        public Task SubmitAsync(string player, BbcEpisodeTarget target, LyrionVoiceMcp.Abstractions.Providers.ProviderPlaybackCommand command, CancellationToken token) => throw new NotSupportedException();
        public Task SubmitAsync(string player, BbcAudioTarget target, LyrionVoiceMcp.Abstractions.Providers.ProviderPlaybackCommand command, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class StubStationIndex(BbcStations? snapshot = null) : IBbcStationIndex
    {
        private BbcStations current = snapshot ?? new BbcStations(false, []);
        public bool Available => current.Available;
        public IReadOnlyList<BbcStation> Stations => current.Stations;
        public void Publish(BbcStations stations) => current = stations;
        public IReadOnlyList<BbcStationMatch> Search(string name, CancellationToken token) => [];
    }
    private sealed class StationFailingClient : IBbcSoundsClient
    {
        public Task<BbcSubscriptions> ReadSubscriptionsAsync(CancellationToken token) =>
            Task.FromResult(new BbcSubscriptions(true, [new("second", "The Orchard Hour")]));
        public Task<BbcStations> ReadStationsAsync(CancellationToken token) =>
            throw new LmsRequestException("Fictional station discovery failure.");
        public Task<BbcEpisodePage> BrowseEpisodesAsync(string id, int offset, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<BbcStationMenuItem>> BrowseStationAsync(string id, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<BbcStationMenuItem>> BrowseStationMenuAsync(BbcStationMenuTarget target, CancellationToken token) => throw new NotSupportedException();
        public Task<bool> IsAvailableAsync(BbcEpisodeTarget target, CancellationToken token) => throw new NotSupportedException();
        public Task<bool> IsAvailableAsync(BbcAudioTarget target, CancellationToken token) => throw new NotSupportedException();
        public Task SubmitAsync(string player, BbcEpisodeTarget target, LyrionVoiceMcp.Abstractions.Providers.ProviderPlaybackCommand command, CancellationToken token) => throw new NotSupportedException();
        public Task SubmitAsync(string player, BbcAudioTarget target, LyrionVoiceMcp.Abstractions.Providers.ProviderPlaybackCommand command, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Log : IJobLogWriter
    {
        public Task WriteAsync(long id, JobLogLevel level, string message, object? data, CancellationToken token) => Task.CompletedTask;
    }
}
