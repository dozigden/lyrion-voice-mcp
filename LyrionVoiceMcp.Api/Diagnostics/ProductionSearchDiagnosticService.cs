using System.Diagnostics;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Search;

namespace LyrionVoiceMcp.Api.Diagnostics;

public sealed record ProductionSearchDiagnosticRequest(
    string Resolver,
    string Query,
    decimal? Rating = null,
    string? RatingMatch = null);

public sealed record ProductionSearchDiagnosticExecution(
    bool ResolverPreparedForThisRequest,
    ProcessMemorySnapshot ProcessMemory,
    SearchDiagnostics Search);

public sealed record ProcessMemorySnapshot(
    long WorkingSetBeforeResolverBytes,
    long WorkingSetAfterResolverBytes,
    long WorkingSetAfterSearchBytes,
    long ProcessPeakWorkingSetBytes);

public sealed record ProductionSearchDiagnosticDescription(
    int SchemaVersion,
    IReadOnlyList<string> Resolvers);

public static class ProductionSearchDiagnosticValidation
{
    public static string? Validate(ProductionSearchDiagnosticRequest? request)
    {
        if (request is null)
        {
            return "A JSON request body is required.";
        }

        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return "query is required.";
        }

        if (request.Query.Length > SearchQueryPolicy.MaximumLength)
        {
            return $"query must contain no more than {SearchQueryPolicy.MaximumLength} characters.";
        }

        if (SearchQueryPolicy.CountNormalisedTokens(request.Query)
            > SearchQueryPolicy.MaximumTokenCount)
        {
            return $"query must contain no more than {SearchQueryPolicy.MaximumTokenCount} words.";
        }

        if ((request.Rating is null) != (request.RatingMatch is null))
        {
            return "rating and ratingMatch must be supplied together.";
        }

        if (request.Rating is < 0 or > 5)
        {
            return "rating must be from 0 to 5.";
        }

        if (request.RatingMatch is not null
            && request.RatingMatch is not "exact" and not "at_least")
        {
            return "ratingMatch must be exact or at_least.";
        }

        return string.Equals(request.Resolver, "production", StringComparison.Ordinal)
            ? null
            : "resolver must be production.";
    }
}

public sealed class ProductionSearchDiagnosticService(
    IDiagnosticSearchResolver resolver,
    ISearchIndexBuilder indexBuilder) : IAsyncDisposable
{
    private static readonly ProductionSearchDiagnosticDescription DescriptionValue = new(
        2,
        ["production"]);
    private readonly SemaphoreSlim gate = new(1, 1);

    public ProductionSearchDiagnosticDescription Description => DescriptionValue;

    public async Task<ProductionSearchDiagnosticExecution> SearchAsync(
        ProductionSearchDiagnosticRequest request,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            var before = process.WorkingSet64;
            var artifact = await indexBuilder.GetArtifactAsync(cancellationToken);
            if (artifact is null)
            {
                throw new CatalogueSearchUnavailableException(
                    "The production catalogue search index has not been built.");
            }

            process.Refresh();
            var afterResolver = process.WorkingSet64;
            var search = await resolver.SearchDetailedAsync(
                request.Query,
                CreateRatingConstraint(request),
                cancellationToken);
            process.Refresh();
            return new ProductionSearchDiagnosticExecution(
                false,
                new ProcessMemorySnapshot(
                    before,
                    afterResolver,
                    process.WorkingSet64,
                    process.PeakWorkingSet64),
                search with { Resolver = "production" });
        }
        finally
        {
            gate.Release();
        }
    }

    private static RatingSearchConstraint? CreateRatingConstraint(
        ProductionSearchDiagnosticRequest request)
    {
        if (request.Rating is null || request.RatingMatch is null)
        {
            return null;
        }

        return new RatingSearchConstraint(
            request.Rating.Value,
            request.RatingMatch == "exact"
                ? RatingMatchMode.Exact
                : RatingMatchMode.AtLeast);
    }

    public ValueTask DisposeAsync()
    {
        gate.Dispose();
        return ValueTask.CompletedTask;
    }
}
