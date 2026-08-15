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

public sealed class CatalogueLuceneSearchResolver : IEvaluationSearchResolver, IDisposable
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
        cancellationToken.ThrowIfCancellationRequested();
        var queryForms = PhuzzyTextForms.Create(query);
        if (queryForms.Normalised.Length == 0)
        {
            return Task.FromResult(new EvaluationSearchResponse([], null));
        }

        var spans = CreateSpanForms(queryForms.Tokens);
        var documentIds = new HashSet<int>();
        AddTermLane("normalised", Values(spans, forms => forms.Normalised), documentIds);
        AddTermLane("compact", Values(spans, forms => forms.Compact), documentIds);
        AddTermLane("skeleton", Values(spans, forms => forms.Phonetic), documentIds);
        AddTermLane(
            "acronym",
            Values(spans, forms => forms.Compact)
                .Concat(spans.SelectMany(forms => forms.SpokenAcronymAliases))
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            documentIds);
        AddTermLane("token", queryForms.Tokens, documentIds);
        AddPrefixLane(spans, documentIds);
        AddFuzzyLane(spans, documentIds);
        AddTermLane("trigram", queryForms.Trigrams.ToArray(), documentIds);
        AddTermLane(
            "double_metaphone",
            spans.SelectMany(forms => forms.DoubleMetaphoneCodes)
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            documentIds);

        cancellationToken.ThrowIfCancellationRequested();
        var candidates = documentIds
            .Select(documentId => ReadCandidate(searcher.Doc(documentId)))
            .ToArray();
        return CataloguePhuzzySearchResolver.SearchCandidatesAsync(
            query,
            candidates,
            cancellationToken);
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

    private void AddTermLane(
        string field,
        IReadOnlyCollection<string> terms,
        HashSet<int> documentIds)
    {
        if (terms.Count == 0)
        {
            return;
        }

        var query = new BooleanQuery();
        foreach (var term in terms)
        {
            if (term.Length > 0)
            {
                query.Add(new TermQuery(new Term(field, term)), Occur.SHOULD);
            }
        }

        AddHits(query, documentIds);
    }

    private void AddPrefixLane(
        IReadOnlyList<PhuzzyTextForms> spans,
        HashSet<int> documentIds)
    {
        var query = new BooleanQuery();
        foreach (var value in Values(spans, forms => forms.Normalised))
        {
            query.Add(new PrefixQuery(new Term("normalised", value)), Occur.SHOULD);
        }

        AddHits(query, documentIds);
    }

    private void AddFuzzyLane(
        IReadOnlyList<PhuzzyTextForms> spans,
        HashSet<int> documentIds)
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

        AddHits(query, documentIds);
    }

    private void AddHits(Query query, HashSet<int> documentIds)
    {
        if (query is BooleanQuery booleanQuery && booleanQuery.Clauses.Count == 0)
        {
            return;
        }

        var hits = searcher.Search(query, LaneCandidateLimit);
        foreach (var hit in hits.ScoreDocs)
        {
            documentIds.Add(hit.Doc);
        }
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
}
