using LyrionVoiceMcp.Evaluation;

namespace LyrionVoiceMcp.Evaluation.Tests;

public sealed class EvaluationCommandOptionsTests
{
    [Fact]
    public void DefaultsShouldRetainThePrivateCorpusBoundaryAndLmsBaseline()
    {
        var root = Path.GetFullPath("/fictional/repository");

        var outcome = EvaluationCommandOptions.Parse(
            [],
            root,
            DateTimeOffset.Parse("2026-08-16T12:34:56Z"));

        var parsed = Assert.IsType<EvaluationArgumentsParsed>(outcome);
        Assert.Equal(
            Path.GetFullPath("/fictional/lyrion-voice-evaluation/corpus.json"),
            parsed.CorpusPath);
        Assert.EndsWith(
            "lms-pass-through-20260816-123456.json",
            parsed.OutputPath,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RetiredComparatorShouldBeRejected()
    {
        var outcome = EvaluationCommandOptions.Parse(
            ["--resolver", "catalogue-lucene"],
            "/fictional/repository",
            DateTimeOffset.Parse("2026-08-16T12:34:56Z"));

        Assert.IsType<EvaluationArgumentsRejected>(outcome);
    }
}
