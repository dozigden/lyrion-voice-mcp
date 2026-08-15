namespace LyrionVoiceMcp.Evaluation;

public abstract record EvaluationArgumentsOutcome;

public sealed record EvaluationArgumentsParsed(
    string CorpusPath,
    string OutputPath,
    EvaluationResolverSelection Resolver,
    string? CataloguePath,
    bool RefreshCatalogue) : EvaluationArgumentsOutcome;

public enum EvaluationResolverSelection
{
    LmsPassThrough,
    CatalogueLexical
}

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
        string? outputPath = null;
        string? resolverName = null;
        string? cataloguePath = null;
        var refreshCatalogue = false;
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (argument is "--help" or "-h")
            {
                return new EvaluationHelpRequested();
            }

            if (argument == "--refresh-catalogue")
            {
                refreshCatalogue = true;
                continue;
            }

            if (argument is not ("--corpus" or "--output" or "--resolver" or "--catalogue"))
            {
                return new EvaluationArgumentsRejected($"Unknown argument: {argument}");
            }

            if (++index >= arguments.Count || string.IsNullOrWhiteSpace(arguments[index]))
            {
                return new EvaluationArgumentsRejected($"{argument} requires a path.");
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
                case "--resolver":
                    resolverName = value;
                    break;
                case "--catalogue":
                    cataloguePath = Path.GetFullPath(value, repositoryRoot);
                    break;
            }
        }

        var resolver = resolverName switch
        {
            null or "lms-pass-through" => EvaluationResolverSelection.LmsPassThrough,
            "catalogue-lexical" => EvaluationResolverSelection.CatalogueLexical,
            _ => (EvaluationResolverSelection?)null
        };
        if (resolver is null)
        {
            return new EvaluationArgumentsRejected(
                $"Unknown resolver: {resolverName}. Use lms-pass-through or catalogue-lexical.");
        }

        if (resolver == EvaluationResolverSelection.LmsPassThrough
            && (cataloguePath is not null || refreshCatalogue))
        {
            return new EvaluationArgumentsRejected(
                "--catalogue and --refresh-catalogue can only be used with "
                + "--resolver catalogue-lexical.");
        }

        corpusPath ??= Path.GetFullPath(
            Path.Combine(repositoryRoot, "..", "lyrion-voice-evaluation", "corpus.json"));
        if (resolver == EvaluationResolverSelection.CatalogueLexical)
        {
            cataloguePath ??= Path.Combine(
                repositoryRoot,
                ".data",
                "evaluation",
                "catalogue.db");
        }

        var resolverFileName = resolver == EvaluationResolverSelection.LmsPassThrough
            ? "lms-pass-through"
            : "catalogue-lexical";
        outputPath ??= Path.Combine(
            repositoryRoot,
            ".data",
            "evaluation",
            $"{resolverFileName}-{now:yyyyMMdd-HHmmss}.json");

        return new EvaluationArgumentsParsed(
            corpusPath,
            outputPath,
            resolver.Value,
            cataloguePath,
            refreshCatalogue);
    }
}
