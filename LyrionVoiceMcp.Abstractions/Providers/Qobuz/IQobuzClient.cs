namespace LyrionVoiceMcp.Abstractions.Providers.Qobuz;

public interface IQobuzClient
{
    Task<IReadOnlyList<QobuzAlbum>> ReadReleasesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<QobuzTrack>> ReadTracksAsync(QobuzAlbum album, CancellationToken cancellationToken);
    Task SubmitAsync(string playerId, QobuzTrackTarget track, ProviderPlaybackCommand command, CancellationToken cancellationToken);
    Task<QobuzConfigurationStatus> GetConfigurationStatusAsync(CancellationToken cancellationToken);
}

public enum QobuzConfigurationStatus
{
    NotInstalled,
    NotConfigured,
    Configured
}

public sealed record QobuzAlbum(string Label, string? Artwork)
{
    public string Title => Label.Split('\n')[0];
}

public sealed record QobuzTrack(string Id, string Url, string Title, string? Artist);
public sealed record QobuzTrackTarget(QobuzAlbum Album, string TrackId) : ProviderMediaTarget("qobuz");
