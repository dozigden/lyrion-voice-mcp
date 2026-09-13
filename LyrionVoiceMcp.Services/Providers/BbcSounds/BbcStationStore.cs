using LyrionVoiceMcp.Abstractions.Providers.BbcSounds;
using LyrionVoiceMcp.Ef.Abstractions.DataAccess;
using LyrionVoiceMcp.Ef.Abstractions.Providers.BbcSounds;

namespace LyrionVoiceMcp.Services.Providers.BbcSounds;

internal sealed class BbcStationStore(IDbContextScopeFactory scopes, IBbcStationRepository repository)
{
    public async Task<BbcStations?> ReadAsync(CancellationToken cancellationToken)
    {
        EntityBbcStationState? state;
        using (scopes.CreateReadOnly())
        {
            state = await repository.GetStateAsync(cancellationToken);
        }
        if (state is null) return null;
        var stations = new List<BbcStation>();
        var afterId = 0;
        while (true)
        {
            using var scope = scopes.CreateReadOnly();
            var page = await repository.ReadPageAsync(state.SnapshotId, afterId, cancellationToken);
            if (page.Count == 0) break;
            stations.AddRange(page.Select(x => new BbcStation(x.StationId, x.Name, x.LiveAudioUrl)));
            if (stations.Count > BbcSoundsProvider.MaximumStations)
                throw new InvalidOperationException("The BBC station snapshot exceeds its limit.");
            afterId = page[^1].Id;
        }
        if (stations.Count != state.StationCount)
            throw new InvalidOperationException("The BBC station snapshot is incomplete.");
        return new BbcStations(state.Available, stations);
    }

    public async Task ReplaceAsync(BbcStations snapshot, CancellationToken cancellationToken)
    {
        if (snapshot.Stations.Count > BbcSoundsProvider.MaximumStations
            || snapshot.Stations.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != snapshot.Stations.Count
            || snapshot.Stations.Any(x => string.IsNullOrWhiteSpace(x.Id) || x.Id.Length > 256
                || string.IsNullOrWhiteSpace(x.Name) || x.Name.Length > 1024
                || string.IsNullOrWhiteSpace(x.LiveAudioUrl) || x.LiveAudioUrl.Length > 1024)
            || (!snapshot.Available && snapshot.Stations.Count != 0))
        {
            throw new InvalidOperationException("The BBC station snapshot is invalid.");
        }
        var snapshotId = Guid.NewGuid().ToString("N");
        foreach (var page in snapshot.Stations.Chunk(500))
        {
            using var scope = scopes.Create();
            repository.AddStations(page.Select(x => new EntityBbcStation
            {
                SnapshotId = snapshotId,
                StationId = x.Id,
                Name = x.Name,
                LiveAudioUrl = x.LiveAudioUrl
            }));
            await scope.SaveChangesAsync(cancellationToken);
        }
        using (var scope = scopes.Create())
        {
            var state = await repository.GetStateAsync(cancellationToken);
            if (state is null)
            {
                state = new EntityBbcStationState();
                repository.AddState(state);
            }
            state.SnapshotId = snapshotId;
            state.Available = snapshot.Available;
            state.StationCount = snapshot.Stations.Count;
            await scope.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task CleanAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            using var scope = scopes.Create();
            var state = await repository.GetStateAsync(cancellationToken);
            if (state is null) return;
            var count = await repository.DeleteInactivePageAsync(state.SnapshotId, cancellationToken);
            await scope.SaveChangesAsync(cancellationToken);
            if (count == 0) return;
        }
    }
}
