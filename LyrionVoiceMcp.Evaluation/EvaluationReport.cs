namespace LyrionVoiceMcp.Evaluation;

public sealed record EvaluationReport(
    int SchemaVersion,
    DateTimeOffset RunAt,
    string CorpusHash,
    string Resolver,
    string ResolverVersion,
    EvaluationSummary Summary,
    IReadOnlyList<EvaluationCaseResult> Cases);

public sealed record EvaluationSummary(
    int TotalCases,
    int PositiveCases,
    int NoMatchCases,
    int PassedCases,
    int ErrorCases,
    int Top1Matches,
    int Top5Matches,
    int CorrectNoMatches,
    double MeanReciprocalRank,
    double MeanDurationMilliseconds,
    long P95DurationMilliseconds);

public sealed record EvaluationCaseResult(
    string Id,
    string Query,
    string Category,
    bool IsNoMatchCase,
    bool Passed,
    int? FirstMatchPosition,
    double ReciprocalRank,
    long DurationMilliseconds,
    string? Error,
    IReadOnlyList<EvaluationResultCandidate> Results);

public sealed record EvaluationResultCandidate(
    int Position,
    string Kind,
    string Title,
    string? Artist,
    string? Album,
    bool Expected);
