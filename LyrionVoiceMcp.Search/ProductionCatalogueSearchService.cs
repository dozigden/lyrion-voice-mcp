using System.Text.Json;
using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Search;

public sealed record ProductionSearchSettings(string IndexDirectoryPath)
{
    public static ProductionSearchSettings FromValues(
        string contentRootPath,
        string? indexDirectoryPath)
    {
        var configured = string.IsNullOrWhiteSpace(indexDirectoryPath)
            ? Path.Combine(contentRootPath, ".data", "search-index")
            : indexDirectoryPath.Trim();
        return new ProductionSearchSettings(Path.IsPathRooted(configured)
            ? Path.GetFullPath(configured)
            : Path.GetFullPath(configured, contentRootPath));
    }
}

public sealed class ProductionCatalogueSearchService :
    ISearchIndexBuilder,
    ICatalogueSearchResolver,
    ICatalogueArtistTrackResolver,
    ICatalogueTrackResolver,
    ICatalogueAlbumResolver,
    ICatalogueArtistAlbumResolver,
    IDiagnosticSearchResolver,
    IRatingBrowseResolver,
    IAsyncDisposable
{
    private const string ManifestFileName = "manifest.json";
    private const string IndexFileName = "search.db";
    private const string PointerFileName = "current.json";
    private static readonly SearchResolverDescriptor DescriptorValue = new(
        "catalogue-phuzzy-sqlite",
        "7");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly ProductionSearchSettings settings;
    private readonly ICatalogueSearchDocumentSource documentSource;
    private readonly TimeProvider timeProvider;
    private LoadedGeneration? loaded;

    public SearchResolverDescriptor Descriptor => DescriptorValue;

    public ProductionCatalogueSearchService(
        ProductionSearchSettings settings,
        ICatalogueSearchDocumentSource documentSource,
        TimeProvider timeProvider)
    {
        this.settings = settings;
        this.documentSource = documentSource;
        this.timeProvider = timeProvider;
    }

    public async Task<CatalogueSearchResponse> SearchAsync(
        string query,
        RatingSearchConstraint? ratingConstraint,
        CancellationToken cancellationToken) =>
        await SearchAsync(
            query,
            ratingConstraint is null
                ? null
                : new CatalogueTrackSearchConstraint(ratingConstraint),
            cancellationToken);

    public async Task<CatalogueSearchResponse> SearchAsync(
        string query,
        CatalogueTrackSearchConstraint? constraint,
        CancellationToken cancellationToken) =>
        await SearchAsync(
            query,
            constraint is null ? null : new CatalogueSearchConstraint(constraint),
            cancellationToken);

    public async Task<CatalogueSearchResponse> SearchAsync(
        string query,
        CatalogueSearchConstraint? constraint,
        CancellationToken cancellationToken)
    {
        var generation = await GetLoadedAsync(cancellationToken);
        if (generation is null)
        {
            throw new CatalogueSearchUnavailableException(
                "The production catalogue search index has not been built.");
        }

        return await generation.Resolver.SearchCatalogueAsync(
            query,
            constraint,
            cancellationToken);
    }

    public Task<CatalogueSearchResponse> SearchAsync(
        string query,
        CancellationToken cancellationToken) =>
        SearchAsync(query, (CatalogueSearchConstraint?)null, cancellationToken);

    public async IAsyncEnumerable<CatalogueSearchCandidate> ReadArtistTracksAsync(
        string artistId,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        var generation = await GetLoadedAsync(cancellationToken);
        if (generation is null)
        {
            throw new CatalogueSearchUnavailableException(
                "The production catalogue search index has not been built.");
        }

        await foreach (var candidate in generation.Resolver.ReadArtistTracksAsync(
            artistId,
            cancellationToken))
        {
            yield return candidate;
        }
    }

    public async IAsyncEnumerable<CatalogueSearchCandidate> ReadArtistTracksAsync(
        string artistId,
        CatalogueTrackSearchConstraint? constraint,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        var generation = await GetLoadedAsync(cancellationToken);
        if (generation is null)
        {
            throw new CatalogueSearchUnavailableException(
                "The production catalogue search index has not been built.");
        }

        await foreach (var candidate in generation.Resolver.ReadArtistTracksAsync(
            artistId,
            constraint,
            cancellationToken))
        {
            yield return candidate;
        }
    }

    public async IAsyncEnumerable<CatalogueSearchCandidate> ReadTracksAsync(
        CatalogueTrackSearchConstraint constraint,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        var generation = await GetLoadedAsync(cancellationToken);
        if (generation is null)
        {
            throw new CatalogueSearchUnavailableException(
                "The production catalogue search index has not been built.");
        }

        await foreach (var candidate in generation.Resolver.ReadTracksAsync(
            constraint,
            cancellationToken))
        {
            yield return candidate;
        }
    }

    public async IAsyncEnumerable<CatalogueSearchCandidate> ReadArtistAlbumsAsync(
        string artistId,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        await foreach (var candidate in ReadArtistAlbumsAsync(
            artistId,
            null,
            cancellationToken))
        {
            yield return candidate;
        }
    }

    public async IAsyncEnumerable<CatalogueSearchCandidate> ReadArtistAlbumsAsync(
        string artistId,
        CatalogueAlbumSearchConstraint? constraint,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        var generation = await GetLoadedAsync(cancellationToken);
        if (generation is null)
        {
            throw new CatalogueSearchUnavailableException(
                "The production catalogue search index has not been built.");
        }

        await foreach (var candidate in generation.Resolver.ReadArtistAlbumsAsync(
            artistId,
            constraint,
            cancellationToken))
        {
            yield return candidate;
        }
    }

    public async IAsyncEnumerable<CatalogueSearchCandidate> ReadAlbumsAsync(
        CatalogueAlbumSearchConstraint constraint,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        var generation = await GetLoadedAsync(cancellationToken);
        if (generation is null)
        {
            throw new CatalogueSearchUnavailableException(
                "The production catalogue search index has not been built.");
        }

        await foreach (var candidate in generation.Resolver.ReadAlbumsAsync(
            constraint,
            cancellationToken))
        {
            yield return candidate;
        }
    }

    public async Task<SearchDiagnostics> SearchDetailedAsync(
        string query,
        CatalogueSearchConstraint? constraint,
        CancellationToken cancellationToken)
    {
        var generation = await GetLoadedAsync(cancellationToken);
        if (generation is null)
        {
            throw new CatalogueSearchUnavailableException(
                "The production catalogue search index has not been built.");
        }

        return await generation.Resolver.SearchDetailedAsync(
            query,
            constraint,
            cancellationToken);
    }

    public Task<SearchDiagnostics> SearchDetailedAsync(
        string query,
        RatingSearchConstraint? ratingConstraint,
        CancellationToken cancellationToken) =>
        SearchDetailedAsync(
            query,
            ratingConstraint is null
                ? null
                : new CatalogueSearchConstraint(
                    new CatalogueTrackSearchConstraint(ratingConstraint)),
            cancellationToken);

    public Task<SearchDiagnostics> SearchDetailedAsync(
        string query,
        CatalogueTrackSearchConstraint? constraint,
        CancellationToken cancellationToken) =>
        SearchDetailedAsync(
            query,
            constraint is null ? null : new CatalogueSearchConstraint(constraint),
            cancellationToken);

    public Task<SearchDiagnostics> SearchDetailedAsync(
        string query,
        CancellationToken cancellationToken) =>
        SearchDetailedAsync(query, (CatalogueSearchConstraint?)null, cancellationToken);

    public async Task<SearchDiagnostics> SearchConstrainedDetailedAsync(
        CatalogueSearchConstraint constraint,
        CancellationToken cancellationToken)
    {
        var generation = await GetLoadedAsync(cancellationToken);
        if (generation is null)
        {
            throw new CatalogueSearchUnavailableException(
                "The production catalogue search index has not been built.");
        }

        return await generation.Resolver.SearchConstrainedDetailedAsync(
            constraint,
            cancellationToken);
    }

    public async Task<RatingBrowsePage> BrowseAsync(
        int bucket,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        var generation = await GetLoadedAsync(cancellationToken);
        if (generation is null)
        {
            throw new CatalogueSearchUnavailableException(
                "The production catalogue search index has not been built.");
        }

        return await generation.Resolver.BrowseRatingsAsync(
            bucket,
            offset,
            limit,
            cancellationToken);
    }

    public async Task<SearchIndexArtifact?> GetArtifactAsync(
        CancellationToken cancellationToken) =>
        (await GetLoadedAsync(cancellationToken))?.Artifact;

    public async Task<SearchIndexRebuildResult> RebuildAsync(
        string catalogueRefreshId,
        long jobId,
        ISearchIndexProgress progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(settings.IndexDirectoryPath);
        var generationName = $"generation-{jobId}-{Guid.NewGuid():N}";
        var stagingName = $".{generationName}.building";
        var stagingDirectory = Path.Combine(settings.IndexDirectoryPath, stagingName);
        var generationDirectory = Path.Combine(settings.IndexDirectoryPath, generationName);
        Directory.CreateDirectory(stagingDirectory);

        try
        {
            await progress.ReportAsync(
                "Building production catalogue search index.",
                new { catalogueRefreshId, generation = generationName },
                cancellationToken);
            var indexPath = Path.Combine(stagingDirectory, IndexFileName);
            var resolver = await SqliteCatalogueSearchIndex.CreateAsync(
                documentSource,
                Descriptor,
                catalogueRefreshId,
                indexPath,
                progress,
                cancellationToken);
            var artifact = new SearchIndexArtifact(
                Descriptor.Name,
                Descriptor.Version,
                catalogueRefreshId,
                timeProvider.GetUtcNow(),
                resolver.Metrics.IndexedCandidateCount
                    ?? throw new InvalidOperationException(
                        "The completed search index did not report a candidate count."),
                resolver.Metrics.PreparationDurationMilliseconds,
                resolver.Metrics.IndexSizeBytes
                    ?? throw new InvalidOperationException(
                        "The completed search index did not report its size."));
            await WriteJsonAsync(
                Path.Combine(stagingDirectory, ManifestFileName),
                artifact,
                cancellationToken);

            var validated = SqliteCatalogueSearchIndex.Open(indexPath, artifact, Descriptor);
            await validated.SearchCatalogueAsync(
                "validation",
                (CatalogueSearchConstraint?)null,
                cancellationToken);
            Directory.Move(stagingDirectory, generationDirectory);
            var publishedResolver = SqliteCatalogueSearchIndex.Open(
                Path.Combine(generationDirectory, IndexFileName),
                artifact,
                Descriptor);

            await progress.ReportAsync(
                "Publishing production catalogue search index.",
                artifact,
                cancellationToken);
            string? previousGeneration;
            await gate.WaitAsync(cancellationToken);
            try
            {
                previousGeneration = loaded?.Name;
                var pointerTemporaryPath = Path.Combine(
                    settings.IndexDirectoryPath,
                    $".{PointerFileName}.{Guid.NewGuid():N}.tmp");
                await WriteJsonAsync(
                    pointerTemporaryPath,
                    new GenerationPointer(generationName),
                    cancellationToken);
                File.Move(
                    pointerTemporaryPath,
                    Path.Combine(settings.IndexDirectoryPath, PointerFileName),
                    overwrite: true);
                loaded = new LoadedGeneration(generationName, artifact, publishedResolver);
            }
            finally
            {
                gate.Release();
            }

            TryCleanOldGenerations(generationName, previousGeneration);

            return new SearchIndexRebuildResult(artifact);
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, recursive: true);
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        gate.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<LoadedGeneration?> GetLoadedAsync(CancellationToken cancellationToken)
    {
        if (loaded is not null)
        {
            return loaded;
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            if (loaded is not null)
            {
                return loaded;
            }

            var pointer = await ReadJsonAsync<GenerationPointer>(
                Path.Combine(settings.IndexDirectoryPath, PointerFileName),
                cancellationToken);
            if (pointer is null || !IsSafeGenerationName(pointer.Generation))
            {
                return null;
            }

            var directory = Path.Combine(settings.IndexDirectoryPath, pointer.Generation);
            var artifact = await ReadJsonAsync<SearchIndexArtifact>(
                Path.Combine(directory, ManifestFileName),
                cancellationToken);
            if (artifact is null
                || !string.Equals(artifact.Resolver, Descriptor.Name, StringComparison.Ordinal)
                || !string.Equals(
                    artifact.ResolverVersion,
                    Descriptor.Version,
                    StringComparison.Ordinal))
            {
                return null;
            }

            var indexPath = Path.Combine(directory, IndexFileName);
            if (!File.Exists(indexPath))
            {
                return null;
            }

            loaded = new LoadedGeneration(
                pointer.Generation,
                artifact,
                SqliteCatalogueSearchIndex.Open(indexPath, artifact, Descriptor));
            return loaded;
        }
        finally
        {
            gate.Release();
        }
    }

    private static bool IsSafeGenerationName(string value) =>
        value.StartsWith("generation-", StringComparison.Ordinal)
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');

    private void TryCleanOldGenerations(string currentGeneration, string? previousGeneration)
    {
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(
                settings.IndexDirectoryPath,
                "generation-*",
                SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(directory);
                if (string.Equals(name, currentGeneration, StringComparison.Ordinal)
                    || string.Equals(name, previousGeneration, StringComparison.Ordinal))
                {
                    continue;
                }

                try
                {
                    Directory.Delete(directory, recursive: true);
                }
                catch (Exception)
                {
                    // A query may still hold this generation. A later rebuild will retry cleanup.
                }
            }
        }
        catch (Exception)
        {
            // Cleanup is best effort and must not change a successfully published job outcome.
        }
    }

    private static async Task WriteJsonAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            16 * 1024,
            FileOptions.Asynchronous);
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task<T?> ReadJsonAsync<T>(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return default;
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            16 * 1024,
            FileOptions.Asynchronous);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    private sealed record GenerationPointer(string Generation);
    private sealed record LoadedGeneration(
        string Name,
        SearchIndexArtifact Artifact,
        SqliteCatalogueSearchIndex Resolver);
}
