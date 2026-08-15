using System.Globalization;
using System.Text.Json;
using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Lms;

public sealed class LmsCatalogueReader(
    LmsJsonRpcClient jsonRpcClient,
    LmsConnectionSettings settings,
    TimeProvider timeProvider) : ICatalogueSourceReader
{
    private const int PageSize = 500;
    private const string TrackTags = "uxoeyiqtdfTnDUESPROWCb1hzJ";

    public async Task<CatalogueImportSnapshot> ReadAsync(
        CancellationToken cancellationToken)
    {
        var initialStatus = await ReadStatusAsync(cancellationToken);
        EnsureReady(initialStatus);

        var artistLookup = await ReadCountedPagesAsync(
            "artist lookup",
            "artists_loop",
            (offset, limit) => ["artists", offset, limit, "tags:E"],
            MapArtist,
            artist => artist.SourceId,
            cancellationToken);
        var albums = await ReadCountedPagesAsync(
            "albums",
            "albums_loop",
            (offset, limit) => ["albums", offset, limit, "tags:lytjqwWES"],
            MapAlbum,
            album => album.SourceId,
            cancellationToken);
        var genres = await ReadCountedPagesAsync(
            "genres",
            "genres_loop",
            (offset, limit) => ["genres", offset, limit],
            MapGenre,
            genre => genre.SourceId,
            cancellationToken);
        var tracks = await ReadCountedPagesAsync(
            "tracks",
            "titles_loop",
            (offset, limit) => ["titles", offset, limit, $"tags:{TrackTags}"],
            MapTrack,
            track => track.SourceId,
            cancellationToken);
        var artists = SelectReferencedArtists(artistLookup, albums, tracks);
        var virtualLibraries = await ReadVirtualLibrariesAsync(cancellationToken);

        var finalStatus = await ReadStatusAsync(cancellationToken);
        EnsureStable(initialStatus, finalStatus);

        var sourceId = settings.ServerId
            ?? throw new LmsRequestException("LMS is not configured.");
        var warnings = BuildWarnings(
            initialStatus,
            artists,
            albums,
            genres,
            tracks,
            virtualLibraries);
        return new CatalogueImportSnapshot(
            new CatalogueImportSource(
                sourceId,
                "lms",
                initialStatus.Version,
                initialStatus.LastScan),
            timeProvider.GetUtcNow(),
            ReadUnixTime(initialStatus.LastScan, "server status", "lastscan"),
            artists,
            albums,
            genres,
            tracks,
            virtualLibraries,
            warnings);
    }

    private async Task<LmsCatalogueStatus> ReadStatusAsync(
        CancellationToken cancellationToken)
    {
        var result = await jsonRpcClient.SendAsync(
            ["serverstatus", 0, 0],
            cancellationToken);
        return new LmsCatalogueStatus(
            LmsJson.ReadString(result, "version"),
            LmsJson.ReadString(result, "lastscan"),
            ReadBoolean(result, "rescan") ?? false,
            ReadOptionalNonNegativeInt(result, "info total artists", "server status"),
            ReadOptionalNonNegativeInt(result, "info total albums", "server status"),
            ReadOptionalNonNegativeInt(result, "info total genres", "server status"),
            ReadOptionalNonNegativeInt(result, "info total songs", "server status"));
    }

    private async Task<IReadOnlyList<T>> ReadCountedPagesAsync<T>(
        string responseName,
        string loopName,
        Func<int, int, object[]> command,
        Func<JsonElement, T> map,
        Func<T, string> sourceId,
        CancellationToken cancellationToken)
    {
        var items = new List<T>();
        int? expectedTotal = null;

        while (expectedTotal is null || items.Count < expectedTotal)
        {
            var result = await jsonRpcClient.SendAsync(
                command(items.Count, PageSize),
                cancellationToken);
            var total = ReadRequiredNonNegativeInt(result, "count", responseName);
            if (expectedTotal is not null && expectedTotal != total)
            {
                throw new LmsRequestException(
                    $"LMS {responseName} count changed while the catalogue was being read.");
            }

            expectedTotal = total;
            if (!result.TryGetProperty(loopName, out var loop))
            {
                if (total == items.Count)
                {
                    break;
                }

                throw new LmsRequestException(
                    $"LMS {responseName} response did not include a {loopName} array.");
            }

            if (loop.ValueKind != JsonValueKind.Array)
            {
                throw new LmsRequestException(
                    $"LMS {responseName} response did not include a valid {loopName} array.");
            }

            var page = loop.EnumerateArray().Select(map).ToArray();
            if (page.Length == 0 && items.Count < total)
            {
                throw new LmsRequestException(
                    $"LMS {responseName} response ended before its reported count was reached.");
            }

            if (items.Count + page.Length > total)
            {
                throw new LmsRequestException(
                    $"LMS {responseName} response exceeded its reported count.");
            }

            items.AddRange(page);
        }

        EnsureUnique(items, responseName, sourceId);
        return items;
    }

    private async Task<IReadOnlyList<CatalogueImportVirtualLibrary>> ReadVirtualLibrariesAsync(
        CancellationToken cancellationToken)
    {
        var libraries = new List<CatalogueImportVirtualLibrary>();
        var result = await jsonRpcClient.SendAsync(
            ["libraries"],
            cancellationToken);
        if (!result.TryGetProperty("folder_loop", out var loop)
            || loop.ValueKind != JsonValueKind.Array)
        {
            throw new LmsRequestException(
                "LMS virtual libraries response did not include a valid folder_loop array.");
        }

        var identities = loop.EnumerateArray().Select(MapVirtualLibraryIdentity).ToArray();
        EnsureUnique(identities, "virtual libraries", library => library.SourceId);
        foreach (var library in identities)
        {
            var trackIds = await ReadCountedPagesAsync(
                "virtual library members",
                "titles_loop",
                (offset, limit) =>
                [
                    "titles",
                    offset,
                    limit,
                    $"library_id:{library.SourceId}",
                    "tags:II"
                ],
                item => ReadRequiredString(item, "id", "virtual library members"),
                trackId => trackId,
                cancellationToken);
            libraries.Add(new CatalogueImportVirtualLibrary(
                library.SourceId,
                library.Name,
                trackIds));
        }

        return libraries;
    }

    private static CatalogueImportArtist MapArtist(JsonElement item) =>
        new(
            ReadRequiredString(item, "id", "artists"),
            ReadRequiredString(item, "artist", "artists"),
            ReadOptionalString(item, "extid"));

    private static CatalogueImportAlbum MapAlbum(JsonElement item) =>
        new(
            ReadRequiredString(item, "id", "albums"),
            ReadRequiredStringWithFallback(item, "title", "album", "albums"),
            ReadOptionalString(item, "artist_id"),
            ReadPositiveInt(item, "year", "albums"),
            ReadPositiveInt(item, "disccount", "albums"),
            ReadBoolean(item, "compilation"),
            ReadOptionalString(item, "release_type"),
            ReadOptionalString(item, "artwork_track_id"),
            ReadOptionalString(item, "extid"));

    private static CatalogueImportGenre MapGenre(JsonElement item) =>
        new(
            ReadRequiredString(item, "id", "genres"),
            ReadRequiredString(item, "genre", "genres"));

    private static CatalogueImportTrack MapTrack(JsonElement item)
    {
        var rating = ReadOptionalNonNegativeInt(item, "rating", "tracks");
        if (rating > 100)
        {
            throw new LmsRequestException(
                "LMS tracks response contained a rating outside the 0 to 100 range.");
        }

        return new CatalogueImportTrack(
            ReadRequiredString(item, "id", "tracks"),
            ReadRequiredString(item, "title", "tracks"),
            ReadOptionalString(item, "subtitle"),
            ReadRequiredString(item, "url", "tracks"),
            ReadOptionalString(item, "type"),
            ReadRequiredBoolean(item, "remote", "tracks"),
            ReadOptionalString(item, "extid"),
            ReadOptionalString(item, "album_id"),
            ReadPositiveInt(item, "year", "tracks"),
            ReadPositiveInt(item, "disc", "tracks"),
            ReadPositiveInt(item, "disccount", "tracks"),
            ReadPositiveInt(item, "tracknum", "tracks"),
            ReadOptionalNonNegativeDouble(item, "duration", "tracks"),
            ReadOptionalNonNegativeLong(item, "filesize", "tracks"),
            ReadOptionalNonNegativeInt(item, "samplerate", "tracks"),
            ReadUnixTime(item, "addedTime", "tracks"),
            ReadUnixTime(item, "modificationTime", "tracks"),
            ReadUnixTime(item, "lastUpdated", "tracks"),
            ReadOptionalString(item, "release_type"),
            ReadBoolean(item, "compilation"),
            ReadOptionalString(item, "artwork_track_id"),
            ReadOptionalString(item, "work_id"),
            ReadOptionalString(item, "work"),
            ReadOptionalString(item, "performance"),
            ReadOptionalString(item, "grouping"),
            ReadDelimitedIds(item, "artist_ids"),
            ReadDelimitedIds(item, "genre_ids"),
            [
                new CatalogueImportTrackStatistics(
                    "lms-core",
                    rating,
                    ReadOptionalNonNegativeInt(item, "playcount", "tracks"),
                    null)
            ]);
    }

    private static CatalogueImportVirtualLibrary MapVirtualLibraryIdentity(JsonElement item) =>
        new(
            ReadRequiredString(item, "id", "virtual libraries"),
            ReadRequiredString(item, "name", "virtual libraries"),
            []);

    private static IReadOnlyList<CatalogueImportArtist> SelectReferencedArtists(
        IReadOnlyList<CatalogueImportArtist> artistLookup,
        IReadOnlyList<CatalogueImportAlbum> albums,
        IReadOnlyList<CatalogueImportTrack> tracks)
    {
        var referencedIds = tracks
            .SelectMany(track => track.ArtistSourceIds)
            .Concat(albums
                .Select(album => album.AlbumArtistSourceId)
                .OfType<string>())
            .ToHashSet(StringComparer.Ordinal);
        return artistLookup
            .Where(artist => referencedIds.Contains(artist.SourceId))
            .ToArray();
    }

    private static IReadOnlyList<string> ReadDelimitedIds(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var property))
        {
            return [];
        }

        return ReadDelimitedIds(property, name, "tracks");
    }

    private static IReadOnlyList<string> ReadDelimitedIds(
        JsonElement property,
        string name,
        string responseName)
    {
        if (property.ValueKind == JsonValueKind.Array)
        {
            return property.EnumerateArray()
                .Select(value => ReadScalarString(value, name, responseName))
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        var value = ReadScalarString(property, name, responseName);
        return value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<CatalogueImportWarning> BuildWarnings(
        LmsCatalogueStatus status,
        IReadOnlyList<CatalogueImportArtist> artists,
        IReadOnlyList<CatalogueImportAlbum> albums,
        IReadOnlyList<CatalogueImportGenre> genres,
        IReadOnlyList<CatalogueImportTrack> tracks,
        IReadOnlyList<CatalogueImportVirtualLibrary> virtualLibraries)
    {
        var artistIds = artists.Select(item => item.SourceId).ToHashSet(StringComparer.Ordinal);
        var albumIds = albums.Select(item => item.SourceId).ToHashSet(StringComparer.Ordinal);
        var genreIds = genres.Select(item => item.SourceId).ToHashSet(StringComparer.Ordinal);
        var trackIds = tracks.Select(item => item.SourceId).ToHashSet(StringComparer.Ordinal);
        var warnings = new List<CatalogueImportWarning>();

        AddWarning(
            warnings,
            "missing-album",
            "Track album references were not present in the imported album set.",
            tracks.Count(track => track.AlbumSourceId is not null && !albumIds.Contains(track.AlbumSourceId)));
        AddWarning(
            warnings,
            "missing-artist",
            "Track or album artist references were not present in the imported artist set.",
            albums.Count(album => album.AlbumArtistSourceId is not null
                                  && !artistIds.Contains(album.AlbumArtistSourceId))
            + tracks.Sum(track => track.ArtistSourceIds.Count(
                artistId => !artistIds.Contains(artistId))));
        AddWarning(
            warnings,
            "missing-genre",
            "Track genre references were not present in the imported genre set.",
            tracks.Sum(track => track.GenreSourceIds.Count(genreId => !genreIds.Contains(genreId))));
        AddWarning(
            warnings,
            "missing-library-track",
            "Virtual-library memberships referenced tracks outside the imported track set.",
            virtualLibraries.Sum(library => library.TrackSourceIds.Count(trackId => !trackIds.Contains(trackId))));
        AddWarning(
            warnings,
            "server-total-mismatch",
            "Server-status totals differed from the corresponding catalogue query totals.",
            CountServerTotalMismatches(
                status,
                albums.Count,
                genres.Count,
                tracks.Count));
        return warnings;
    }

    private static void AddWarning(
        ICollection<CatalogueImportWarning> warnings,
        string code,
        string message,
        int occurrences)
    {
        if (occurrences > 0)
        {
            warnings.Add(new CatalogueImportWarning(code, message, occurrences));
        }
    }

    private static void EnsureReady(LmsCatalogueStatus status)
    {
        if (status.Rescan)
        {
            throw new LmsRequestException(
                "LMS catalogue cannot be read while a library scan is in progress.");
        }
    }

    private static void EnsureStable(
        LmsCatalogueStatus initial,
        LmsCatalogueStatus final)
    {
        EnsureReady(final);
        if (!string.Equals(initial.Version, final.Version, StringComparison.Ordinal)
            || !string.Equals(initial.LastScan, final.LastScan, StringComparison.Ordinal)
            || initial.ArtistCount != final.ArtistCount
            || initial.AlbumCount != final.AlbumCount
            || initial.GenreCount != final.GenreCount
            || initial.TrackCount != final.TrackCount)
        {
            throw new LmsRequestException(
                "LMS catalogue changed while it was being read.");
        }
    }

    private static int CountServerTotalMismatches(
        LmsCatalogueStatus status,
        int albumCount,
        int genreCount,
        int trackCount)
    {
        var mismatches = 0;
        mismatches += status.AlbumCount is not null && status.AlbumCount != albumCount ? 1 : 0;
        mismatches += status.GenreCount is not null && status.GenreCount != genreCount ? 1 : 0;
        mismatches += status.TrackCount is not null && status.TrackCount != trackCount ? 1 : 0;
        return mismatches;
    }

    private static void EnsureUnique<T>(
        IReadOnlyList<T> items,
        string responseName,
        Func<T, string> sourceId)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (!ids.Add(sourceId(item)))
            {
                throw new LmsRequestException(
                    $"LMS {responseName} response contained a duplicate id.");
            }
        }
    }

    private static string ReadRequiredString(
        JsonElement item,
        string name,
        string responseName)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            throw new LmsRequestException(
                $"LMS {responseName} response contained an invalid item.");
        }

        var value = ReadOptionalString(item, name);
        if (value is null)
        {
            throw new LmsRequestException(
                $"LMS {responseName} response contained an item without {name}.");
        }

        return value;
    }

    private static string ReadRequiredStringWithFallback(
        JsonElement item,
        string name,
        string fallbackName,
        string responseName) =>
        ReadOptionalString(item, name)
        ?? ReadRequiredString(item, fallbackName, responseName);

    private static string? ReadOptionalString(JsonElement item, string name)
    {
        var value = LmsJson.ReadString(item, name)?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static int ReadRequiredNonNegativeInt(
        JsonElement item,
        string name,
        string responseName) =>
        ReadOptionalNonNegativeInt(item, name, responseName)
        ?? throw new LmsRequestException(
            $"LMS {responseName} response did not include a valid {name}.");

    private static int? ReadOptionalNonNegativeInt(
        JsonElement item,
        string name,
        string responseName)
    {
        if (!item.TryGetProperty(name, out var property))
        {
            return null;
        }

        var value = ReadInteger(property, name, responseName);
        if (value < 0 || value > int.MaxValue)
        {
            throw new LmsRequestException(
                $"LMS {responseName} response contained an invalid {name} value.");
        }

        return (int)value;
    }

    private static int? ReadPositiveInt(
        JsonElement item,
        string name,
        string responseName)
    {
        var value = ReadOptionalNonNegativeInt(item, name, responseName);
        return value is > 0 ? value : null;
    }

    private static long? ReadOptionalNonNegativeLong(
        JsonElement item,
        string name,
        string responseName)
    {
        if (!item.TryGetProperty(name, out var property))
        {
            return null;
        }

        var value = ReadInteger(property, name, responseName);
        if (value < 0)
        {
            throw new LmsRequestException(
                $"LMS {responseName} response contained an invalid {name} value.");
        }

        return value;
    }

    private static long ReadInteger(
        JsonElement property,
        string name,
        string responseName)
    {
        if (property.ValueKind == JsonValueKind.Number
            && property.TryGetInt64(out var numberValue))
        {
            return numberValue;
        }

        if (property.ValueKind == JsonValueKind.String
            && long.TryParse(
                property.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var stringValue))
        {
            return stringValue;
        }

        throw new LmsRequestException(
            $"LMS {responseName} response contained an invalid {name} value.");
    }

    private static double? ReadOptionalNonNegativeDouble(
        JsonElement item,
        string name,
        string responseName)
    {
        if (!item.TryGetProperty(name, out var property))
        {
            return null;
        }

        double value;
        if (property.ValueKind == JsonValueKind.Number
            && property.TryGetDouble(out var numberValue))
        {
            value = numberValue;
        }
        else if (property.ValueKind == JsonValueKind.String
                 && double.TryParse(
                     property.GetString(),
                     NumberStyles.Float,
                     CultureInfo.InvariantCulture,
                     out var stringValue))
        {
            value = stringValue;
        }
        else
        {
            throw new LmsRequestException(
                $"LMS {responseName} response contained an invalid {name} value.");
        }

        if (!double.IsFinite(value) || value < 0)
        {
            throw new LmsRequestException(
                $"LMS {responseName} response contained an invalid {name} value.");
        }

        return value;
    }

    private static bool ReadRequiredBoolean(
        JsonElement item,
        string name,
        string responseName) =>
        ReadBoolean(item, name)
        ?? throw new LmsRequestException(
            $"LMS {responseName} response contained an item without a valid {name} value.");

    private static bool? ReadBoolean(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when property.TryGetInt32(out var value) && value == 1 => true,
            JsonValueKind.Number when property.TryGetInt32(out var value) && value == 0 => false,
            JsonValueKind.String when property.GetString() == "1" => true,
            JsonValueKind.String when property.GetString() == "0" => false,
            _ => throw new LmsRequestException(
                $"LMS response contained an invalid {name} value.")
        };
    }

    private static DateTimeOffset? ReadUnixTime(
        JsonElement item,
        string name,
        string responseName)
    {
        if (!item.TryGetProperty(name, out var property))
        {
            return null;
        }

        return ReadUnixTime(ReadScalarString(property, name, responseName), responseName, name);
    }

    private static DateTimeOffset? ReadUnixTime(
        string? value,
        string responseName,
        string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
            || seconds < 0)
        {
            throw new LmsRequestException(
                $"LMS {responseName} response contained an invalid {name} value.");
        }

        if (seconds == 0)
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new LmsRequestException(
                $"LMS {responseName} response contained an invalid {name} value.",
                exception);
        }
    }

    private static string ReadScalarString(
        JsonElement property,
        string name,
        string responseName)
    {
        var value = property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            _ => null
        };
        if (value is null)
        {
            throw new LmsRequestException(
                $"LMS {responseName} response contained an invalid {name} value.");
        }

        return value.Trim();
    }

    private sealed record LmsCatalogueStatus(
        string? Version,
        string? LastScan,
        bool Rescan,
        int? ArtistCount,
        int? AlbumCount,
        int? GenreCount,
        int? TrackCount);
}
