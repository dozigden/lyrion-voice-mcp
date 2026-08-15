namespace LyrionVoiceMcp.Evaluation.Tests;

public sealed class EvaluationCommandOptionsTests
{
    [Fact]
    public void Parse_defaults_to_the_private_sibling_corpus_and_evaluation_output()
    {
        var root = Path.Combine(Path.GetTempPath(), "lyrion-voice-mcp");
        var now = new DateTimeOffset(2026, 8, 13, 9, 10, 11, TimeSpan.Zero);

        var outcome = EvaluationCommandOptions.Parse([], root, now);

        var parsed = Assert.IsType<EvaluationArgumentsParsed>(outcome);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(root, "..", "lyrion-voice-evaluation", "corpus.json")),
            parsed.CorpusPath);
        Assert.EndsWith(
            Path.Combine(".data", "evaluation", "lms-pass-through-20260813-091011.json"),
            parsed.OutputPath,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_rejects_the_removed_settings_option()
    {
        var root = Path.Combine(Path.GetTempPath(), "lyrion-voice-mcp");

        var outcome = EvaluationCommandOptions.Parse(
            ["--settings", "settings.json"],
            root,
            DateTimeOffset.UtcNow);

        var rejected = Assert.IsType<EvaluationArgumentsRejected>(outcome);
        Assert.Contains("Unknown argument", rejected.Error, StringComparison.Ordinal);
    }
}
