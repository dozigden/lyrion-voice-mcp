using System.Text.Json;
using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Services;

public sealed class BrowseReferenceCodec : IBrowseReferenceCodec
{
    private const string Prefix = "browse_";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Encode(BrowseReferenceValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (!IsValid(value))
        {
            throw new ArgumentException("The browse reference value is invalid.", nameof(value));
        }

        var payload = new ReferencePayload(
            value.Target?.Kind,
            value.Target?.FilterId,
            value.Target?.Offset,
            value.Media?.Identity.Kind,
            value.Media?.Identity.Id,
            value.Media?.ArtistScope,
            value.SearchCorrelationId);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        return Prefix + EncodeBase64Url(bytes);
    }

    public BrowseReferenceValue? TryDecode(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference)
            || !reference.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            var bytes = DecodeBase64Url(reference[Prefix.Length..]);
            var payload = JsonSerializer.Deserialize<ReferencePayload>(bytes, JsonOptions);
            if (payload is null)
            {
                return null;
            }

            var target = payload.TargetKind is { } targetKind
                ? new BrowseTarget(targetKind, payload.FilterId, payload.Offset ?? -1)
                : null;
            var media = payload.MediaKind is { } mediaKind
                ? new PlayableMedia(
                    new MediaIdentity(mediaKind, payload.MediaId ?? string.Empty),
                    payload.ArtistScope)
                : null;
            if (media is null
                && (payload.MediaId is not null || payload.ArtistScope is not null))
            {
                return null;
            }

            var value = new BrowseReferenceValue(
                target,
                media,
                payload.SearchCorrelationId);
            return IsValid(value) ? value : null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
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
            LmsBrowseQueryKind.AlbumArtistAlbums or
            LmsBrowseQueryKind.ArtistAlbums or
            LmsBrowseQueryKind.GenreAlbums or
            LmsBrowseQueryKind.YearAlbums or
            LmsBrowseQueryKind.AlbumTracks or
            LmsBrowseQueryKind.PlaylistTracks;
        return requiresFilter
            ? !string.IsNullOrWhiteSpace(target.FilterId)
            : target.FilterId is null;
    }

    private static string EncodeBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static byte[] DecodeBase64Url(string value)
    {
        var base64 = value
            .Replace('-', '+')
            .Replace('_', '/');
        var padding = base64.Length % 4;
        if (padding != 0)
        {
            base64 = base64.PadRight(base64.Length + (4 - padding), '=');
        }

        return Convert.FromBase64String(base64);
    }

    private sealed record ReferencePayload(
        LmsBrowseQueryKind? TargetKind,
        string? FilterId,
        int? Offset,
        MediaEntityKind? MediaKind,
        string? MediaId,
        ArtistSelectionScope? ArtistScope,
        string? SearchCorrelationId);
}
