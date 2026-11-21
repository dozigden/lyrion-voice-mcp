using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Abstractions.Providers;
using LyrionVoiceMcp.Abstractions.Providers.Qobuz;
using Microsoft.Extensions.DependencyInjection;

namespace LyrionVoiceMcp.Services.Providers.Qobuz;

internal sealed class QobuzBrowseSource(IQobuzClient client, IBrowseReferenceCodec references) : IProviderBrowseSource
{
    public string ProviderId => "qobuz";
    private abstract record Location() : ProviderBrowseTarget("qobuz");
    private sealed record Streaming : Location;
    private sealed record Releases : Location;
    private sealed record Album(QobuzAlbum Release) : Location;
    private sealed record Continuation(IReadOnlyList<Entry> Items, int Offset) : Location;
    private sealed record Entry(string Title, BrowseItemKind Kind, Location? Location = null,
        QobuzTrackTarget? Track = null, string? Artist = null, string? Album = null);

    public async Task<IReadOnlyList<BrowseItemResult>> GetRootsAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (await client.GetConfigurationStatusAsync(cancellationToken) == QobuzConfigurationStatus.Configured)
                return [Item(new Entry("Streaming", BrowseItemKind.Category, new Streaming()), null)];
        }
        catch (LmsRequestException)
        {
            // Failure of this optional provider must leave the local browse roots usable.
        }
        return [];
    }

    public async Task<BrowseOutcome> BrowseAsync(ProviderBrowseTarget target, string? correlationId,
        CancellationToken cancellationToken)
    {
        try
        {
            switch (target)
            {
                case Streaming:
                    return new BrowseSucceeded([Item(new Entry("New Releases", BrowseItemKind.Category, new Releases()), correlationId)], null);
                case Releases:
                    var albums = await client.ReadReleasesAsync(cancellationToken);
                    return Page(albums.Select(album => new Entry(album.Title, BrowseItemKind.Album, new Album(album),
                        Artist: AlbumArtist(album))).ToArray(), 0, correlationId);
                case Album album:
                    var tracks = await client.ReadTracksAsync(album.Release, cancellationToken);
                    return Page(tracks.Select(track => new Entry(track.Title, BrowseItemKind.Track,
                        Track: new QobuzTrackTarget(album.Release, track.Id), Artist: track.Artist, Album: album.Release.Title)).ToArray(), 0, correlationId);
                case Continuation page:
                    return Page(page.Items, page.Offset, correlationId);
                default:
                    return new BrowseRejected(BrowseRejectionReason.InvalidReference, "Invalid streaming browse reference.");
            }
        }
        catch (LmsRequestException exception)
        {
            return new BrowseRejected(BrowseRejectionReason.BrowseUnavailable, exception.Message);
        }
    }

    private static string? AlbumArtist(QobuzAlbum album)
    {
        if (album.Label.Split('\n', 2) is not [_, var detail]) return null;
        // The plugin optionally appends a release year to the list of album artists.
        var artist = System.Text.RegularExpressions.Regex.Replace(detail, @" ?\([0-9]{4}\)$", "").Trim();
        return artist.Length > 0 ? artist : null;
    }

    private BrowseSucceeded Page(IReadOnlyList<Entry> entries, int offset, string? correlationId)
    {
        var items = entries.Skip(offset).Take(50).Select(entry => Item(entry, correlationId)).ToArray();
        var next = offset + items.Length;
        var continuation = next < entries.Count ? references.Encode(new BrowseReferenceValue(null, null, correlationId,
            new ReferenceDisplayMetadata(ReferenceDisplayKind.Category, "More", IsContinuation: true),
            new Continuation(entries, next))) : null;
        return new BrowseSucceeded(items, continuation);
    }

    private BrowseItemResult Item(Entry entry, string? correlationId)
    {
        var kind = entry.Kind switch
        {
            BrowseItemKind.Album => ReferenceDisplayKind.Album,
            BrowseItemKind.Track => ReferenceDisplayKind.Track,
            _ => ReferenceDisplayKind.Category
        };
        var metadata = new ReferenceDisplayMetadata(kind, entry.Title, entry.Artist, entry.Album);
        var media = entry.Track is { } track ? new PlayableMedia(new MediaIdentity(MediaEntityKind.Track,
            track.TrackId, ProviderId), track) : null;
        var handle = references.Encode(new BrowseReferenceValue(null, media, correlationId, metadata, entry.Location));
        return new BrowseItemResult(handle, entry.Kind, entry.Title, entry.Artist, entry.Album, entry.Location is not null, media is not null);
    }
}

internal sealed class QobuzPlaybackSource(IQobuzClient client) : IProviderPlaybackSource
{
    public string ProviderId => "qobuz";

    public async Task<int> GetPlayableItemCountAsync(ProviderMediaTarget target, CancellationToken cancellationToken) =>
        target is QobuzTrackTarget track && (await client.ReadTracksAsync(track.Album, cancellationToken))
            .Any(item => item.Id == track.TrackId) ? 1 : 0;

    public Task SubmitAsync(string playerId, ProviderMediaTarget target, ProviderPlaybackCommand command,
        CancellationToken cancellationToken) => target is QobuzTrackTarget track
        ? client.SubmitAsync(playerId, track, command, cancellationToken)
        : throw new LmsRequestException("Invalid streaming track reference.");
}

internal static class QobuzServiceRegistration
{
    public static IServiceCollection AddQobuzServices(this IServiceCollection services)
    {
        services.AddTransient<IProviderBrowseSource, QobuzBrowseSource>();
        return services.AddTransient<IProviderPlaybackSource, QobuzPlaybackSource>();
    }
}
