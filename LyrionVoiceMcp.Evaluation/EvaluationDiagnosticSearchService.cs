using System.Diagnostics;
using System.Text.Json;
using LyrionVoiceMcp.Abstractions;

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

public sealed class SearchIndexUnavailableException(string message) : Exception(message);

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

        return EvaluationIndexFiles.Resolvers.Contains(request.Resolver, StringComparer.Ordinal)
            ? null
            : "resolver must be catalogue-phuzzy-indexed, catalogue-lucene, "
                + "or catalogue-lucene-native.";
    }
}

public sealed class EvaluationDiagnosticSearchService :
    IAsyncDisposable,
    ISearchIndexBuilder
{
    private static readonly EvaluationDiagnosticDescription description = new(
        1,
        EvaluationIndexFiles.Resolvers);
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly IEvaluationDiagnosticResolverProvider resolverProvider;
    private readonly EvaluationDiagnosticSettings? settings;
    private readonly TimeProvider timeProvider;

    public EvaluationDiagnosticSearchService(
        EvaluationDiagnosticSettings settings,
        TimeProvider timeProvider)
        : this(
            new EvaluationDiagnosticResolverHost(settings),
            settings,
            timeProvider)
    {
    }

    internal EvaluationDiagnosticSearchService(
        IEvaluationDiagnosticResolverProvider resolverProvider)
        : this(resolverProvider, null, TimeProvider.System)
    {
    }

    private EvaluationDiagnosticSearchService(
        IEvaluationDiagnosticResolverProvider resolverProvider,
        EvaluationDiagnosticSettings? settings,
        TimeProvider timeProvider)
    {
        this.resolverProvider = resolverProvider;
        this.settings = settings;
        this.timeProvider = timeProvider;
    }

    public EvaluationDiagnosticDescription Description => description;
    public IReadOnlyList<string> Resolvers => EvaluationIndexFiles.Resolvers;

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
                false,
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

    public async Task<SearchIndexArtifact?> GetArtifactAsync(
        string resolver,
        CancellationToken cancellationToken)
    {
        var configuredSettings = RequireSettings();
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await EvaluationIndexFiles.ReadArtifactAsync(
                configuredSettings,
                resolver,
                cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<SearchIndexRebuildResult> RebuildAsync(
        string resolver,
        string catalogueRefreshId,
        long jobId,
        ISearchIndexProgress progress,
        CancellationToken cancellationToken)
    {
        var configuredSettings = RequireSettings();
        EvaluationIndexFiles.EnsureSupported(resolver);
        Directory.CreateDirectory(configuredSettings.IndexDirectoryPath);
        var stagingDirectory = Path.Combine(
            configuredSettings.IndexDirectoryPath,
            $".{resolver}.building-{jobId}");
        if (Directory.Exists(stagingDirectory))
        {
            Directory.Delete(stagingDirectory, recursive: true);
        }

        IEvaluationDiagnosticSearchResolver? builtResolver = null;
        try
        {
            await progress.ReportAsync(
                "Building search-index artifact.",
                new { resolver, catalogueRefreshId },
                cancellationToken);
            builtResolver = await EvaluationIndexFiles.BuildAsync(
                configuredSettings,
                resolver,
                stagingDirectory,
                cancellationToken);
            var artifact = new SearchIndexArtifact(
                resolver,
                builtResolver.Version,
                catalogueRefreshId,
                timeProvider.GetUtcNow(),
                builtResolver.Metrics.IndexedCandidateCount
                    ?? throw new InvalidOperationException(
                        "The built resolver did not report its candidate count."),
                builtResolver.Metrics.PreparationDurationMilliseconds,
                builtResolver.Metrics.IndexSizeBytes
                    ?? throw new InvalidOperationException(
                        "The built resolver did not report its index size."));
            await EvaluationIndexFiles.WriteArtifactAsync(
                stagingDirectory,
                artifact,
                cancellationToken);
            (builtResolver as IDisposable)?.Dispose();
            builtResolver = null;

            await progress.ReportAsync(
                "Publishing completed search-index artifact.",
                artifact,
                cancellationToken);
            await gate.WaitAsync(cancellationToken);
            try
            {
                await resolverProvider.RemoveAsync(resolver);
                EvaluationIndexFiles.Publish(
                    configuredSettings,
                    resolver,
                    stagingDirectory);
            }
            finally
            {
                gate.Release();
            }

            return new SearchIndexRebuildResult(artifact);
        }
        finally
        {
            (builtResolver as IDisposable)?.Dispose();
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, recursive: true);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        gate.Dispose();
        await resolverProvider.DisposeAsync();
    }

    private EvaluationDiagnosticSettings RequireSettings() => settings
        ?? throw new InvalidOperationException(
            "Search-index building is not available from this test service.");
}

internal interface IEvaluationDiagnosticResolverProvider : IAsyncDisposable
{
    Task<ResolvedDiagnosticResolver> GetAsync(
        string name,
        CancellationToken cancellationToken);

    ValueTask RemoveAsync(string name);
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

            var artifact = await EvaluationIndexFiles.ReadArtifactAsync(
                settings,
                name,
                cancellationToken);
            if (artifact is null)
            {
                throw new SearchIndexUnavailableException(
                    $"Search index '{name}' has not been built.");
            }

            var created = EvaluationIndexFiles.Open(settings, name, artifact);
            resolvers.Add(name, created);
            return new ResolvedDiagnosticResolver(created, false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask RemoveAsync(string name)
    {
        await gate.WaitAsync();
        try
        {
            if (resolvers.Remove(name, out var resolver))
            {
                (resolver as IDisposable)?.Dispose();
            }
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

internal static class EvaluationIndexFiles
{
    private const string ManifestFileName = "manifest.json";
    private static readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<string> Resolvers { get; } =
    [
        "catalogue-phuzzy-indexed",
        "catalogue-lucene",
        "catalogue-lucene-native"
    ];

    public static void EnsureSupported(string resolver)
    {
        if (!Resolvers.Contains(resolver, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Evaluation resolver '{resolver}' is not supported.");
        }
    }

    public static async Task<SearchIndexArtifact?> ReadArtifactAsync(
        EvaluationDiagnosticSettings settings,
        string resolver,
        CancellationToken cancellationToken)
    {
        EnsureSupported(resolver);
        var path = Path.Combine(ResolverDirectory(settings, resolver), ManifestFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var artifact = await JsonSerializer.DeserializeAsync<SearchIndexArtifact>(
                stream,
                jsonOptions,
                cancellationToken);
            return artifact is not null
                && string.Equals(artifact.Resolver, resolver, StringComparison.Ordinal)
                && string.Equals(
                    artifact.ResolverVersion,
                    ExpectedVersion(resolver),
                    StringComparison.Ordinal)
                ? artifact
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static async Task<IEvaluationDiagnosticSearchResolver> BuildAsync(
        EvaluationDiagnosticSettings settings,
        string resolver,
        string stagingDirectory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(stagingDirectory);
        return resolver switch
        {
            "catalogue-phuzzy-indexed" =>
                await CataloguePhuzzyIndexedSearchResolver.CreateAsync(
                    settings.CataloguePath,
                    Path.Combine(stagingDirectory, "index.db"),
                    cancellationToken),
            "catalogue-lucene" =>
                await CatalogueLuceneSearchResolver.CreateAsync(
                    settings.CataloguePath,
                    Path.Combine(stagingDirectory, "index"),
                    cancellationToken),
            "catalogue-lucene-native" =>
                await CatalogueLuceneNativeSearchResolver.CreateAsync(
                    settings.CataloguePath,
                    Path.Combine(stagingDirectory, "index"),
                    cancellationToken),
            _ => throw new InvalidOperationException(
                $"Evaluation resolver '{resolver}' is not supported.")
        };
    }

    public static IEvaluationDiagnosticSearchResolver Open(
        EvaluationDiagnosticSettings settings,
        string resolver,
        SearchIndexArtifact artifact)
    {
        var directory = ResolverDirectory(settings, resolver);
        return resolver switch
        {
            "catalogue-phuzzy-indexed" => CataloguePhuzzyIndexedSearchResolver.Open(
                Path.Combine(directory, "index.db"),
                artifact),
            "catalogue-lucene" => CatalogueLuceneSearchResolver.Open(
                Path.Combine(directory, "index"),
                artifact),
            "catalogue-lucene-native" => CatalogueLuceneNativeSearchResolver.Open(
                Path.Combine(directory, "index"),
                artifact),
            _ => throw new InvalidOperationException(
                $"Evaluation resolver '{resolver}' is not supported.")
        };
    }

    public static async Task WriteArtifactAsync(
        string directory,
        SearchIndexArtifact artifact,
        CancellationToken cancellationToken)
    {
        await using var stream = File.Create(Path.Combine(directory, ManifestFileName));
        await JsonSerializer.SerializeAsync(
            stream,
            artifact,
            jsonOptions,
            cancellationToken);
    }

    public static void Publish(
        EvaluationDiagnosticSettings settings,
        string resolver,
        string stagingDirectory)
    {
        var publishedDirectory = ResolverDirectory(settings, resolver);
        if (Directory.Exists(publishedDirectory))
        {
            Directory.Delete(publishedDirectory, recursive: true);
        }

        Directory.Move(stagingDirectory, publishedDirectory);
    }

    private static string ResolverDirectory(
        EvaluationDiagnosticSettings settings,
        string resolver) =>
        Path.Combine(settings.IndexDirectoryPath, resolver);

    private static string ExpectedVersion(string resolver) => resolver switch
    {
        "catalogue-phuzzy-indexed" => "2",
        "catalogue-lucene" => "1",
        "catalogue-lucene-native" => "1",
        _ => throw new InvalidOperationException(
            $"Evaluation resolver '{resolver}' is not supported.")
    };
}
