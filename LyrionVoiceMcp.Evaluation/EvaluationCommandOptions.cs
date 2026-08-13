namespace LyrionVoiceMcp.Evaluation;

public abstract record EvaluationArgumentsOutcome;

public sealed record EvaluationArgumentsParsed(
    string CorpusPath,
    string SettingsPath,
    string OutputPath) : EvaluationArgumentsOutcome;

public sealed record EvaluationHelpRequested : EvaluationArgumentsOutcome;

public sealed record EvaluationArgumentsRejected(
    string Error) : EvaluationArgumentsOutcome;

public static class EvaluationCommandOptions
{
    public static EvaluationArgumentsOutcome Parse(
        IReadOnlyList<string> arguments,
        string repositoryRoot,
        DateTimeOffset now)
    {
        string? corpusPath = null;
        string? settingsPath = null;
        string? outputPath = null;
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (argument is "--help" or "-h")
            {
                return new EvaluationHelpRequested();
            }

            if (argument is not ("--corpus" or "--settings" or "--output"))
            {
                return new EvaluationArgumentsRejected($"Unknown argument: {argument}");
            }

            if (++index >= arguments.Count || string.IsNullOrWhiteSpace(arguments[index]))
            {
                return new EvaluationArgumentsRejected($"{argument} requires a path.");
            }

            var value = Path.GetFullPath(arguments[index], repositoryRoot);
            switch (argument)
            {
                case "--corpus":
                    corpusPath = value;
                    break;
                case "--settings":
                    settingsPath = value;
                    break;
                case "--output":
                    outputPath = value;
                    break;
            }
        }

        corpusPath ??= Path.GetFullPath(
            Path.Combine(repositoryRoot, "..", "lyrion-voice-evaluation", "corpus.json"));
        settingsPath ??= Path.Combine(
            repositoryRoot,
            ".data",
            "dev",
            "appsettings.local.json");
        outputPath ??= Path.Combine(
            repositoryRoot,
            ".data",
            "evaluation",
            $"lms-pass-through-{now:yyyyMMdd-HHmmss}.json");

        return new EvaluationArgumentsParsed(corpusPath, settingsPath, outputPath);
    }
}
