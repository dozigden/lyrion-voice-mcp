using LyrionVoiceMcp.Abstractions.Providers.BbcSounds;
using LyrionVoiceMcp.Ef.Abstractions.DataAccess;
using LyrionVoiceMcp.Ef.Abstractions.Providers.BbcSounds;

namespace LyrionVoiceMcp.Services.Providers.BbcSounds;

internal sealed class BbcSubscriptionStore(IDbContextScopeFactory scopes, IBbcSubscriptionRepository repository)
{
    public async Task<BbcSubscriptions?> ReadAsync(CancellationToken cancellationToken)
    {
        EntityBbcSubscriptionState? state;
        using (scopes.CreateReadOnly())
        {
            state = await repository.GetStateAsync(cancellationToken);
        }
        if (state is null) return null;
        var shows = new List<BbcShow>();
        var afterId = 0;
        while (true)
        {
            using var scope = scopes.CreateReadOnly();
            var page = await repository.ReadPageAsync(state.SnapshotId, afterId, cancellationToken);
            if (page.Count == 0) break;
            shows.AddRange(page.Select(x => new BbcShow(x.ProgrammeId, x.Title)));
            if (shows.Count > BbcSoundsProvider.MaximumShows) throw new InvalidOperationException("The subscription snapshot exceeds its limit.");
            afterId = page[^1].Id;
        }
        if (shows.Count != state.ShowCount) throw new InvalidOperationException("The subscription snapshot is incomplete.");
        return new BbcSubscriptions(state.Available, shows);
    }

    public async Task ReplaceAsync(BbcSubscriptions snapshot, CancellationToken cancellationToken)
    {
        if (snapshot.Shows.Count > BbcSoundsProvider.MaximumShows
            || snapshot.Shows.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != snapshot.Shows.Count
            || snapshot.Shows.Any(x => string.IsNullOrWhiteSpace(x.Id) || x.Id.Length > 256
                || string.IsNullOrWhiteSpace(x.Title) || x.Title.Length > 1024)
            || (!snapshot.Available && snapshot.Shows.Count != 0))
        {
            throw new InvalidOperationException("The subscription snapshot is invalid.");
        }
        var snapshotId = Guid.NewGuid().ToString("N");
        foreach (var page in snapshot.Shows.Chunk(500))
        {
            using var scope = scopes.Create();
            repository.AddShows(page.Select(x => new EntityBbcSubscribedShow
            {
                SnapshotId = snapshotId, ProgrammeId = x.Id, Title = x.Title
            }));
            await scope.SaveChangesAsync(cancellationToken);
        }
        using (var scope = scopes.Create())
        {
            var state = await repository.GetStateAsync(cancellationToken);
            if (state is null)
            {
                state = new EntityBbcSubscriptionState();
                repository.AddState(state);
            }
            state.SnapshotId = snapshotId;
            state.Available = snapshot.Available;
            state.ShowCount = snapshot.Shows.Count;
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
