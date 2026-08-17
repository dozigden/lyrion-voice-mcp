using System.Diagnostics;
namespace LyrionVoiceMcp.Evaluation;

public sealed class EvaluationRunner(
    ISearchResolver resolver,
    TimeProvider timeProvider)
{
    public async Task<EvaluationReport> RunAsync(
        EvaluationCorpus corpus,
        string corpusHash,
        CancellationToken cancellationToken)
    {
        var results = new List<EvaluationCaseResult>(corpus.Cases.Count);
        foreach (var item in corpus.Cases)
        {
            results.Add(await RunCaseAsync(item, cancellationToken));
        }

        return new EvaluationReport(
            2,
            timeProvider.GetUtcNow(),
            corpusHash,
            resolver.Descriptor.Name,
            resolver.Descriptor.Version,
            resolver.Metrics,
            Summarise(results),
            results);
    }

    private async Task<EvaluationCaseResult> RunCaseAsync(
        EvaluationCase item,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await resolver.SearchAsync(item.Query, cancellationToken);
            stopwatch.Stop();
            return BuildResult(
                item,
                response.Candidates,
                stopwatch.ElapsedMilliseconds,
                response.Error);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            stopwatch.Stop();
            return BuildResult(item, [], stopwatch.ElapsedMilliseconds, exception.Message);
        }
    }

    private static EvaluationCaseResult BuildResult(
        EvaluationCase item,
        IReadOnlyList<SearchCandidate> candidates,
        long durationMilliseconds,
        string? error)
    {
        var reportCandidates = candidates.Select((candidate, index) =>
            new EvaluationResultCandidate(
                index + 1,
                candidate.Kind.ToString().ToLowerInvariant(),
                candidate.Title,
                candidate.Artist,
                candidate.Album,
                item.Expected.Any(expected => Matches(expected, candidate))))
            .ToArray();
        var firstMatchPosition = reportCandidates
            .FirstOrDefault(candidate => candidate.Expected)
            ?.Position;
        var isNoMatch = item.Expected.Count == 0;
        var passed = error is null
            && (isNoMatch ? reportCandidates.Length == 0 : firstMatchPosition is not null);
        var reciprocalRank = firstMatchPosition is null ? 0 : 1d / firstMatchPosition.Value;

        return new EvaluationCaseResult(
            item.Id,
            item.Query,
            item.Category,
            isNoMatch,
            passed,
            firstMatchPosition,
            reciprocalRank,
            durationMilliseconds,
            error,
            reportCandidates);
    }

    private static bool Matches(ExpectedEntity expected, SearchCandidate candidate) =>
        expected.Kind == candidate.Kind
        && Same(expected.Title, candidate.Title)
        && OptionalSame(expected.Artist, candidate.Artist)
        && OptionalSame(expected.Album, candidate.Album);

    private static bool Same(string expected, string? actual) =>
        actual is not null
        && string.Equals(expected.Trim(), actual.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool OptionalSame(string? expected, string? actual) =>
        expected is null || Same(expected, actual);

    private static EvaluationSummary Summarise(IReadOnlyList<EvaluationCaseResult> results)
    {
        var positive = results.Where(item => !item.IsNoMatchCase).ToArray();
        var noMatch = results.Where(item => item.IsNoMatchCase).ToArray();
        var orderedDurations = results
            .Select(item => item.DurationMilliseconds)
            .Order()
            .ToArray();
        var p95Index = Math.Max(0, (int)Math.Ceiling(orderedDurations.Length * 0.95) - 1);

        return new EvaluationSummary(
            results.Count,
            positive.Length,
            noMatch.Length,
            results.Count(item => item.Passed),
            results.Count(item => item.Error is not null),
            positive.Count(item => item.FirstMatchPosition == 1),
            positive.Count(item => item.FirstMatchPosition is >= 1 and <= 5),
            noMatch.Count(item => item.Passed),
            positive.Length == 0 ? 0 : positive.Average(item => item.ReciprocalRank),
            results.Average(item => item.DurationMilliseconds),
            orderedDurations[p95Index]);
    }
}
