using System.Diagnostics;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Search;

namespace LyrionVoiceMcp.Evaluation;

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

        if (request.Query.Length > SearchQueryPolicy.MaximumLength)
        {
            return $"query must contain no more than {SearchQueryPolicy.MaximumLength} characters.";
        }

        if (SearchQueryPolicy.CountNormalisedTokens(request.Query)
            > SearchQueryPolicy.MaximumTokenCount)
        {
            return $"query must contain no more than {SearchQueryPolicy.MaximumTokenCount} words.";
        }

        return string.Equals(request.Resolver, "production", StringComparison.Ordinal)
            ? null
            : "resolver must be production.";
    }
}

public sealed class EvaluationDiagnosticSearchService(
    ProductionCatalogueSearchService productionSearch) : IAsyncDisposable
{
    private static readonly EvaluationDiagnosticDescription DescriptionValue = new(
        1,
        ["production"]);
    private readonly SemaphoreSlim gate = new(1, 1);

    public EvaluationDiagnosticDescription Description => DescriptionValue;

    public async Task<EvaluationDiagnosticSearchExecution> SearchAsync(
        EvaluationDiagnosticSearchRequest request,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            var before = process.WorkingSet64;
            var artifact = await productionSearch.GetArtifactAsync(cancellationToken);
            if (artifact is null)
            {
                throw new CatalogueSearchUnavailableException(
                    "The production catalogue search index has not been built.");
            }

            process.Refresh();
            var afterResolver = process.WorkingSet64;
            var search = await productionSearch.SearchDetailedAsync(
                request.Query,
                cancellationToken);
            process.Refresh();
            return new EvaluationDiagnosticSearchExecution(
                false,
                new EvaluationProcessMemory(
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

    public ValueTask DisposeAsync()
    {
        gate.Dispose();
        return ValueTask.CompletedTask;
    }
}
