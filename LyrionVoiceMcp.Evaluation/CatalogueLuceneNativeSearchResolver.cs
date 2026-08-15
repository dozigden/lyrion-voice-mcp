using System.Diagnostics;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Evaluation;

public sealed class CatalogueLuceneNativeSearchResolver
    : IEvaluationDiagnosticSearchResolver, IDisposable
{
    private const int DiagnosticResultLimit = 300;
    private const int NormalResultLimit = 20;
    private const int LuceneScoreScale = 1_000;
    private const LuceneVersion LuceneApiVersion = LuceneVersion.LUCENE_48;
    private static readonly SearchField[] SearchFields =
    [
        new("title", 4f),
        new("artist", 2.75f),
        new("album", 2f)
    ];
    private readonly FSDirectory directory;
    private readonly DirectoryReader reader;
    private readonly IndexSearcher searcher;

    private CatalogueLuceneNativeSearchResolver(
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

    public string Name => "catalogue-lucene-native";
    public string Version => "1";
    public EvaluationResolverMetrics Metrics { get; }

    public static async Task<CatalogueLuceneNativeSearchResolver> CreateAsync(
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
            return new CatalogueLuceneNativeSearchResolver(
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
        var execution = SearchCore(query, NormalResultLimit, cancellationToken);
        return Task.FromResult(new EvaluationSearchResponse(
            execution.Results.Select(item => item.Candidate).ToArray(),
            null));
    }

    public Task<EvaluationDiagnosticSearchResponse> SearchDetailedAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var execution = SearchCore(query, DiagnosticResultLimit, cancellationToken);
        var results = execution.Results.Select((item, index) =>
            new EvaluationDiagnosticCandidate(
                index + 1,
                item.Candidate.Kind,
                item.Candidate.Title,
                item.Candidate.Artist,
                item.Candidate.Album,
                item.Score,
                ["native_query"],
                new EvaluationScoreEvidence(
                    "multi_field",
                    "native_lucene_score",
                    execution.NormalisedQuery,
                    0,
                    0,
                    item.Score,
                    0,
                    0,
                    item.Score)))
            .ToArray();
        return Task.FromResult(new EvaluationDiagnosticSearchResponse(
            Name,
            Version,
            Metrics,
            execution.RetrievalDurationMilliseconds,
            0,
            execution.TotalDurationMilliseconds,
            results.Length,
            [
                new EvaluationLaneMeasurement(
                    "native_query",
                    execution.RetrievalDurationMilliseconds,
                    execution.TotalHits,
                    results.Length,
                    results.Length)
            ],
            results));
    }

    public void Dispose()
    {
        reader.Dispose();
        directory.Dispose();
    }

    private NativeSearchExecution SearchCore(
        string query,
        int resultLimit,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var totalStopwatch = Stopwatch.StartNew();
        var queryForms = PhuzzyTextForms.Create(query);
        if (queryForms.Normalised.Length == 0)
        {
            totalStopwatch.Stop();
            return new NativeSearchExecution(
                queryForms.Normalised,
                0,
                totalStopwatch.Elapsed.TotalMilliseconds,
                0,
                []);
        }

        var nativeQuery = BuildQuery(queryForms);
        var retrievalStopwatch = Stopwatch.StartNew();
        var hits = searcher.Search(nativeQuery, resultLimit);
        var results = hits.ScoreDocs.Select(hit =>
        {
            var document = searcher.Doc(hit.Doc);
            return new NativeSearchResult(
                ReadCandidate(document),
                Math.Max(1, (int)Math.Round(
                    hit.Score * LuceneScoreScale,
                    MidpointRounding.AwayFromZero)));
        }).ToArray();
        retrievalStopwatch.Stop();
        totalStopwatch.Stop();
        cancellationToken.ThrowIfCancellationRequested();
        return new NativeSearchExecution(
            queryForms.Normalised,
            retrievalStopwatch.Elapsed.TotalMilliseconds,
            totalStopwatch.Elapsed.TotalMilliseconds,
            hits.TotalHits,
            results);
    }

    private static Query BuildQuery(PhuzzyTextForms queryForms)
    {
        var spans = CreateQuerySpans(queryForms.Tokens);
        var root = new DisjunctionMaxQuery(tieBreakerMultiplier: 0.1f);
        foreach (var field in SearchFields)
        {
            AddGroup(root, ExactGroup(field, spans), field.Boost * 12f);
            AddGroup(root, CompactGroup(field, spans), field.Boost * 10f);
            AddGroup(root, AcronymGroup(field, spans), field.Boost * 9f);
            AddGroup(root, PhraseGroup(field, spans), field.Boost * 8f);
            AddGroup(root, SkeletonGroup(field, spans), field.Boost * 6f);
            AddGroup(root, DoubleMetaphoneGroup(field, spans), field.Boost * 5f);
            AddGroup(root, FuzzyCompactGroup(field, spans), field.Boost * 4f);
            AddGroup(root, TokenGroup(field, queryForms.Tokens), field.Boost * 3f);
            AddGroup(root, FuzzyTokenGroup(field, queryForms.Tokens), field.Boost * 2.5f);
            AddGroup(root, PrefixTokenGroup(field, queryForms.Tokens), field.Boost * 1.5f);
        }

        return root;
    }

    private static void BuildIndex(
        Lucene.Net.Store.Directory directory,
        IReadOnlyList<CatalogueEvaluationCandidate> candidates,
        CancellationToken cancellationToken)
    {
        using var analyser = new WhitespaceAnalyzer(LuceneApiVersion);
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

            AddSearchFields(document, "title", source.Value.Title);
            AddSearchFields(document, "artist", source.Value.Artist);
            AddSearchFields(document, "album", source.Value.Album);
            writer.AddDocument(document);
        }

        writer.Commit();
        writer.ForceMerge(1);
    }

    private static void AddSearchFields(Document document, string field, string? value)
    {
        var forms = PhuzzyTextForms.Create(value);
        if (forms.Normalised.Length == 0)
        {
            return;
        }

        document.Add(new StringField($"{field}_exact", forms.Normalised, Field.Store.NO));
        document.Add(new StringField($"{field}_compact", forms.Compact, Field.Store.NO));
        document.Add(new TextField($"{field}_text", forms.Normalised, Field.Store.NO));
        AddTerm(document, $"{field}_skeleton", forms.Phonetic);
        foreach (var code in forms.DoubleMetaphoneCodes)
        {
            AddTerm(document, $"{field}_double_metaphone", code);
        }

        foreach (var alias in forms.SpokenAcronymAliases)
        {
            AddTerm(document, $"{field}_acronym", alias);
        }
    }

    private static Query? ExactGroup(SearchField field, IReadOnlyList<QuerySpan> spans) =>
        SpanTermGroup(
            $"{field.Name}_exact",
            spans,
            item => [item.Forms.Normalised]);

    private static Query? CompactGroup(SearchField field, IReadOnlyList<QuerySpan> spans) =>
        SpanTermGroup(
            $"{field.Name}_compact",
            spans,
            item => [item.Forms.Compact]);

    private static Query? AcronymGroup(SearchField field, IReadOnlyList<QuerySpan> spans) =>
        SpanTermGroup(
            $"{field.Name}_acronym",
            spans,
            item => item.Forms.SpokenAcronymAliases.Append(item.Forms.Compact));

    private static Query? SkeletonGroup(SearchField field, IReadOnlyList<QuerySpan> spans) =>
        SpanTermGroup(
            $"{field.Name}_skeleton",
            spans.Where(item => item.Forms.Phonetic.Length >= 3),
            item => [item.Forms.Phonetic]);

    private static Query? DoubleMetaphoneGroup(
        SearchField field,
        IReadOnlyList<QuerySpan> spans) =>
        SpanTermGroup(
            $"{field.Name}_double_metaphone",
            spans,
            item => item.Forms.DoubleMetaphoneCodes);

    private static Query? PhraseGroup(SearchField field, IReadOnlyList<QuerySpan> spans)
    {
        var group = new BooleanQuery();
        foreach (var span in spans.Where(item => item.TokenCount >= 2))
        {
            var phrase = new PhraseQuery { Slop = 1 };
            foreach (var token in span.Forms.Tokens)
            {
                phrase.Add(new Term($"{field.Name}_text", token));
            }

            phrase.Boost = CoverageBoost(span);
            group.Add(phrase, Occur.SHOULD);
        }

        return group.Clauses.Count == 0 ? null : group;
    }

    private static Query? FuzzyCompactGroup(
        SearchField field,
        IReadOnlyList<QuerySpan> spans)
    {
        var group = new BooleanQuery();
        foreach (var span in spans.Where(item =>
                     item.Forms.Compact.Length >= 5 && item.TokenCount <= 3))
        {
            var value = span.Forms.Compact;
            var fuzzy = new FuzzyQuery(
                new Term($"{field.Name}_compact", value),
                value.Length <= 5 ? 1 : 2,
                prefixLength: 1,
                maxExpansions: 30,
                transpositions: true)
            {
                Boost = CoverageBoost(span)
            };
            group.Add(fuzzy, Occur.SHOULD);
        }

        return group.Clauses.Count == 0 ? null : group;
    }

    private static Query? TokenGroup(SearchField field, IReadOnlyList<string> tokens)
    {
        var values = tokens.Distinct(StringComparer.Ordinal).ToArray();
        var group = new BooleanQuery
        {
            MinimumNumberShouldMatch = Math.Min(2, values.Length)
        };
        foreach (var token in values)
        {
            group.Add(
                new TermQuery(new Term($"{field.Name}_text", token)),
                Occur.SHOULD);
        }

        return group.Clauses.Count == 0 ? null : group;
    }

    private static Query? FuzzyTokenGroup(SearchField field, IReadOnlyList<string> tokens)
    {
        var values = tokens
            .Where(token => token.Length >= 4)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var group = new BooleanQuery
        {
            MinimumNumberShouldMatch = Math.Min(2, values.Length)
        };
        foreach (var token in values)
        {
            group.Add(
                new FuzzyQuery(
                    new Term($"{field.Name}_text", token),
                    token.Length <= 4 ? 1 : 2,
                    prefixLength: 1,
                    maxExpansions: 30,
                    transpositions: true),
                Occur.SHOULD);
        }

        return group.Clauses.Count == 0 ? null : group;
    }

    private static Query? PrefixTokenGroup(SearchField field, IReadOnlyList<string> tokens)
    {
        var values = tokens.Where(token => token.Length >= 3)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var group = new BooleanQuery
        {
            MinimumNumberShouldMatch = Math.Min(2, values.Length)
        };
        foreach (var token in values)
        {
            group.Add(
                new PrefixQuery(new Term($"{field.Name}_text", token)),
                Occur.SHOULD);
        }

        return group.Clauses.Count == 0 ? null : group;
    }

    private static Query? SpanTermGroup(
        string field,
        IEnumerable<QuerySpan> spans,
        Func<QuerySpan, IEnumerable<string>> selectTerms)
    {
        var terms = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var span in spans)
        {
            var boost = CoverageBoost(span);
            foreach (var term in selectTerms(span).Where(value => value.Length > 0))
            {
                if (!terms.TryGetValue(term, out var existing) || boost > existing)
                {
                    terms[term] = boost;
                }
            }
        }

        var group = new BooleanQuery();
        foreach (var (term, boost) in terms)
        {
            group.Add(
                new TermQuery(new Term(field, term)) { Boost = boost },
                Occur.SHOULD);
        }

        return group.Clauses.Count == 0 ? null : group;
    }

    private static void AddGroup(DisjunctionMaxQuery root, Query? group, float boost)
    {
        if (group is null)
        {
            return;
        }

        group.Boost = boost;
        root.Add(group);
    }

    private static IReadOnlyList<QuerySpan> CreateQuerySpans(IReadOnlyList<string> tokens)
    {
        var spans = new List<QuerySpan>();
        var maximumSpanLength = Math.Min(4, tokens.Count);
        for (var start = 0; start < tokens.Count; start++)
        {
            for (var length = 1;
                 length <= maximumSpanLength && start + length <= tokens.Count;
                 length++)
            {
                spans.Add(CreateSpan(tokens, start, length));
            }
        }

        if (tokens.Count > maximumSpanLength)
        {
            spans.Add(CreateSpan(tokens, 0, tokens.Count));
        }

        return spans;
    }

    private static QuerySpan CreateSpan(
        IReadOnlyList<string> tokens,
        int start,
        int length) =>
        new(
            PhuzzyTextForms.Create(string.Join(' ', tokens.Skip(start).Take(length))),
            length,
            tokens.Count);

    private static float CoverageBoost(QuerySpan span)
    {
        var proportion = (float)span.TokenCount / span.QueryTokenCount;
        return proportion * proportion;
    }

    private static void AddTerm(Document document, string field, string value)
    {
        if (value.Length > 0)
        {
            document.Add(new StringField(field, value, Field.Store.NO));
        }
    }

    private static EvaluationSearchCandidate ReadCandidate(Document document) =>
        new(
            (MediaEntityKind)document.GetField("kind").GetInt32Value()!.Value,
            document.Get("title"),
            document.Get("artist"),
            document.Get("album"));

    private sealed record SearchField(string Name, float Boost);

    private sealed record QuerySpan(
        PhuzzyTextForms Forms,
        int TokenCount,
        int QueryTokenCount);

    private sealed record NativeSearchExecution(
        string NormalisedQuery,
        double RetrievalDurationMilliseconds,
        double TotalDurationMilliseconds,
        int TotalHits,
        IReadOnlyList<NativeSearchResult> Results);

    private sealed record NativeSearchResult(
        EvaluationSearchCandidate Candidate,
        int Score);
}
