using System.Diagnostics;
using System.Globalization;
using LyrionVoiceMcp.Abstractions;
using Microsoft.Data.Sqlite;

namespace LyrionVoiceMcp.Evaluation;

public sealed class CataloguePhuzzyIndexedSearchResolver : IEvaluationDiagnosticSearchResolver
{
    private const int LaneCandidateLimit = 80;
    private readonly string connectionString;

    private CataloguePhuzzyIndexedSearchResolver(
        string databasePath,
        int candidateCount,
        long preparationDurationMilliseconds)
    {
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString();
        Metrics = new EvaluationResolverMetrics(
            candidateCount,
            preparationDurationMilliseconds,
            new FileInfo(databasePath).Length);
    }

    public string Name => "catalogue-phuzzy-indexed";
    public string Version => "2";
    public EvaluationResolverMetrics Metrics { get; }

    public static CataloguePhuzzyIndexedSearchResolver Open(
        string indexDatabasePath,
        SearchIndexArtifact artifact) =>
        new(
            indexDatabasePath,
            artifact.CandidateCount,
            artifact.PreparationDurationMilliseconds);

    public static async Task<CataloguePhuzzyIndexedSearchResolver> CreateAsync(
        string catalogueDatabasePath,
        string indexDatabasePath,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var catalogue = await CatalogueEvaluationIndex.LoadAsync(
            catalogueDatabasePath,
            cancellationToken);
        await BuildIndexAsync(indexDatabasePath, catalogue.Candidates, cancellationToken);
        stopwatch.Stop();
        return new CataloguePhuzzyIndexedSearchResolver(
            indexDatabasePath,
            catalogue.Candidates.Count,
            stopwatch.ElapsedMilliseconds);
    }

    public async Task<EvaluationSearchResponse> SearchAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var execution = await SearchCoreAsync(query, captureDiagnostics: false, cancellationToken);
        var candidates = execution.Ranked
            .Take(20)
            .Select(result => result.Candidate.Source.Value)
            .ToArray();
        return new EvaluationSearchResponse(candidates, null);
    }

    public async Task<EvaluationDiagnosticSearchResponse> SearchDetailedAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var execution = await SearchCoreAsync(query, captureDiagnostics: true, cancellationToken);
        return EvaluationDiagnosticResults.Create(
            this,
            execution.RetrievalDurationMilliseconds,
            execution.RerankDurationMilliseconds,
            execution.TotalDurationMilliseconds,
            execution.Lanes,
            execution.Ranked,
            execution.RetrievalLanes);
    }

    private async Task<ResolverSearchExecution> SearchCoreAsync(
        string query,
        bool captureDiagnostics,
        CancellationToken cancellationToken)
    {
        var totalStopwatch = Stopwatch.StartNew();
        cancellationToken.ThrowIfCancellationRequested();
        var queryForms = PhuzzyTextForms.Create(query);
        if (queryForms.Normalised.Length == 0)
        {
            return new ResolverSearchExecution(
                0,
                0,
                totalStopwatch.Elapsed.TotalMilliseconds,
                [],
                [],
                new Dictionary<string, IReadOnlyList<string>>());
        }

        var retrievalStopwatch = Stopwatch.StartNew();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var candidates = new CandidateCollector<long>(captureDiagnostics);
        var laneMeasurements = new List<EvaluationLaneMeasurement>();
        foreach (var lane in CreateLookupLanes(queryForms))
        {
            await RetrieveLaneAsync(
                captureDiagnostics ? laneMeasurements : null,
                lane.Name,
                candidates,
                () => AddLookupCandidatesAsync(
                    connection,
                    lane.Name,
                    lane.Terms,
                    candidates,
                    captureDiagnostics,
                    cancellationToken));
        }

        await RetrieveLaneAsync(
            captureDiagnostics ? laneMeasurements : null,
            "token_prefix",
            candidates,
            () => AddFtsCandidatesAsync(
                connection,
                queryForms,
                candidates,
                captureDiagnostics,
                cancellationToken));
        await RetrieveLaneAsync(
            captureDiagnostics ? laneMeasurements : null,
            "trigram",
            candidates,
            () => AddTrigramCandidatesAsync(
                connection,
                queryForms,
                candidates,
                captureDiagnostics,
                cancellationToken));
        var read = await ReadCandidatesAsync(
            connection,
            candidates,
            captureDiagnostics,
            cancellationToken);
        retrievalStopwatch.Stop();

        var rerankStopwatch = Stopwatch.StartNew();
        var ranked = CataloguePhuzzySearchResolver.RankCandidates(
            query,
            read.Candidates,
            includeUnmatched: captureDiagnostics,
            captureEvidence: captureDiagnostics,
            cancellationToken);
        rerankStopwatch.Stop();
        totalStopwatch.Stop();
        return new ResolverSearchExecution(
            retrievalStopwatch.Elapsed.TotalMilliseconds,
            rerankStopwatch.Elapsed.TotalMilliseconds,
            totalStopwatch.Elapsed.TotalMilliseconds,
            laneMeasurements,
            ranked,
            read.RetrievalLanes);
    }

    private static async Task BuildIndexAsync(
        string databasePath,
        IReadOnlyList<CatalogueEvaluationCandidate> candidates,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using (var schema = connection.CreateCommand())
        {
            schema.CommandText = """
                PRAGMA journal_mode = OFF;
                PRAGMA synchronous = OFF;
                CREATE TABLE documents (
                    document_id INTEGER PRIMARY KEY,
                    stable_key TEXT NOT NULL UNIQUE,
                    kind INTEGER NOT NULL,
                    title TEXT NOT NULL,
                    artist TEXT NULL,
                    album TEXT NULL
                );
                CREATE TABLE lookup_terms (
                    lane TEXT NOT NULL,
                    term TEXT NOT NULL,
                    document_id INTEGER NOT NULL,
                    PRIMARY KEY (lane, term, document_id)
                ) WITHOUT ROWID;
                CREATE INDEX ix_lookup_terms_document
                    ON lookup_terms (document_id);
                CREATE VIRTUAL TABLE document_fts USING fts5(
                    document_id UNINDEXED,
                    content,
                    tokenize = 'unicode61 remove_diacritics 2',
                    prefix = '2 3 4'
                );
                CREATE VIRTUAL TABLE document_trigram_fts USING fts5(
                    document_id UNINDEXED,
                    content,
                    tokenize = 'trigram'
                );
                """;
            await schema.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var insertDocument = connection.CreateCommand();
        insertDocument.Transaction = (SqliteTransaction)transaction;
        insertDocument.CommandText = """
            INSERT INTO documents (document_id, stable_key, kind, title, artist, album)
            VALUES ($documentId, $stableKey, $kind, $title, $artist, $album);
            """;
        var documentIdParameter = insertDocument.Parameters.Add("$documentId", SqliteType.Integer);
        var stableKeyParameter = insertDocument.Parameters.Add("$stableKey", SqliteType.Text);
        var kindParameter = insertDocument.Parameters.Add("$kind", SqliteType.Integer);
        var titleParameter = insertDocument.Parameters.Add("$title", SqliteType.Text);
        var artistParameter = insertDocument.Parameters.Add("$artist", SqliteType.Text);
        var albumParameter = insertDocument.Parameters.Add("$album", SqliteType.Text);

        await using var insertTerm = connection.CreateCommand();
        insertTerm.Transaction = (SqliteTransaction)transaction;
        insertTerm.CommandText = """
            INSERT OR IGNORE INTO lookup_terms (lane, term, document_id)
            VALUES ($lane, $term, $documentId);
            """;
        var laneParameter = insertTerm.Parameters.Add("$lane", SqliteType.Text);
        var termParameter = insertTerm.Parameters.Add("$term", SqliteType.Text);
        var termDocumentIdParameter = insertTerm.Parameters.Add("$documentId", SqliteType.Integer);

        await using var insertFts = connection.CreateCommand();
        insertFts.Transaction = (SqliteTransaction)transaction;
        insertFts.CommandText = """
            INSERT INTO document_fts (document_id, content)
            VALUES ($documentId, $content);
            """;
        var ftsDocumentIdParameter = insertFts.Parameters.Add("$documentId", SqliteType.Integer);
        var contentParameter = insertFts.Parameters.Add("$content", SqliteType.Text);

        await using var insertTrigramFts = connection.CreateCommand();
        insertTrigramFts.Transaction = (SqliteTransaction)transaction;
        insertTrigramFts.CommandText = """
            INSERT INTO document_trigram_fts (document_id, content)
            VALUES ($documentId, $content);
            """;
        var trigramDocumentIdParameter = insertTrigramFts.Parameters.Add(
            "$documentId",
            SqliteType.Integer);
        var trigramContentParameter = insertTrigramFts.Parameters.Add("$content", SqliteType.Text);

        for (var index = 0; index < candidates.Count; index++)
        {
            if ((index & 255) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var documentId = index + 1L;
            var source = candidates[index];
            var candidate = CataloguePhuzzySearchResolver.CreateCandidate(source);
            documentIdParameter.Value = documentId;
            stableKeyParameter.Value = source.StableKey;
            kindParameter.Value = (int)source.Value.Kind;
            titleParameter.Value = source.Value.Title;
            artistParameter.Value = source.Value.Artist ?? (object)DBNull.Value;
            albumParameter.Value = source.Value.Album ?? (object)DBNull.Value;
            await insertDocument.ExecuteNonQueryAsync(cancellationToken);

            termDocumentIdParameter.Value = documentId;
            foreach (var term in CreateStoredTerms(candidate))
            {
                laneParameter.Value = term.Name;
                termParameter.Value = term.Value;
                await insertTerm.ExecuteNonQueryAsync(cancellationToken);
            }

            ftsDocumentIdParameter.Value = documentId;
            contentParameter.Value = candidate.Combined.Normalised;
            await insertFts.ExecuteNonQueryAsync(cancellationToken);

            trigramDocumentIdParameter.Value = documentId;
            trigramContentParameter.Value = candidate.Combined.Compact;
            await insertTrigramFts.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        await using var optimise = connection.CreateCommand();
        optimise.CommandText = """
            INSERT INTO document_fts(document_fts) VALUES('optimize');
            INSERT INTO document_trigram_fts(document_trigram_fts) VALUES('optimize');
            """;
        await optimise.ExecuteNonQueryAsync(cancellationToken);
    }

    private static IEnumerable<LookupTerm> CreateStoredTerms(PhuzzyCandidate candidate)
    {
        var forms = new[]
        {
            candidate.Title,
            candidate.Artist,
            candidate.Album,
            candidate.Combined
        };
        var terms = new HashSet<LookupTerm>();
        foreach (var form in forms)
        {
            Add(terms, "normalised", form.Normalised);
            Add(terms, "compact", form.Compact);
            Add(terms, "skeleton", form.Phonetic);
            foreach (var alias in form.SpokenAcronymAliases)
            {
                Add(terms, "acronym", alias);
            }

            foreach (var code in form.DoubleMetaphoneCodes)
            {
                Add(terms, "double_metaphone", code);
            }

        }

        return terms;
    }

    private static IReadOnlyList<LookupLane> CreateLookupLanes(PhuzzyTextForms query)
    {
        var spanForms = CreateSpanForms(query.Tokens);
        return
        [
            new LookupLane("normalised", Values(spanForms, form => form.Normalised)),
            new LookupLane("compact", Values(spanForms, form => form.Compact)),
            new LookupLane("skeleton", Values(spanForms, form => form.Phonetic)),
            new LookupLane(
                "double_metaphone",
                spanForms.SelectMany(form => form.DoubleMetaphoneCodes)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray()),
            new LookupLane(
                "acronym",
                Values(spanForms, form => form.Compact)
                    .Concat(spanForms.SelectMany(form => form.SpokenAcronymAliases))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray())
        ];
    }

    private static IReadOnlyList<PhuzzyTextForms> CreateSpanForms(IReadOnlyList<string> tokens)
    {
        var forms = new List<PhuzzyTextForms>();
        for (var start = 0; start < tokens.Count; start++)
        {
            for (var length = 1; length <= tokens.Count - start; length++)
            {
                forms.Add(PhuzzyTextForms.Create(
                    string.Join(' ', tokens.Skip(start).Take(length))));
            }
        }

        return forms;
    }

    private static IReadOnlyList<string> Values(
        IEnumerable<PhuzzyTextForms> forms,
        Func<PhuzzyTextForms, string> select) =>
        forms.Select(select)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static void Add(HashSet<LookupTerm> terms, string name, string value)
    {
        if (value.Length > 0)
        {
            terms.Add(new LookupTerm(name, value));
        }
    }

    private static async Task<LaneRetrieval> AddLookupCandidatesAsync(
        SqliteConnection connection,
        string lane,
        IReadOnlyList<string> terms,
        CandidateCollector<long> candidates,
        bool countMatches,
        CancellationToken cancellationToken)
    {
        if (terms.Count == 0)
        {
            return new LaneRetrieval(0, 0);
        }

        var matchedCandidateCount = 0;
        if (countMatches)
        {
            await using var count = connection.CreateCommand();
            var countParameters = AddParameters(count, "$countTerm", terms);
            count.CommandText = $"""
                SELECT COUNT(*)
                FROM (
                    SELECT document_id
                    FROM lookup_terms
                    WHERE lane = $lane
                      AND term IN ({string.Join(", ", countParameters)})
                    GROUP BY document_id
                );
                """;
            count.Parameters.AddWithValue("$lane", lane);
            matchedCandidateCount = Convert.ToInt32(
                await count.ExecuteScalarAsync(cancellationToken),
                CultureInfo.InvariantCulture);
        }

        await using var command = connection.CreateCommand();
        var termParameters = AddParameters(command, "$term", terms);
        command.CommandText = $"""
            SELECT document_id
            FROM lookup_terms
            WHERE lane = $lane
              AND term IN ({string.Join(", ", termParameters)})
            GROUP BY document_id
            ORDER BY COUNT(*) DESC, document_id
            LIMIT {LaneCandidateLimit};
            """;
        command.Parameters.AddWithValue("$lane", lane);
        var retrievedCandidateCount = await AddIdsAsync(
            command,
            lane,
            candidates,
            cancellationToken);
        return new LaneRetrieval(
            countMatches ? matchedCandidateCount : retrievedCandidateCount,
            retrievedCandidateCount);
    }

    private static async Task<LaneRetrieval> AddFtsCandidatesAsync(
        SqliteConnection connection,
        PhuzzyTextForms query,
        CandidateCollector<long> candidates,
        bool countMatches,
        CancellationToken cancellationToken)
    {
        var tokens = query.Tokens
            .Where(token => token.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Select(token => $"\"{token}\"*")
            .ToArray();
        if (tokens.Length == 0)
        {
            return new LaneRetrieval(0, 0);
        }

        var expression = string.Join(" OR ", tokens);
        var matchedCandidateCount = countMatches
            ? await CountFtsMatchesAsync(
                connection,
                "document_fts",
                expression,
                cancellationToken)
            : 0;
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT document_id
            FROM document_fts
            WHERE document_fts MATCH $query
            ORDER BY bm25(document_fts), document_id
            LIMIT {LaneCandidateLimit};
            """;
        command.Parameters.AddWithValue("$query", expression);
        var retrievedCandidateCount = await AddIdsAsync(
            command,
            "token_prefix",
            candidates,
            cancellationToken);
        return new LaneRetrieval(
            countMatches ? matchedCandidateCount : retrievedCandidateCount,
            retrievedCandidateCount);
    }

    private static async Task<LaneRetrieval> AddTrigramCandidatesAsync(
        SqliteConnection connection,
        PhuzzyTextForms query,
        CandidateCollector<long> candidates,
        bool countMatches,
        CancellationToken cancellationToken)
    {
        var terms = query.Trigrams
            .Select(term => $"\"{term}\"")
            .ToArray();
        if (terms.Length == 0)
        {
            return new LaneRetrieval(0, 0);
        }

        var expression = string.Join(" OR ", terms);
        var matchedCandidateCount = countMatches
            ? await CountFtsMatchesAsync(
                connection,
                "document_trigram_fts",
                expression,
                cancellationToken)
            : 0;
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT document_id
            FROM document_trigram_fts
            WHERE document_trigram_fts MATCH $query
            ORDER BY bm25(document_trigram_fts), document_id
            LIMIT {LaneCandidateLimit};
            """;
        command.Parameters.AddWithValue("$query", expression);
        var retrievedCandidateCount = await AddIdsAsync(
            command,
            "trigram",
            candidates,
            cancellationToken);
        return new LaneRetrieval(
            countMatches ? matchedCandidateCount : retrievedCandidateCount,
            retrievedCandidateCount);
    }

    private static async Task<int> CountFtsMatchesAsync(
        SqliteConnection connection,
        string table,
        string expression,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table} WHERE {table} MATCH $query;";
        command.Parameters.AddWithValue("$query", expression);
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);
    }

    private static IReadOnlyList<string> AddParameters(
        SqliteCommand command,
        string prefix,
        IReadOnlyList<string> values)
    {
        var names = new string[values.Count];
        for (var index = 0; index < values.Count; index++)
        {
            names[index] = $"{prefix}{index}";
            command.Parameters.AddWithValue(names[index], values[index]);
        }

        return names;
    }

    private static async Task<int> AddIdsAsync(
        SqliteCommand command,
        string lane,
        CandidateCollector<long> candidates,
        CancellationToken cancellationToken)
    {
        var hitCount = 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            hitCount++;
            var documentId = reader.GetInt64(0);
            candidates.Add(documentId, lane);
        }

        return hitCount;
    }

    private static async Task<CandidateReadResult> ReadCandidatesAsync(
        SqliteConnection connection,
        CandidateCollector<long> collectedCandidates,
        bool captureEvidence,
        CancellationToken cancellationToken)
    {
        var documentIds = collectedCandidates.CandidateIds.ToArray();
        if (documentIds.Length == 0)
        {
            return new CandidateReadResult([], new Dictionary<string, IReadOnlyList<string>>());
        }

        await using var command = connection.CreateCommand();
        var parameters = AddParameters(
            command,
            "$documentId",
            documentIds.Select(value => value.ToString(CultureInfo.InvariantCulture)).ToArray());
        for (var index = 0; index < documentIds.Length; index++)
        {
            command.Parameters[parameters[index]].Value = documentIds[index];
        }

        command.CommandText = $"""
            SELECT document_id, stable_key, kind, title, artist, album
            FROM documents
            WHERE document_id IN ({string.Join(", ", parameters)});
            """;
        var candidates = new List<PhuzzyCandidate>(documentIds.Length);
        var retrievalLanes = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var documentId = reader.GetInt64(0);
            var stableKey = reader.GetString(1);
            var source = new CatalogueEvaluationCandidate(
                stableKey,
                new EvaluationSearchCandidate(
                    (MediaEntityKind)reader.GetInt32(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5)),
                PhuzzyText.Normalise(reader.GetString(3)),
                reader.IsDBNull(4) ? string.Empty : PhuzzyText.Normalise(reader.GetString(4)),
                reader.IsDBNull(5) ? string.Empty : PhuzzyText.Normalise(reader.GetString(5)),
                string.Empty);
            candidates.Add(CataloguePhuzzySearchResolver.CreateCandidate(source));
            if (captureEvidence)
            {
                retrievalLanes.Add(stableKey, collectedCandidates.GetEvidence(documentId));
            }
        }

        return new CandidateReadResult(candidates, retrievalLanes);
    }

    private static async Task RetrieveLaneAsync(
        List<EvaluationLaneMeasurement>? measurements,
        string name,
        CandidateCollector<long> candidates,
        Func<Task<LaneRetrieval>> retrieve)
    {
        if (measurements is null)
        {
            await retrieve();
            return;
        }

        var candidatesBefore = candidates.Count;
        var stopwatch = Stopwatch.StartNew();
        var retrieval = await retrieve();
        stopwatch.Stop();
        measurements.Add(new EvaluationLaneMeasurement(
            name,
            stopwatch.Elapsed.TotalMilliseconds,
            retrieval.MatchedCandidateCount,
            retrieval.RetrievedCandidateCount,
            candidates.Count - candidatesBefore));
    }

    private sealed record LookupTerm(string Name, string Value);
    private sealed record LookupLane(string Name, IReadOnlyList<string> Terms);
    private sealed record CandidateReadResult(
        IReadOnlyList<PhuzzyCandidate> Candidates,
        IReadOnlyDictionary<string, IReadOnlyList<string>> RetrievalLanes);
}
