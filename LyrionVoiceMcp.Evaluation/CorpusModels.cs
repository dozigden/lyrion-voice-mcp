using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Evaluation;

public sealed record EvaluationCorpus(
    int SchemaVersion,
    IReadOnlyList<EvaluationCase> Cases);

public sealed record EvaluationCase(
    string Id,
    string Query,
    IReadOnlyList<ExpectedEntity> Expected,
    string Category,
    string? Notes);

public sealed record ExpectedEntity(
    MediaEntityKind Kind,
    string Title,
    string? Artist,
    string? Album);

public abstract record CorpusReadOutcome;

public sealed record CorpusRead(
    EvaluationCorpus Corpus,
    string ContentHash) : CorpusReadOutcome;

public sealed record CorpusRejected(
    IReadOnlyList<string> Errors) : CorpusReadOutcome;
