namespace LyrionVoiceMcp.Abstractions;

public enum PlaybackRejectionReason
{
    InvalidPlayer,
    EmptyItems,
    InvalidReference,
    PlayerNotFound,
    AmbiguousPlayer,
    MediaNotFound
}

public abstract record PlaybackOutcome;

public sealed record PlaybackSucceeded(LmsPlayerStatus Player) : PlaybackOutcome;

public sealed record PlaybackRejected(
    PlaybackRejectionReason Reason,
    string Message) : PlaybackOutcome;

public enum ArtistSelectionScope
{
    AlbumArtist
}

public sealed record PlayableMedia(
    MediaIdentity Identity,
    ArtistSelectionScope? ArtistScope = null);

public interface ILmsPlaybackClient
{
    Task<int> GetPlayableItemCountAsync(
        PlayableMedia media,
        CancellationToken cancellationToken);

    Task PowerOnAsync(
        string playerId,
        CancellationToken cancellationToken);

    Task<int> GetQueueCountAsync(
        string playerId,
        CancellationToken cancellationToken);

    Task LoadAsync(
        string playerId,
        PlayableMedia media,
        CancellationToken cancellationToken);

    Task AddAsync(
        string playerId,
        PlayableMedia media,
        CancellationToken cancellationToken);

    Task InsertAsync(
        string playerId,
        PlayableMedia media,
        CancellationToken cancellationToken);

    Task ClearAsync(
        string playerId,
        CancellationToken cancellationToken);
}

public interface IPlaybackService
{
    Task<PlaybackOutcome> PlayAsync(
        string playerSelector,
        IReadOnlyList<string> references,
        CancellationToken cancellationToken);
}
