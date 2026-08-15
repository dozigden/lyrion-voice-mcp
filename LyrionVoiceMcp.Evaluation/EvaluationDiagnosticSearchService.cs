using System.Diagnostics;

namespace LyrionVoiceMcp.Evaluation;

public sealed record EvaluationDiagnosticSettings(
    string CataloguePath,
    string IndexDirectoryPath)
{
    public static EvaluationDiagnosticSettings FromValues(
        string contentRootPath,
        string cataloguePath,
        string? indexDirectoryPath)
    {
        var configuredPath = string.IsNullOrWhiteSpace(indexDirectoryPath)
            ? Path.Combine(
                Path.GetDirectoryName(cataloguePath) ?? contentRootPath,
                "search-indexes")
            : indexDirectoryPath.Trim();
        var resolvedPath = Path.IsPathRooted(configuredPath)
            ? Path.GetFullPath(configuredPath)
            : Path.GetFullPath(configuredPath, contentRootPath);
        return new EvaluationDiagnosticSettings(cataloguePath, resolvedPath);
    }
}

public sealed record EvaluationDiagnosticSearchRequest(string Resolver, string Query);

public sealed record EvaluationDiagnosticSearchExecution(
    bool ResolverPreparedForThisRequest,
    EvaluationProcessMemory ProcessMemory,
    EvaluationDiagnosticSearchResponse Search);

public sealed record EvaluationProcessMemory(
    long WorkingSetBeforeResolverBytes,
    long WorkingSetAfterResolverBytes,
    long WorkingSetAfterSearchBytes,
    long ProcessPeakWorkingSetBytes);

public sealed record EvaluationDiagnosticDescription(
    int SchemaVersion,
    IReadOnlyList<string> Resolvers);

public static class EvaluationDiagnosticSearchValidation
{
    public static string? Validate(EvaluationDiagnosticSearchRequest? request)
    {
        if (request is null)
        {
            return "A JSON request body is required.";
        }

        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return "query is required.";
        }

        if (request.Query.Length > 500)
        {
            return "query must contain no more than 500 characters.";
        }

        if (PhuzzyTextForms.Create(request.Query).Tokens.Count > 20)
        {
            return "query must contain no more than 20 words.";
        }

        return request.Resolver is "catalogue-phuzzy-indexed"
            or "catalogue-lucene"
            or "catalogue-lucene-native"
            ? null
            : "resolver must be catalogue-phuzzy-indexed, catalogue-lucene, "
                + "or catalogue-lucene-native.";
    }
}

public sealed class EvaluationDiagnosticSearchService : IAsyncDisposable
{
    private static readonly EvaluationDiagnosticDescription description = new(
        1,
        ["catalogue-phuzzy-indexed", "catalogue-lucene", "catalogue-lucene-native"]);
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly IEvaluationDiagnosticResolverProvider resolverProvider;

    public EvaluationDiagnosticSearchService(EvaluationDiagnosticSettings settings)
        : this(new EvaluationDiagnosticResolverHost(settings))
    {
    }

    internal EvaluationDiagnosticSearchService(
        IEvaluationDiagnosticResolverProvider resolverProvider)
    {
        this.resolverProvider = resolverProvider;
    }

    public EvaluationDiagnosticDescription Description => description;

    public async Task<EvaluationDiagnosticSearchExecution> SearchAsync(
        EvaluationDiagnosticSearchRequest request,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            var workingSetBefore = process.WorkingSet64;
            var resolved = await resolverProvider.GetAsync(
                request.Resolver,
                cancellationToken);
            process.Refresh();
            var workingSetAfterResolver = process.WorkingSet64;
            var search = await resolved.Resolver.SearchDetailedAsync(
                request.Query,
                cancellationToken);
            process.Refresh();
            return new EvaluationDiagnosticSearchExecution(
                resolved.PreparedForThisRequest,
                new EvaluationProcessMemory(
                    workingSetBefore,
                    workingSetAfterResolver,
                    process.WorkingSet64,
                    process.PeakWorkingSet64),
                search);
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        gate.Dispose();
        await resolverProvider.DisposeAsync();
    }
}

internal interface IEvaluationDiagnosticResolverProvider : IAsyncDisposable
{
    Task<ResolvedDiagnosticResolver> GetAsync(
        string name,
        CancellationToken cancellationToken);
}

internal sealed class EvaluationDiagnosticResolverHost(
    EvaluationDiagnosticSettings settings) : IEvaluationDiagnosticResolverProvider
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, IEvaluationDiagnosticSearchResolver> resolvers =
        new(StringComparer.Ordinal);

    public async Task<ResolvedDiagnosticResolver> GetAsync(
        string name,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (resolvers.TryGetValue(name, out var existing))
            {
                return new ResolvedDiagnosticResolver(existing, false);
            }

            Directory.CreateDirectory(settings.IndexDirectoryPath);
            IEvaluationDiagnosticSearchResolver created = name switch
            {
                "catalogue-phuzzy-indexed" =>
                    await CataloguePhuzzyIndexedSearchResolver.CreateAsync(
                        settings.CataloguePath,
                        Path.Combine(settings.IndexDirectoryPath, "catalogue-phuzzy-index.db"),
                        cancellationToken),
                "catalogue-lucene" =>
                    await CatalogueLuceneSearchResolver.CreateAsync(
                        settings.CataloguePath,
                        Path.Combine(settings.IndexDirectoryPath, "catalogue-lucene-index"),
                        cancellationToken),
                "catalogue-lucene-native" =>
                    await CatalogueLuceneNativeSearchResolver.CreateAsync(
                        settings.CataloguePath,
                        Path.Combine(
                            settings.IndexDirectoryPath,
                            "catalogue-lucene-native-index"),
                        cancellationToken),
                _ => throw new InvalidOperationException(
                    $"Evaluation resolver '{name}' is not supported.")
            };
            resolvers.Add(name, created);
            return new ResolvedDiagnosticResolver(created, true);
        }
        finally
        {
            gate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        foreach (var resolver in resolvers.Values.OfType<IDisposable>())
        {
            resolver.Dispose();
        }

        gate.Dispose();
        return ValueTask.CompletedTask;
    }
}

internal sealed record ResolvedDiagnosticResolver(
    IEvaluationDiagnosticSearchResolver Resolver,
    bool PreparedForThisRequest);
