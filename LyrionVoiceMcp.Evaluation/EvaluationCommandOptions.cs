namespace LyrionVoiceMcp.Evaluation;

public abstract record EvaluationArgumentsOutcome;

public sealed record EvaluationArgumentsParsed(
    string CorpusPath,
    string OutputPath) : EvaluationArgumentsOutcome;

public sealed record EvaluationHelpRequested : EvaluationArgumentsOutcome;

public sealed record EvaluationArgumentsRejected(string Error) : EvaluationArgumentsOutcome;

public static class EvaluationCommandOptions
{
    public static EvaluationArgumentsOutcome Parse(
        IReadOnlyList<string> arguments,
        string repositoryRoot,
        DateTimeOffset now)
    {
        string? corpusPath = null;
        string? outputPath = null;
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (argument is "--help" or "-h")
            {
                return new EvaluationHelpRequested();
            }

            if (argument is not ("--corpus" or "--output" or "--resolver"))
            {
                return new EvaluationArgumentsRejected($"Unknown argument: {argument}");
            }

            if (++index >= arguments.Count || string.IsNullOrWhiteSpace(arguments[index]))
            {
                return new EvaluationArgumentsRejected($"{argument} requires a value.");
            }

            var value = arguments[index];
            switch (argument)
            {
                case "--corpus":
                    corpusPath = Path.GetFullPath(value, repositoryRoot);
                    break;
                case "--output":
                    outputPath = Path.GetFullPath(value, repositoryRoot);
                    break;
                case "--resolver" when value != "lms-pass-through":
                    return new EvaluationArgumentsRejected(
                        "The local evaluation runner supports only lms-pass-through; "
                        + "use /api/evaluation/search for production diagnostics.");
            }
        }

        corpusPath ??= Path.GetFullPath(Path.Combine(
            repositoryRoot,
            "..",
            "lyrion-voice-evaluation",
            "corpus.json"));
        outputPath ??= Path.Combine(
            repositoryRoot,
            ".data",
            "evaluation",
            $"lms-pass-through-{now:yyyyMMdd-HHmmss}.json");
        return new EvaluationArgumentsParsed(corpusPath, outputPath);
    }
}
