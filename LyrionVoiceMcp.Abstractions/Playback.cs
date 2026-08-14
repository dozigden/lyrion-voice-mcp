namespace LyrionVoiceMcp.Abstractions;

public enum PlaybackRejectionReason
{
    InvalidPlayer,
    EmptyItems,
    InvalidReference,
    PlayerNotFound,
    MediaNotFound
}

public abstract record PlaybackOutcome;

public sealed record PlaybackSucceeded(LmsPlayerStatus Player) : PlaybackOutcome;

public sealed record PlaybackRejected(
    PlaybackRejectionReason Reason,
    string Message) : PlaybackOutcome;

public interface ILmsPlaybackClient
{
    Task<int> GetPlayableItemCountAsync(
        MediaIdentity identity,
        CancellationToken cancellationToken);

    Task PowerOnAsync(
        string playerId,
        CancellationToken cancellationToken);

    Task<int> GetQueueCountAsync(
        string playerId,
        CancellationToken cancellationToken);

    Task LoadAsync(
        string playerId,
        MediaIdentity identity,
        CancellationToken cancellationToken);

    Task AddAsync(
        string playerId,
        MediaIdentity identity,
        CancellationToken cancellationToken);

    Task InsertAsync(
        string playerId,
        MediaIdentity identity,
        CancellationToken cancellationToken);

    Task ClearAsync(
        string playerId,
        CancellationToken cancellationToken);
}

public interface IPlaybackService
{
    Task<PlaybackOutcome> PlayAsync(
        string playerId,
        IReadOnlyList<string> references,
        CancellationToken cancellationToken);
}
