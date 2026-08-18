using System.Globalization;
using LyrionVoiceMcp.Abstractions;
using Microsoft.Data.Sqlite;

namespace LyrionVoiceMcp.Persistence;

public sealed class LegacySearchObservationSource(
    SearchObservationSettings settings) : ILegacySearchObservationSource
{
    public async Task<IReadOnlyList<SearchObservation>> ReadBatchAsync(
        DateTimeOffset cutoff,
        LegacySearchObservationCursor? after,
        int limit,
        CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                "Legacy observation batches must contain between 1 and 500 rows.");
        }

        if (!File.Exists(settings.DatabasePath))
        {
            return [];
        }

        await using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = settings.DatabasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Shared,
                Pooling = false
            }.ToString());
        await connection.OpenAsync(cancellationToken);
        var schemaVersion = await ReadSchemaVersionAsync(connection, cancellationToken);
        var observations = await ReadObservationsAsync(
            connection,
            cutoff,
            after,
            limit,
            cancellationToken);
        if (observations.Count == 0)
        {
            return [];
        }

        var requests = await ReadRequestsAsync(
            connection,
            schemaVersion,
            observations.Select(item => item.Id).ToArray(),
            cancellationToken);
        var candidates = await ReadCandidatesAsync(
            connection,
            observations.Select(item => item.Id).ToArray(),
            cancellationToken);
        return observations.Select(item => item with
        {
            Requests = requests.GetValueOrDefault(item.Id) ?? [],
            Candidates = candidates.GetValueOrDefault(item.Id) ?? []
        }).ToArray();
    }

    private static async Task<int> ReadSchemaVersionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT version FROM search_observation_schema LIMIT 1;";
        var version = Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);
        return version is 1 or 2
            ? version
            : throw new InvalidOperationException(
                "The legacy search observation database schema is not supported.");
    }

    private static async Task<List<SearchObservation>> ReadObservationsAsync(
        SqliteConnection connection,
        DateTimeOffset cutoff,
        LegacySearchObservationCursor? after,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT *
            FROM search_observations
            WHERE created_at >= $cutoff
            {(after is null ? string.Empty : "AND (created_at > $afterCreatedAt OR (created_at = $afterCreatedAt AND id > $afterId))")}
            ORDER BY created_at, id
            LIMIT $limit;
            """;
        Add(command, "$cutoff", FormatDate(cutoff));
        Add(command, "$limit", limit);
        if (after is not null)
        {
            Add(command, "$afterCreatedAt", FormatDate(after.CreatedAt));
            Add(command, "$afterId", after.ObservationId);
        }

        var observations = new List<SearchObservation>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            SearchObservationReview? review = null;
            if (!reader.IsDBNull(reader.GetOrdinal("review_classification")))
            {
                review = new SearchObservationReview(
                    ParseClassification(reader.GetString(reader.GetOrdinal("review_classification"))),
                    GetNullableString(reader, "expected_correlation_id"),
                    GetNullableString(reader, "expected_kind") is { } expectedKind
                        ? ParseKind(expectedKind)
                        : null,
                    GetNullableString(reader, "expected_title"),
                    GetNullableString(reader, "expected_artist"),
                    GetNullableString(reader, "expected_album"),
                    GetNullableString(reader, "review_notes"),
                    reader.GetInt32(reader.GetOrdinal("include_in_evaluation")) != 0,
                    ParseDate(reader.GetString(reader.GetOrdinal("reviewed_at"))));
            }

            observations.Add(new SearchObservation(
                reader.GetString(reader.GetOrdinal("id")),
                ParseDate(reader.GetString(reader.GetOrdinal("created_at"))),
                reader.GetString(reader.GetOrdinal("original_query")),
                reader.GetString(reader.GetOrdinal("normalised_query")),
                GetNullableString(reader, "requested_kind") is { } requestedKind
                    ? ParseKind(requestedKind)
                    : null,
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
                review));
        }

        return observations;
    }

    private static async Task<Dictionary<string, IReadOnlyList<LmsSearchRequestObservation>>>
        ReadRequestsAsync(
            SqliteConnection connection,
            int schemaVersion,
            IReadOnlyList<string> observationIds,
            CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        var idList = AddObservationIds(command, observationIds);
        command.CommandText = schemaVersion == 1
            ? $"""
                SELECT observation_id, sequence, source, command, duration_ms, result_count
                FROM search_observation_requests
                WHERE observation_id IN ({idList})
                ORDER BY observation_id, sequence;
                """
            : $"""
                SELECT observation_id, sequence, source, command, duration_ms, result_count,
                       status, failure_message
                FROM search_observation_requests
                WHERE observation_id IN ({idList})
                ORDER BY observation_id, sequence;
                """;

        var requests = new Dictionary<string, List<LmsSearchRequestObservation>>(
            StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var observationId = reader.GetString(0);
            if (!requests.TryGetValue(observationId, out var items))
            {
                items = [];
                requests.Add(observationId, items);
            }

            items.Add(new LmsSearchRequestObservation(
                reader.GetString(2),
                reader.GetString(3),
                schemaVersion == 1
                    ? LmsSearchRequestStatus.Completed
                    : ParseRequestStatus(reader.GetString(6)),
                schemaVersion == 1 || reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetInt64(4),
                reader.GetInt32(5)));
        }

        return requests.ToDictionary(
            item => item.Key,
            item => (IReadOnlyList<LmsSearchRequestObservation>)item.Value,
            StringComparer.Ordinal);
    }

    private static async Task<Dictionary<string, IReadOnlyList<SearchObservationCandidate>>>
        ReadCandidatesAsync(
            SqliteConnection connection,
            IReadOnlyList<string> observationIds,
            CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        var idList = AddObservationIds(command, observationIds);
        command.CommandText = $"""
            SELECT observation_id, position, correlation_id, kind, media_id, title,
                   artist, album, selected_at
            FROM search_observation_candidates
            WHERE observation_id IN ({idList})
            ORDER BY observation_id, position;
            """;

        var candidates = new Dictionary<string, List<SearchObservationCandidate>>(
            StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var observationId = reader.GetString(0);
            if (!candidates.TryGetValue(observationId, out var items))
            {
                items = [];
                candidates.Add(observationId, items);
            }

            items.Add(new SearchObservationCandidate(
                reader.GetInt32(1),
                reader.GetString(2),
                new MediaIdentity(ParseKind(reader.GetString(3)), reader.GetString(4)),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : ParseDate(reader.GetString(8))));
        }

        return candidates.ToDictionary(
            item => item.Key,
            item => (IReadOnlyList<SearchObservationCandidate>)item.Value,
            StringComparer.Ordinal);
    }

    private static string AddObservationIds(
        SqliteCommand command,
        IReadOnlyList<string> observationIds)
    {
        var names = new string[observationIds.Count];
        for (var index = 0; index < observationIds.Count; index++)
        {
            names[index] = $"$id{index}";
            Add(command, names[index], observationIds[index]);
        }

        return string.Join(", ", names);
    }

    private static void Add(SqliteCommand command, string name, object value) =>
        command.Parameters.AddWithValue(name, value);

    private static string? GetNullableString(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseDate(string value) =>
        DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);

    private static SearchObservationStatus ParseStatus(string value) => value switch
    {
        "completed" => SearchObservationStatus.Completed,
        "failed" => SearchObservationStatus.Failed,
        _ => throw new InvalidOperationException("Unknown legacy search status.")
    };

    private static LmsSearchRequestStatus ParseRequestStatus(string value) => value switch
    {
        "completed" => LmsSearchRequestStatus.Completed,
        "failed" => LmsSearchRequestStatus.Failed,
        _ => throw new InvalidOperationException("Unknown legacy request status.")
    };

    private static MediaEntityKind ParseKind(string value) =>
        Enum.Parse<MediaEntityKind>(value, true);

    private static SearchReviewClassification ParseClassification(string value) => value switch
    {
        "good" => SearchReviewClassification.Good,
        "wrong_order" => SearchReviewClassification.WrongOrder,
        "no_match" => SearchReviewClassification.NoMatch,
        "ambiguous" => SearchReviewClassification.Ambiguous,
        "transcription_error" => SearchReviewClassification.TranscriptionError,
        "other" => SearchReviewClassification.Other,
        _ => throw new InvalidOperationException("Unknown legacy review classification.")
    };
}
