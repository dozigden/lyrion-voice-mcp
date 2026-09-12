using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Services;

public sealed class BrowseReferenceCodec : IBrowseReferenceCodec
{
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

        return registry.Issue(
            ReferencePrefixes.ForBrowse(value),
            value,
            value.DisplayMetadata);
    }

    public BrowseReferenceValue? TryDecode(string reference)
    {
        return registry.Resolve<BrowseReferenceValue>(reference);
    }

    private static bool IsValid(BrowseReferenceValue value)
    {
        if (value.Target is null && value.Media is null && value.ProviderTarget is null)
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

        if (media.Identity.ProviderId != media.ProviderTarget?.ProviderId) return false;

        return media.Identity.Kind is not (MediaEntityKind.Artist or MediaEntityKind.Programme);
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
