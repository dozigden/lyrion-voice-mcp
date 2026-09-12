namespace LyrionVoiceMcp.Ef.Abstractions.Providers.BbcSounds;

public sealed class EntityBbcSubscriptionState
{
    public int Id { get; set; } = 1;
    public string SnapshotId { get; set; } = string.Empty;
    public bool Available { get; set; }
    public int ShowCount { get; set; }
}

public sealed class EntityBbcSubscribedShow
{
    public int Id { get; set; }
    public string SnapshotId { get; set; } = string.Empty;
    public string ProgrammeId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
}

public interface IBbcSubscriptionRepository
{
    Task<EntityBbcSubscriptionState?> GetStateAsync(CancellationToken cancellationToken);
    void AddState(EntityBbcSubscriptionState state);
    void AddShows(IEnumerable<EntityBbcSubscribedShow> shows);
    Task<IReadOnlyList<EntityBbcSubscribedShow>> ReadPageAsync(string snapshotId, int afterId, CancellationToken cancellationToken);
    Task<int> DeleteInactivePageAsync(string snapshotId, CancellationToken cancellationToken);
}
