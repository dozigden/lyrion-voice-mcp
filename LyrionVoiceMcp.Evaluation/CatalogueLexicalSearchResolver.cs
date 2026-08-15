using System.Diagnostics;
using System.Globalization;
using System.Text;
using LyrionVoiceMcp.Abstractions;
using Microsoft.Data.Sqlite;

namespace LyrionVoiceMcp.Evaluation;

public sealed class CatalogueLexicalSearchResolver : IEvaluationSearchResolver
{
    private const int ResultLimit = 20;
    private const int SupportedCatalogueSchemaVersion = 4;
    private readonly IReadOnlyList<IndexedCandidate> candidates;

    private CatalogueLexicalSearchResolver(
        IReadOnlyList<IndexedCandidate> candidates,
        long preparationDurationMilliseconds)
    {
        this.candidates = candidates;
        Metrics = new EvaluationResolverMetrics(
            candidates.Count,
            preparationDurationMilliseconds,
            null);
    }

    public string Name => "catalogue-lexical-fuzzy";
    public string Version => "1";
    public EvaluationResolverMetrics Metrics { get; }

    public static async Task<CatalogueLexicalSearchResolver> CreateAsync(
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

        var loaded = new List<IndexedCandidate>();
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
        return new CatalogueLexicalSearchResolver(loaded, stopwatch.ElapsedMilliseconds);
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

    public Task<EvaluationSearchResponse> SearchAsync(
        string query,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalisedQuery = Normalise(query);
        if (normalisedQuery.Length == 0)
        {
            return Task.FromResult(new EvaluationSearchResponse([], null));
        }

        var queryTokens = SplitTokens(normalisedQuery);
        var scored = new List<ScoredCandidate>();
        for (var index = 0; index < candidates.Count; index++)
        {
            if ((index & 255) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var candidate = candidates[index];
            var score = Score(candidate, normalisedQuery, queryTokens);
            if (score > 0)
            {
                scored.Add(new ScoredCandidate(candidate, score));
            }
        }

        var results = scored
            .OrderByDescending(item => item.Score)
            .ThenBy(item => KindOrder(item.Candidate.Value.Kind))
            .ThenBy(item => item.Candidate.Value.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Candidate.StableKey, StringComparer.Ordinal)
            .Take(ResultLimit)
            .Select(item => item.Candidate.Value)
            .ToArray();
        return Task.FromResult(new EvaluationSearchResponse(results, null));
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
        List<IndexedCandidate> candidates,
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
        List<IndexedCandidate> candidates,
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
        List<IndexedCandidate> candidates,
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

    private static IndexedCandidate CreateCandidate(
        string stableKey,
        MediaEntityKind kind,
        string title,
        string? artist,
        string? album)
    {
        var normalisedTitle = Normalise(title);
        var normalisedArtist = Normalise(artist);
        var normalisedAlbum = Normalise(album);
        return new IndexedCandidate(
            stableKey,
            new EvaluationSearchCandidate(kind, title, artist, album),
            normalisedTitle,
            normalisedArtist,
            normalisedAlbum,
            Join(normalisedTitle, normalisedArtist, normalisedAlbum));
    }

    private static int Score(
        IndexedCandidate candidate,
        string query,
        IReadOnlyList<string> queryTokens)
    {
        var score = FieldScore(query, queryTokens, candidate.Title);
        score = Math.Max(score, FieldScore(query, queryTokens, candidate.Artist) - 180);
        score = Math.Max(score, FieldScore(query, queryTokens, candidate.Album) - 240);
        if (ContainsTokens(candidate.Combined, queryTokens))
        {
            score = Math.Max(score, 760);
        }

        return Math.Max(0, score);
    }

    private static int FieldScore(
        string query,
        IReadOnlyList<string> queryTokens,
        string field)
    {
        if (field.Length == 0)
        {
            return 0;
        }

        if (string.Equals(query, field, StringComparison.Ordinal))
        {
            return 1_000;
        }

        var fieldTokens = SplitTokens(field);
        if (SameTokens(queryTokens, fieldTokens))
        {
            return 950;
        }

        if (field.StartsWith(query, StringComparison.Ordinal))
        {
            return 900;
        }

        if (ContainsTokens(field, queryTokens))
        {
            return 820;
        }

        var threshold = EditDistanceThreshold(Math.Max(query.Length, field.Length));
        if (Math.Abs(query.Length - field.Length) > threshold)
        {
            return 0;
        }

        var distance = BoundedEditDistance(query, field, threshold);
        return distance <= threshold ? 760 - (distance * 30) : 0;
    }

    private static bool SameTokens(
        IReadOnlyList<string> left,
        IReadOnlyList<string> right) =>
        left.Count == right.Count
        && left.Order(StringComparer.Ordinal).SequenceEqual(
            right.Order(StringComparer.Ordinal),
            StringComparer.Ordinal);

    private static bool ContainsTokens(
        string field,
        IReadOnlyList<string> queryTokens)
    {
        var fieldTokens = SplitTokens(field);
        return queryTokens.Count > 0
            && queryTokens.All(queryToken => fieldTokens.Any(fieldToken =>
                string.Equals(queryToken, fieldToken, StringComparison.Ordinal)
                || fieldToken.StartsWith(queryToken, StringComparison.Ordinal)));
    }

    private static int EditDistanceThreshold(int length) => length switch
    {
        <= 4 => 1,
        <= 8 => 2,
        <= 16 => 3,
        _ => 4
    };

    private static int BoundedEditDistance(string left, string right, int limit)
    {
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        var current = new int[right.Length + 1];
        for (var leftIndex = 1; leftIndex <= left.Length; leftIndex++)
        {
            current[0] = leftIndex;
            var rowMinimum = current[0];
            for (var rightIndex = 1; rightIndex <= right.Length; rightIndex++)
            {
                var substitution = previous[rightIndex - 1]
                    + (left[leftIndex - 1] == right[rightIndex - 1] ? 0 : 1);
                current[rightIndex] = Math.Min(
                    Math.Min(previous[rightIndex] + 1, current[rightIndex - 1] + 1),
                    substitution);
                rowMinimum = Math.Min(rowMinimum, current[rightIndex]);
            }

            if (rowMinimum > limit)
            {
                return limit + 1;
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    private static string Normalise(string? value)
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

    private static IReadOnlyList<string> SplitTokens(string value) =>
        value.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static string Join(params string[] values) =>
        string.Join(' ', values.Where(value => value.Length > 0));

    private static int KindOrder(MediaEntityKind kind) => kind switch
    {
        MediaEntityKind.Artist => 0,
        MediaEntityKind.Album => 1,
        MediaEntityKind.Track => 2,
        MediaEntityKind.Playlist => 3,
        _ => 4
    };

    private sealed record IndexedCandidate(
        string StableKey,
        EvaluationSearchCandidate Value,
        string Title,
        string Artist,
        string Album,
        string Combined);

    private sealed record ScoredCandidate(IndexedCandidate Candidate, int Score);
}
