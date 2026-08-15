using System.Diagnostics;
using System.Globalization;
using System.Text;
using LyrionVoiceMcp.Abstractions;
using Microsoft.Data.Sqlite;

namespace LyrionVoiceMcp.Evaluation;

internal sealed class CatalogueEvaluationIndex(
    IReadOnlyList<CatalogueEvaluationCandidate> candidates,
    long preparationDurationMilliseconds)
{
    private const int SupportedCatalogueSchemaVersion = 4;

    public IReadOnlyList<CatalogueEvaluationCandidate> Candidates { get; } = candidates;
    public long PreparationDurationMilliseconds { get; } = preparationDurationMilliseconds;

    public static async Task<CatalogueEvaluationIndex> LoadAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(databasePath))
        {
            throw new InvalidOperationException(
                $"Catalogue database does not exist: {databasePath}");
        }

        var stopwatch = Stopwatch.StartNew();
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSupportedSchemaAsync(connection, cancellationToken);
        var refreshId = await ReadReadyRefreshIdAsync(connection, cancellationToken);

        var loaded = new List<CatalogueEvaluationCandidate>();
        await ReadArtistsAsync(connection, loaded, cancellationToken);
        await ReadAlbumsAsync(connection, loaded, cancellationToken);
        await ReadTracksAsync(connection, loaded, cancellationToken);
        var finalRefreshId = await ReadReadyRefreshIdAsync(connection, cancellationToken);
        if (!string.Equals(refreshId, finalRefreshId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Catalogue refresh state changed while evaluation candidates were being loaded.");
        }

        stopwatch.Stop();
        return new CatalogueEvaluationIndex(loaded, stopwatch.ElapsedMilliseconds);
    }

    private static async Task EnsureSupportedSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT version FROM catalogue_schema LIMIT 1;";
        var value = await command.ExecuteScalarAsync(cancellationToken);
        var version = Convert.ToInt32(value, CultureInfo.InvariantCulture);
        if (version != SupportedCatalogueSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Catalogue schema {version} is not supported by this benchmark resolver.");
        }
    }

    private static async Task<string> ReadReadyRefreshIdAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT refresh.id, refresh.status
            FROM catalogue_refresh_runs refresh
            WHERE EXISTS (SELECT 1 FROM catalogue_state WHERE id = 1)
            ORDER BY refresh.started_at DESC, refresh.id DESC
            LIMIT 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)
            || !string.Equals(reader.GetString(1), "succeeded", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Catalogue database is not converged at a successful refresh.");
        }

        return reader.GetString(0);
    }

    private static async Task ReadArtistsAsync(
        SqliteConnection connection,
        List<CatalogueEvaluationCandidate> candidates,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT source_id, name
            FROM catalogue_artists
            ORDER BY source_id;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            candidates.Add(CreateCandidate(
                $"artist:{reader.GetString(0)}",
                MediaEntityKind.Artist,
                reader.GetString(1),
                null,
                null));
        }
    }

    private static async Task ReadAlbumsAsync(
        SqliteConnection connection,
        List<CatalogueEvaluationCandidate> candidates,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT album.source_id, album.title, artist.name
            FROM catalogue_albums album
            LEFT JOIN catalogue_artists artist
                ON artist.source_id = album.album_artist_source_id
            ORDER BY album.source_id;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            candidates.Add(CreateCandidate(
                $"album:{reader.GetString(0)}",
                MediaEntityKind.Album,
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                null));
        }
    }

    private static async Task ReadTracksAsync(
        SqliteConnection connection,
        List<CatalogueEvaluationCandidate> candidates,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT track.source_id,
                   track.title,
                   COALESCE(
                       (SELECT GROUP_CONCAT(artist.name, ', ')
                        FROM catalogue_track_artists track_artist
                        JOIN catalogue_artists artist
                          ON artist.source_id = track_artist.artist_source_id
                        WHERE track_artist.track_source_id = track.source_id),
                       album_artist.name),
                   album.title
            FROM catalogue_tracks track
            LEFT JOIN catalogue_albums album
              ON album.source_id = track.album_source_id
            LEFT JOIN catalogue_artists album_artist
              ON album_artist.source_id = album.album_artist_source_id
            ORDER BY track.source_id;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            candidates.Add(CreateCandidate(
                $"track:{reader.GetString(0)}",
                MediaEntityKind.Track,
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        }
    }

    private static CatalogueEvaluationCandidate CreateCandidate(
        string stableKey,
        MediaEntityKind kind,
        string title,
        string? artist,
        string? album)
    {
        var normalisedTitle = CatalogueEvaluationText.Normalise(title);
        var normalisedArtist = CatalogueEvaluationText.Normalise(artist);
        var normalisedAlbum = CatalogueEvaluationText.Normalise(album);
        return new CatalogueEvaluationCandidate(
            stableKey,
            new EvaluationSearchCandidate(kind, title, artist, album),
            normalisedTitle,
            normalisedArtist,
            normalisedAlbum,
            CatalogueEvaluationText.Join(normalisedTitle, normalisedArtist, normalisedAlbum));
    }
}

internal sealed record CatalogueEvaluationCandidate(
    string StableKey,
    EvaluationSearchCandidate Value,
    string Title,
    string Artist,
    string Album,
    string Combined);

internal static class CatalogueEvaluationText
{
    public static string Normalise(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var previousWasSpace = true;
        foreach (var rune in decomposed.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (Rune.IsLetterOrDigit(rune))
            {
                builder.Append(Rune.ToLowerInvariant(rune));
                previousWasSpace = false;
            }
            else if (!previousWasSpace)
            {
                builder.Append(' ');
                previousWasSpace = true;
            }
        }

        return builder.ToString().Trim();
    }

    public static IReadOnlyList<string> SplitTokens(string value) =>
        value.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    public static string Join(params string[] values) =>
        string.Join(' ', values.Where(value => value.Length > 0));
}
