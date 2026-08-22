using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Services;

public sealed class BrowseReferenceCodec : IBrowseReferenceCodec
{
    private const string Prefix = "browse_";
    private readonly ReferenceHandleRegistry registry;

    internal BrowseReferenceCodec(ReferenceHandleRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        this.registry = registry;
    }

    public string Encode(BrowseReferenceValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (!IsValid(value))
        {
            throw new ArgumentException("The browse reference value is invalid.", nameof(value));
        }

        return registry.Issue(Prefix, value);
    }

    public BrowseReferenceValue? TryDecode(string reference)
    {
        return registry.Resolve<BrowseReferenceValue>(Prefix, reference);
    }

    private static bool IsValid(BrowseReferenceValue value)
    {
        if (value.Target is null && value.Media is null)
        {
            return false;
        }

        if (value.SearchCorrelationId is { } correlationId
            && !Guid.TryParseExact(correlationId, "N", out _))
        {
            return false;
        }

        if (value.Target is { } target)
        {
            if (!Enum.IsDefined(target.Kind)
                || target.Offset < 0
                || !HasValidFilter(target))
            {
                return false;
            }
        }

        if (value.Media is not { } media)
        {
            return true;
        }

        if (!Enum.IsDefined(media.Identity.Kind)
            || string.IsNullOrWhiteSpace(media.Identity.Id))
        {
            return false;
        }

        return media.ArtistScope is not { } artistScope
            || Enum.IsDefined(artistScope)
                && media.Identity.Kind == MediaEntityKind.Artist;
    }

    private static bool HasValidFilter(BrowseTarget target)
    {
        var requiresFilter = target.Kind is
            BrowseTargetKind.AlbumArtistAlbums or
            BrowseTargetKind.ArtistAlbums or
            BrowseTargetKind.GenreAlbums or
            BrowseTargetKind.YearAlbums or
            BrowseTargetKind.AlbumTracks or
            BrowseTargetKind.PlaylistTracks or
            BrowseTargetKind.RatingTracks;
        return requiresFilter
            ? !string.IsNullOrWhiteSpace(target.FilterId)
            : target.FilterId is null;
    }
}
