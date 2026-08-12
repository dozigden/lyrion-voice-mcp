using System.Globalization;
using LyrionVoiceMcp.Abstractions;
using Microsoft.Data.Sqlite;

namespace LyrionVoiceMcp.Persistence;

public sealed class SqliteSearchObservationStore(
    SearchObservationSettings settings,
    TimeProvider timeProvider) : ISearchObservationStore
{
    private const int SchemaVersion = 2;
    private readonly string connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = settings.DatabasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Shared,
        ForeignKeys = true
    }.ToString();

    public int RetentionDays => settings.RetentionDays;

    public async Task InitialiseAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(settings.DatabasePath)!);
        await using var connection = await OpenAsync(cancellationToken);
        await ExecuteAsync(connection, "PRAGMA journal_mode = WAL;", cancellationToken);
        await ExecuteAsync(connection, SchemaSql, cancellationToken);

        await using var version = connection.CreateCommand();
        version.CommandText = "SELECT version FROM search_observation_schema LIMIT 1;";
        var value = await version.ExecuteScalarAsync(cancellationToken);
        var currentVersion = Convert.ToInt32(value, CultureInfo.InvariantCulture);
        if (currentVersion == 1)
        {
            await ExecuteAsync(connection, MigrationVersion2Sql, cancellationToken);
            currentVersion = 2;
        }

        if (currentVersion != SchemaVersion)
        {
            throw new InvalidOperationException("The search observation database schema is not supported.");
        }

        await DeleteExpiredAsync(connection, cancellationToken);
    }

    public async Task RecordAsync(
        SearchObservation observation,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await DeleteExpiredAsync(connection, cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO search_observations (
                    id, created_at, original_query, normalised_query, requested_kind, provider, collection, resolver, resolver_version,
                    status, failure_message, total_duration_ms, retrieval_duration_ms, processing_duration_ms)
                VALUES ($id, $createdAt, $originalQuery, $normalisedQuery, $requestedKind, $provider, $collection, $resolver, $resolverVersion,
                    $status, $failureMessage, $totalDuration, $retrievalDuration, $processingDuration);
                """;
            Add(command, "$id", observation.Id);
            Add(command, "$createdAt", FormatDate(observation.CreatedAt));
            Add(command, "$originalQuery", observation.OriginalQuery);
            Add(command, "$normalisedQuery", observation.NormalisedQuery);
            Add(command, "$requestedKind", observation.RequestedKind is null ? null : ToText(observation.RequestedKind.Value));
            Add(command, "$provider", observation.Provider);
            Add(command, "$collection", observation.Collection);
            Add(command, "$resolver", observation.Resolver);
            Add(command, "$resolverVersion", observation.ResolverVersion);
            Add(command, "$status", ToText(observation.Status));
            Add(command, "$failureMessage", observation.FailureMessage);
            Add(command, "$totalDuration", observation.TotalDurationMilliseconds);
            Add(command, "$retrievalDuration", observation.RetrievalDurationMilliseconds);
            Add(command, "$processingDuration", observation.ProcessingDurationMilliseconds);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        for (var index = 0; index < observation.Requests.Count; index++)
        {
            var request = observation.Requests[index];
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO search_observation_requests (
                    observation_id, sequence, source, command, status, failure_message, duration_ms, result_count)
                VALUES ($observationId, $sequence, $source, $command, $status, $failureMessage, $duration, $resultCount);
                """;
            Add(command, "$observationId", observation.Id);
            Add(command, "$sequence", index + 1);
            Add(command, "$source", request.Source);
            Add(command, "$command", request.Command);
            Add(command, "$status", ToText(request.Status));
            Add(command, "$failureMessage", request.FailureMessage);
            Add(command, "$duration", request.DurationMilliseconds);
            Add(command, "$resultCount", request.ResultCount);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var candidate in observation.Candidates)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO search_observation_candidates (
                    observation_id, position, correlation_id, kind, media_id, title, artist, album, selected_at)
                VALUES ($observationId, $position, $correlationId, $kind, $mediaId, $title, $artist, $album, $selectedAt);
                """;
            Add(command, "$observationId", observation.Id);
            Add(command, "$position", candidate.Position);
            Add(command, "$correlationId", candidate.CorrelationId);
            Add(command, "$kind", ToText(candidate.Identity.Kind));
            Add(command, "$mediaId", candidate.Identity.Id);
            Add(command, "$title", candidate.Title);
            Add(command, "$artist", candidate.Artist);
            Add(command, "$album", candidate.Album);
            Add(command, "$selectedAt", candidate.SelectedAt is null ? null : FormatDate(candidate.SelectedAt.Value));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task MarkSelectedAsync(
        IReadOnlyCollection<string> correlationIds,
        DateTimeOffset selectedAt,
        CancellationToken cancellationToken)
    {
        if (correlationIds.Count == 0)
        {
            return;
        }

        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        foreach (var correlationId in correlationIds.Distinct(StringComparer.Ordinal))
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE search_observation_candidates
                SET selected_at = COALESCE(selected_at, $selectedAt)
                WHERE correlation_id = $correlationId;
                """;
            Add(command, "$selectedAt", FormatDate(selectedAt));
            Add(command, "$correlationId", correlationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<SearchObservationPage> BrowseAsync(
        SearchObservationQuery query,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var where = BuildWhere(query);
        var total = await CountAsync(connection, where.Sql, where.Parameters, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT o.id, o.created_at, o.original_query, o.resolver, o.resolver_version, o.status,
                   COUNT(c.correlation_id), MIN(CASE WHEN c.selected_at IS NOT NULL THEN c.position END),
                   o.total_duration_ms, o.review_classification, o.include_in_evaluation
            FROM search_observations o
            LEFT JOIN search_observation_candidates c ON c.observation_id = o.id
            {where.Sql}
            GROUP BY o.id
            ORDER BY o.created_at DESC
            LIMIT $limit OFFSET $offset;
            """;
        AddParameters(command, where.Parameters);
        Add(command, "$limit", query.Limit);
        Add(command, "$offset", query.Offset);

        var items = new List<SearchObservationSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new SearchObservationSummary(
                reader.GetString(0),
                ParseDate(reader.GetString(1)),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                ParseStatus(reader.GetString(5)),
                reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetInt32(7),
                reader.GetInt64(8),
                reader.IsDBNull(9) ? null : ParseClassification(reader.GetString(9)),
                reader.GetInt32(10) != 0));
        }

        return new SearchObservationPage(items, total, query.Offset, query.Limit);
    }

    public async Task<SearchObservation?> GetAsync(
        string id,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM search_observations WHERE id = $id;";
        Add(command, "$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var core = ReadCore(reader);
        await reader.DisposeAsync();
        var requests = await ReadRequestsAsync(connection, id, cancellationToken);
        var candidates = await ReadCandidatesAsync(connection, id, cancellationToken);
        return core with { Requests = requests, Candidates = candidates };
    }

    public async Task<bool> SaveReviewAsync(
        string id,
        SearchObservationReview review,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE search_observations SET
                review_classification = $classification,
                expected_correlation_id = $expectedCorrelationId,
                expected_kind = $expectedKind,
                expected_title = $expectedTitle,
                expected_artist = $expectedArtist,
                expected_album = $expectedAlbum,
                review_notes = $notes,
                include_in_evaluation = $include,
                reviewed_at = $reviewedAt
            WHERE id = $id;
            """;
        Add(command, "$classification", ToText(review.Classification));
        Add(command, "$expectedCorrelationId", review.ExpectedCorrelationId);
        Add(command, "$expectedKind", review.ExpectedKind is null ? null : ToText(review.ExpectedKind.Value));
        Add(command, "$expectedTitle", review.ExpectedTitle);
        Add(command, "$expectedArtist", review.ExpectedArtist);
        Add(command, "$expectedAlbum", review.ExpectedAlbum);
        Add(command, "$notes", review.Notes);
        Add(command, "$include", review.IncludeInEvaluation ? 1 : 0);
        Add(command, "$reviewedAt", FormatDate(review.ReviewedAt));
        Add(command, "$id", id);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<IReadOnlyList<SearchEvaluationCase>> ExportAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT o.id, o.original_query, o.review_classification, o.expected_correlation_id,
                   o.expected_kind, o.expected_title, o.expected_artist, o.expected_album,
                   c.position, c.correlation_id, c.kind, c.title, c.artist, c.album, c.selected_at
            FROM search_observations o
            LEFT JOIN search_observation_candidates c ON c.observation_id = o.id
            WHERE o.status = 'completed'
              AND o.review_classification IS NOT NULL
              AND o.include_in_evaluation = 1
            ORDER BY o.created_at, o.id, c.position;
            """;
        var exported = new List<SearchEvaluationCase>();
        ExportCaseBuilder? current = null;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetString(0);
            if (current?.Id != id)
            {
                if (current is not null)
                {
                    exported.Add(current.Build());
                }

                current = new ExportCaseBuilder(
                    id,
                    reader.GetString(1),
                    ParseClassification(reader.GetString(2)),
                    reader.IsDBNull(4) ? null : ParseKind(reader.GetString(4)),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.IsDBNull(3) ? null : reader.GetString(3));
            }

            if (!reader.IsDBNull(8))
            {
                current!.Candidates.Add(new EvaluationCandidate(
                    reader.GetInt32(8),
                    ParseKind(reader.GetString(10)),
                    reader.GetString(11),
                    reader.IsDBNull(12) ? null : reader.GetString(12),
                    reader.IsDBNull(13) ? null : reader.GetString(13),
                    !reader.IsDBNull(14),
                    string.Equals(reader.GetString(9), current.ExpectedCorrelationId, StringComparison.Ordinal)));
            }
        }

        if (current is not null)
        {
            exported.Add(current.Build());
        }

        return exported;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await ExecuteAsync(connection, "PRAGMA foreign_keys = ON;", cancellationToken);
        return connection;
    }

    private async Task DeleteExpiredAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM search_observations WHERE created_at < $cutoff;";
        Add(command, "$cutoff", FormatDate(timeProvider.GetUtcNow().AddDays(-RetentionDays)));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static SearchObservation ReadCore(SqliteDataReader reader)
    {
        SearchObservationReview? review = null;
        if (!reader.IsDBNull(reader.GetOrdinal("review_classification")))
        {
            review = new SearchObservationReview(
                ParseClassification(reader.GetString(reader.GetOrdinal("review_classification"))),
                GetNullableString(reader, "expected_correlation_id"),
                GetNullableString(reader, "expected_kind") is { } kind ? ParseKind(kind) : null,
                GetNullableString(reader, "expected_title"),
                GetNullableString(reader, "expected_artist"),
                GetNullableString(reader, "expected_album"),
                GetNullableString(reader, "review_notes"),
                reader.GetInt32(reader.GetOrdinal("include_in_evaluation")) != 0,
                ParseDate(reader.GetString(reader.GetOrdinal("reviewed_at"))));
        }

        return new SearchObservation(
            reader.GetString(reader.GetOrdinal("id")),
            ParseDate(reader.GetString(reader.GetOrdinal("created_at"))),
            reader.GetString(reader.GetOrdinal("original_query")),
            reader.GetString(reader.GetOrdinal("normalised_query")),
            GetNullableString(reader, "requested_kind") is { } kindText ? ParseKind(kindText) : null,
            reader.GetString(reader.GetOrdinal("provider")),
            reader.GetString(reader.GetOrdinal("collection")),
            reader.GetString(reader.GetOrdinal("resolver")),
            reader.GetString(reader.GetOrdinal("resolver_version")),
            ParseStatus(reader.GetString(reader.GetOrdinal("status"))),
            GetNullableString(reader, "failure_message"),
            reader.GetInt64(reader.GetOrdinal("total_duration_ms")),
            reader.GetInt64(reader.GetOrdinal("retrieval_duration_ms")),
            reader.GetInt64(reader.GetOrdinal("processing_duration_ms")),
            [],
            [],
            review);
    }

    private static async Task<IReadOnlyList<LmsSearchRequestObservation>> ReadRequestsAsync(
        SqliteConnection connection,
        string id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT source, command, status, failure_message, duration_ms, result_count
            FROM search_observation_requests WHERE observation_id = $id ORDER BY sequence;
            """;
        Add(command, "$id", id);
        var items = new List<LmsSearchRequestObservation>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new LmsSearchRequestObservation(
                reader.GetString(0),
                reader.GetString(1),
                ParseRequestStatus(reader.GetString(2)),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetInt64(4),
                reader.GetInt32(5)));
        }

        return items;
    }

    private static async Task<IReadOnlyList<SearchObservationCandidate>> ReadCandidatesAsync(
        SqliteConnection connection,
        string id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT position, correlation_id, kind, media_id, title, artist, album, selected_at
            FROM search_observation_candidates WHERE observation_id = $id ORDER BY position;
            """;
        Add(command, "$id", id);
        var items = new List<SearchObservationCandidate>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new SearchObservationCandidate(
                reader.GetInt32(0), reader.GetString(1),
                new MediaIdentity(ParseKind(reader.GetString(2)), reader.GetString(3)),
                reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : ParseDate(reader.GetString(7))));
        }

        return items;
    }

    private static async Task<int> CountAsync(
        SqliteConnection connection,
        string where,
        IReadOnlyDictionary<string, object> parameters,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM search_observations o {where};";
        AddParameters(command, parameters);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private static WhereClause BuildWhere(SearchObservationQuery query)
    {
        var clauses = new List<string>();
        var parameters = new Dictionary<string, object>();
        if (!string.IsNullOrWhiteSpace(query.Text))
        {
            clauses.Add("o.original_query LIKE $text ESCAPE '\\'");
            parameters["$text"] = $"%{EscapeLike(query.Text.Trim())}%";
        }

        if (query.Review == SearchObservationReviewFilter.Unreviewed)
        {
            clauses.Add("o.review_classification IS NULL");
        }
        else if (query.Review == SearchObservationReviewFilter.Reviewed)
        {
            clauses.Add("o.review_classification IS NOT NULL");
        }

        if (query.Result == SearchObservationResultFilter.NoResults)
        {
            clauses.Add("o.status = 'completed'");
            clauses.Add("NOT EXISTS (SELECT 1 FROM search_observation_candidates c0 WHERE c0.observation_id = o.id)");
        }
        else if (query.Result == SearchObservationResultFilter.Selected)
        {
            clauses.Add("EXISTS (SELECT 1 FROM search_observation_candidates c0 WHERE c0.observation_id = o.id AND c0.selected_at IS NOT NULL)");
        }
        else if (query.Result == SearchObservationResultFilter.Failed)
        {
            clauses.Add("o.status = 'failed'");
        }

        return new WhereClause(clauses.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", clauses)}", parameters);
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private static void AddParameters(SqliteCommand command, IReadOnlyDictionary<string, object> parameters)
    {
        foreach (var parameter in parameters)
        {
            Add(command, parameter.Key, parameter.Value);
        }
    }

    private static void Add(SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static string? GetNullableString(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static string FormatDate(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset ParseDate(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    private static string ToText(SearchObservationStatus value) => value switch { SearchObservationStatus.Completed => "completed", SearchObservationStatus.Failed => "failed", _ => throw new InvalidOperationException("Unknown search status.") };
    private static SearchObservationStatus ParseStatus(string value) => value switch { "completed" => SearchObservationStatus.Completed, "failed" => SearchObservationStatus.Failed, _ => throw new InvalidOperationException("Unknown stored search status.") };
    private static string ToText(MediaEntityKind value) => value.ToString().ToLowerInvariant();
    private static MediaEntityKind ParseKind(string value) => Enum.Parse<MediaEntityKind>(value, true);
    private static string ToText(LmsSearchRequestStatus value) => value switch { LmsSearchRequestStatus.Completed => "completed", LmsSearchRequestStatus.Failed => "failed", _ => throw new InvalidOperationException("Unknown LMS request status.") };
    private static LmsSearchRequestStatus ParseRequestStatus(string value) => value switch { "completed" => LmsSearchRequestStatus.Completed, "failed" => LmsSearchRequestStatus.Failed, _ => throw new InvalidOperationException("Unknown stored LMS request status.") };
    private static string ToText(SearchReviewClassification value) => value switch { SearchReviewClassification.Good => "good", SearchReviewClassification.WrongOrder => "wrong_order", SearchReviewClassification.NoMatch => "no_match", SearchReviewClassification.Ambiguous => "ambiguous", SearchReviewClassification.TranscriptionError => "transcription_error", SearchReviewClassification.Other => "other", _ => throw new InvalidOperationException("Unknown review classification.") };
    private static SearchReviewClassification ParseClassification(string value) => value switch { "good" => SearchReviewClassification.Good, "wrong_order" => SearchReviewClassification.WrongOrder, "no_match" => SearchReviewClassification.NoMatch, "ambiguous" => SearchReviewClassification.Ambiguous, "transcription_error" => SearchReviewClassification.TranscriptionError, "other" => SearchReviewClassification.Other, _ => throw new InvalidOperationException("Unknown stored review classification.") };

    private sealed record WhereClause(string Sql, IReadOnlyDictionary<string, object> Parameters);

    private sealed class ExportCaseBuilder(
        string id,
        string query,
        SearchReviewClassification classification,
        MediaEntityKind? expectedKind,
        string? expectedTitle,
        string? expectedArtist,
        string? expectedAlbum,
        string? expectedCorrelationId)
    {
        public string Id { get; } = id;
        public string? ExpectedCorrelationId { get; } = expectedCorrelationId;
        public List<EvaluationCandidate> Candidates { get; } = [];

        public SearchEvaluationCase Build() => new(
            query,
            classification,
            expectedKind,
            expectedTitle,
            expectedArtist,
            expectedAlbum,
            Candidates);
    }

    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS search_observation_schema (version INTEGER NOT NULL);
        INSERT INTO search_observation_schema (version)
            SELECT 1 WHERE NOT EXISTS (SELECT 1 FROM search_observation_schema);
        CREATE TABLE IF NOT EXISTS search_observations (
            id TEXT PRIMARY KEY,
            created_at TEXT NOT NULL,
            original_query TEXT NOT NULL,
            normalised_query TEXT NOT NULL,
            requested_kind TEXT NULL,
            provider TEXT NOT NULL,
            collection TEXT NOT NULL,
            resolver TEXT NOT NULL,
            resolver_version TEXT NOT NULL,
            status TEXT NOT NULL,
            failure_message TEXT NULL,
            total_duration_ms INTEGER NOT NULL,
            retrieval_duration_ms INTEGER NOT NULL,
            processing_duration_ms INTEGER NOT NULL,
            review_classification TEXT NULL,
            expected_correlation_id TEXT NULL,
            expected_kind TEXT NULL,
            expected_title TEXT NULL,
            expected_artist TEXT NULL,
            expected_album TEXT NULL,
            review_notes TEXT NULL,
            include_in_evaluation INTEGER NOT NULL DEFAULT 0,
            reviewed_at TEXT NULL
        );
        CREATE TABLE IF NOT EXISTS search_observation_requests (
            observation_id TEXT NOT NULL,
            sequence INTEGER NOT NULL,
            source TEXT NOT NULL,
            command TEXT NOT NULL,
            duration_ms INTEGER NOT NULL,
            result_count INTEGER NOT NULL,
            PRIMARY KEY (observation_id, sequence),
            FOREIGN KEY (observation_id) REFERENCES search_observations(id) ON DELETE CASCADE
        );
        CREATE TABLE IF NOT EXISTS search_observation_candidates (
            observation_id TEXT NOT NULL,
            position INTEGER NOT NULL,
            correlation_id TEXT NOT NULL UNIQUE,
            kind TEXT NOT NULL,
            media_id TEXT NOT NULL,
            title TEXT NOT NULL,
            artist TEXT NULL,
            album TEXT NULL,
            selected_at TEXT NULL,
            PRIMARY KEY (observation_id, position),
            FOREIGN KEY (observation_id) REFERENCES search_observations(id) ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS ix_search_observations_created_at ON search_observations(created_at DESC);
        CREATE INDEX IF NOT EXISTS ix_search_candidates_selected_at ON search_observation_candidates(selected_at);
        """;

    private const string MigrationVersion2Sql = """
        BEGIN IMMEDIATE;
        ALTER TABLE search_observation_requests
            ADD COLUMN status TEXT NOT NULL DEFAULT 'completed';
        ALTER TABLE search_observation_requests
            ADD COLUMN failure_message TEXT NULL;
        UPDATE search_observation_schema SET version = 2;
        COMMIT;
        """;
}
