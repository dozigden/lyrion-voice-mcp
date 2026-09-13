using LyrionVoiceMcp.Abstractions.Providers.BbcSounds;
using LyrionVoiceMcp.Ef;
using LyrionVoiceMcp.Ef.Abstractions.DataAccess;
using LyrionVoiceMcp.Ef.Abstractions.Providers.BbcSounds;
using LyrionVoiceMcp.Services.Providers.BbcSounds;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace LyrionVoiceMcp.Services.Tests.Providers;

public sealed class BbcStationStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"lyrion-stations-{Guid.NewGuid():N}");
    private readonly ServiceProvider services;
    private readonly BbcStationStore store;
    private readonly IDbContextScopeFactory scopes;
    private readonly IBbcStationRepository repository;

    public BbcStationStoreTests()
    {
        services = new ServiceCollection()
            .AddLyrionVoiceMcpEf(new ApplicationDatabaseSettings(Path.Combine(directory, "test.db")))
            .BuildServiceProvider();
        services.InitialiseLyrionVoiceMcpEfAsync().GetAwaiter().GetResult();
        scopes = services.GetRequiredService<IDbContextScopeFactory>();
        repository = services.GetRequiredService<IBbcStationRepository>();
        store = new BbcStationStore(scopes, repository);
    }

    [Fact]
    public async Task CompleteEmptyRefreshShouldReplaceAndCleanTheActiveSnapshot()
    {
        await store.ReplaceAsync(new BbcStations(true,
            [new("stationa", "Fictional Radio", "sounds://_LIVE_stationa")]),
            TestContext.Current.CancellationToken);

        await store.ReplaceAsync(new BbcStations(true, []), TestContext.Current.CancellationToken);
        await store.CleanAsync(TestContext.Current.CancellationToken);

        var restored = await store.ReadAsync(TestContext.Current.CancellationToken);
        Assert.True(restored!.Available);
        Assert.Empty(restored.Stations);
    }

    [Fact]
    public async Task InvalidOrIncompleteSnapshotShouldNotReplaceTheActiveSnapshot()
    {
        await store.ReplaceAsync(new BbcStations(true,
            [new("stationa", "Fictional Radio", "sounds://_LIVE_stationa")]),
            TestContext.Current.CancellationToken);
        using (var scope = scopes.Create())
        {
            repository.AddStations([new EntityBbcStation
            {
                SnapshotId = "unfinished",
                StationId = "stationb",
                Name = "Orchard Speech",
                LiveAudioUrl = "sounds://_LIVE_stationb"
            }]);
            await scope.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReplaceAsync(new BbcStations(true,
            [new("duplicate", "One", "sounds://_LIVE_one"),
                new("duplicate", "Two", "sounds://_LIVE_two")]), TestContext.Current.CancellationToken));

        Assert.Equal("stationa", Assert.Single((await store.ReadAsync(TestContext.Current.CancellationToken))!.Stations).Id);
    }

    public void Dispose()
    {
        services.Dispose();
        SqliteConnection.ClearAllPools();
        Directory.Delete(directory, true);
    }
}
