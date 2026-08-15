using System.Diagnostics;
using System.Globalization;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Evaluation;

public sealed class CatalogueLuceneSearchResolver : IEvaluationDiagnosticSearchResolver, IDisposable
{
    private const int LaneCandidateLimit = 80;
    private const LuceneVersion LuceneApiVersion = LuceneVersion.LUCENE_48;
    private readonly FSDirectory directory;
    private readonly DirectoryReader reader;
    private readonly IndexSearcher searcher;

    private CatalogueLuceneSearchResolver(
        FSDirectory directory,
        DirectoryReader reader,
        int candidateCount,
        long preparationDurationMilliseconds,
        long indexSizeBytes)
    {
        this.directory = directory;
        this.reader = reader;
        searcher = new IndexSearcher(reader);
        Metrics = new EvaluationResolverMetrics(
            candidateCount,
            preparationDurationMilliseconds,
            indexSizeBytes);
    }

    public string Name => "catalogue-lucene";
    public string Version => "1";
    public EvaluationResolverMetrics Metrics { get; }

    public static async Task<CatalogueLuceneSearchResolver> CreateAsync(
        string catalogueDatabasePath,
        string indexDirectoryPath,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var catalogue = await CatalogueEvaluationIndex.LoadAsync(
            catalogueDatabasePath,
            cancellationToken);
        if (System.IO.Directory.Exists(indexDirectoryPath))
        {
            System.IO.Directory.Delete(indexDirectoryPath, recursive: true);
        }

        System.IO.Directory.CreateDirectory(indexDirectoryPath);
        var directory = FSDirectory.Open(new DirectoryInfo(indexDirectoryPath));
        DirectoryReader? reader = null;
        try
        {
            BuildIndex(directory, catalogue.Candidates, cancellationToken);
            reader = DirectoryReader.Open(directory);
            stopwatch.Stop();
            var size = System.IO.Directory
                .EnumerateFiles(indexDirectoryPath, "*", SearchOption.AllDirectories)
                .Sum(path => new FileInfo(path).Length);
            return new CatalogueLuceneSearchResolver(
                directory,
                reader,
                catalogue.Candidates.Count,
                stopwatch.ElapsedMilliseconds,
                size);
        }
        catch
        {
            reader?.Dispose();
            directory.Dispose();
            throw;
        }
    }

    public Task<EvaluationSearchResponse> SearchAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var execution = SearchCore(query, captureDiagnostics: false, cancellationToken);
        var candidates = execution.Ranked
            .Take(20)
            .Select(result => result.Candidate.Source.Value)
            .ToArray();
        return Task.FromResult(new EvaluationSearchResponse(candidates, null));
    }

    public Task<EvaluationDiagnosticSearchResponse> SearchDetailedAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var execution = SearchCore(query, captureDiagnostics: true, cancellationToken);
        return Task.FromResult(EvaluationDiagnosticResults.Create(
            this,
            execution.RetrievalDurationMilliseconds,
            execution.RerankDurationMilliseconds,
            execution.TotalDurationMilliseconds,
            execution.Lanes,
            execution.Ranked,
            execution.RetrievalLanes));
    }

    private ResolverSearchExecution SearchCore(
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
        var spans = CreateSpanForms(queryForms.Tokens);
        var candidates = new CandidateCollector<int>(captureDiagnostics);
        var laneMeasurements = new List<EvaluationLaneMeasurement>();
        RetrieveLane(
            captureDiagnostics ? laneMeasurements : null,
            "normalised",
            candidates,
            () => AddTermLane(
                "normalised",
                Values(spans, forms => forms.Normalised),
                candidates));
        RetrieveLane(
            captureDiagnostics ? laneMeasurements : null,
            "compact",
            candidates,
            () => AddTermLane(
                "compact",
                Values(spans, forms => forms.Compact),
                candidates));
        RetrieveLane(
            captureDiagnostics ? laneMeasurements : null,
            "skeleton",
            candidates,
            () => AddTermLane(
                "skeleton",
                Values(spans, forms => forms.Phonetic),
                candidates));
        RetrieveLane(
            captureDiagnostics ? laneMeasurements : null,
            "acronym",
            candidates,
            () => AddTermLane(
                "acronym",
                Values(spans, forms => forms.Compact)
                    .Concat(spans.SelectMany(forms => forms.SpokenAcronymAliases))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                candidates));
        RetrieveLane(
            captureDiagnostics ? laneMeasurements : null,
            "token",
            candidates,
            () => AddTermLane("token", queryForms.Tokens, candidates));
        RetrieveLane(
            captureDiagnostics ? laneMeasurements : null,
            "prefix",
            candidates,
            () => AddPrefixLane(spans, candidates));
        RetrieveLane(
            captureDiagnostics ? laneMeasurements : null,
            "fuzzy",
            candidates,
            () => AddFuzzyLane(spans, candidates));
        RetrieveLane(
            captureDiagnostics ? laneMeasurements : null,
            "trigram",
            candidates,
            () => AddTermLane("trigram", queryForms.Trigrams.ToArray(), candidates));
        RetrieveLane(
            captureDiagnostics ? laneMeasurements : null,
            "double_metaphone",
            candidates,
            () => AddTermLane(
                "double_metaphone",
                spans.SelectMany(forms => forms.DoubleMetaphoneCodes)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                candidates));

        cancellationToken.ThrowIfCancellationRequested();
        var candidateDocuments = candidates.CandidateIds
            .Select(documentId => new CandidateDocument(documentId, searcher.Doc(documentId)))
            .ToArray();
        var candidateValues = candidateDocuments
            .Select(item => ReadCandidate(item.Document))
            .ToArray();
        var retrievalLanes = captureDiagnostics
            ? candidateDocuments.ToDictionary(
                item => item.Document.Get("stable_key"),
                item => candidates.GetEvidence(item.DocumentId),
                StringComparer.Ordinal)
            : new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        retrievalStopwatch.Stop();

        var rerankStopwatch = Stopwatch.StartNew();
        var ranked = CataloguePhuzzySearchResolver.RankCandidates(
            query,
            candidateValues,
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
            retrievalLanes);
    }

    public void Dispose()
    {
        reader.Dispose();
        directory.Dispose();
    }

    private static void BuildIndex(
        Lucene.Net.Store.Directory directory,
        IReadOnlyList<CatalogueEvaluationCandidate> candidates,
        CancellationToken cancellationToken)
    {
        using var analyser = new KeywordAnalyzer();
        var configuration = new IndexWriterConfig(LuceneApiVersion, analyser)
        {
            OpenMode = OpenMode.CREATE,
            RAMBufferSizeMB = 32
        };
        using var writer = new IndexWriter(directory, configuration);
        for (var index = 0; index < candidates.Count; index++)
        {
            if ((index & 255) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var source = candidates[index];
            var candidate = CataloguePhuzzySearchResolver.CreateCandidate(source);
            var document = new Document
            {
                new StringField("stable_key", source.StableKey, Field.Store.YES),
                new Int32Field("kind", (int)source.Value.Kind, Field.Store.YES),
                new StoredField("title", source.Value.Title)
            };
            if (source.Value.Artist is { } artist)
            {
                document.Add(new StoredField("artist", artist));
            }

            if (source.Value.Album is { } album)
            {
                document.Add(new StoredField("album", album));
            }

            var forms = new[]
            {
                candidate.Title,
                candidate.Artist,
                candidate.Album,
                candidate.Combined
            };
            foreach (var form in forms)
            {
                AddTerm(document, "normalised", form.Normalised);
                AddTerm(document, "compact", form.Compact);
                AddTerm(document, "skeleton", form.Phonetic);
                foreach (var token in form.Tokens)
                {
                    AddTerm(document, "token", token);
                }

                foreach (var alias in form.SpokenAcronymAliases)
                {
                    AddTerm(document, "acronym", alias);
                }

                foreach (var code in form.DoubleMetaphoneCodes)
                {
                    AddTerm(document, "double_metaphone", code);
                }
            }

            foreach (var trigram in candidate.Combined.Trigrams)
            {
                AddTerm(document, "trigram", trigram);
            }

            writer.AddDocument(document);
        }

        writer.Commit();
        writer.ForceMerge(1);
    }

    private LaneRetrieval AddTermLane(
        string field,
        IReadOnlyCollection<string> terms,
        CandidateCollector<int> candidates)
    {
        if (terms.Count == 0)
        {
            return new LaneRetrieval(0, 0);
        }

        var query = new BooleanQuery();
        foreach (var term in terms)
        {
            if (term.Length > 0)
            {
                query.Add(new TermQuery(new Term(field, term)), Occur.SHOULD);
            }
        }

        return AddHits(field, query, candidates);
    }

    private LaneRetrieval AddPrefixLane(
        IReadOnlyList<PhuzzyTextForms> spans,
        CandidateCollector<int> candidates)
    {
        var query = new BooleanQuery();
        foreach (var value in Values(spans, forms => forms.Normalised))
        {
            query.Add(new PrefixQuery(new Term("normalised", value)), Occur.SHOULD);
        }

        return AddHits("prefix", query, candidates);
    }

    private LaneRetrieval AddFuzzyLane(
        IReadOnlyList<PhuzzyTextForms> spans,
        CandidateCollector<int> candidates)
    {
        var query = new BooleanQuery();
        foreach (var span in spans)
        {
            var value = span.Compact;
            if (value.Length < 5 || span.Tokens.Count > 3)
            {
                continue;
            }

            var edits = value.Length <= 5 ? 1 : 2;
            query.Add(
                new FuzzyQuery(
                    new Term("compact", value),
                    edits,
                    prefixLength: 1,
                    maxExpansions: 20,
                    transpositions: true),
                Occur.SHOULD);
        }

        return AddHits("fuzzy", query, candidates);
    }

    private LaneRetrieval AddHits(
        string lane,
        Query query,
        CandidateCollector<int> candidates)
    {
        if (query is BooleanQuery booleanQuery && booleanQuery.Clauses.Count == 0)
        {
            return new LaneRetrieval(0, 0);
        }

        var hits = searcher.Search(query, LaneCandidateLimit);
        foreach (var hit in hits.ScoreDocs)
        {
            candidates.Add(hit.Doc, lane);
        }

        return new LaneRetrieval(hits.TotalHits, hits.ScoreDocs.Length);
    }

    private static void RetrieveLane(
        List<EvaluationLaneMeasurement>? measurements,
        string name,
        CandidateCollector<int> candidates,
        Func<LaneRetrieval> retrieve)
    {
        if (measurements is null)
        {
            retrieve();
            return;
        }

        var candidatesBefore = candidates.Count;
        var stopwatch = Stopwatch.StartNew();
        var retrieval = retrieve();
        stopwatch.Stop();
        measurements.Add(new EvaluationLaneMeasurement(
            name,
            stopwatch.Elapsed.TotalMilliseconds,
            retrieval.MatchedCandidateCount,
            retrieval.RetrievedCandidateCount,
            candidates.Count - candidatesBefore));
    }

    private static void AddTerm(Document document, string field, string value)
    {
        if (value.Length > 0)
        {
            document.Add(new StringField(field, value, Field.Store.NO));
        }
    }

    private static IReadOnlyList<PhuzzyTextForms> CreateSpanForms(
        IReadOnlyList<string> tokens)
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

    private static PhuzzyCandidate ReadCandidate(Document document)
    {
        var title = document.Get("title");
        var artist = document.Get("artist");
        var album = document.Get("album");
        var source = new CatalogueEvaluationCandidate(
            document.Get("stable_key"),
            new EvaluationSearchCandidate(
                (MediaEntityKind)document.GetField("kind").GetInt32Value()!.Value,
                title,
                artist,
                album),
            PhuzzyText.Normalise(title),
            PhuzzyText.Normalise(artist),
            PhuzzyText.Normalise(album),
            string.Empty);
        return CataloguePhuzzySearchResolver.CreateCandidate(source);
    }

    private sealed record CandidateDocument(int DocumentId, Document Document);
}
