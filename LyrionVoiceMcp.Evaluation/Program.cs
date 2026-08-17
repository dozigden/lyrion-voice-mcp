using System.Text.Json;
using LyrionVoiceMcp.Evaluation;
using LyrionVoiceMcp.Lms;

var repositoryRoot = RepositoryRoot.Find(Environment.CurrentDirectory);
if (repositoryRoot is null)
{
    Console.Error.WriteLine(
        "Could not find LyrionVoiceMcp.slnx above the current working directory.");
    return 2;
}

var arguments = EvaluationCommandOptions.Parse(
    args,
    repositoryRoot,
    TimeProvider.System.GetUtcNow());
if (arguments is EvaluationHelpRequested)
{
    PrintHelp();
    return 0;
}

if (arguments is EvaluationArgumentsRejected rejectedArguments)
{
    Console.Error.WriteLine(rejectedArguments.Error);
    PrintHelp();
    return 2;
}

var options = (EvaluationArgumentsParsed)arguments;
var corpusOutcome = await new EvaluationCorpusReader().ReadFileAsync(
    options.CorpusPath,
    CancellationToken.None);
if (corpusOutcome is CorpusRejected rejectedCorpus)
{
    foreach (var error in rejectedCorpus.Errors)
    {
        Console.Error.WriteLine(error);
    }

    return 2;
}

var configuration = EvaluationConfiguration.LoadFromEnvironment();
if (configuration is EvaluationConfigurationRejected rejectedConfiguration)
{
    Console.Error.WriteLine(rejectedConfiguration.Error);
    return 2;
}

var settings = ((EvaluationConfigurationLoaded)configuration).Settings;
using var httpClient = new HttpClient { Timeout = settings.RequestTimeout };
httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("LyrionVoiceMcp.Evaluation/0.1.0");
var searchClient = new LmsSearchClient(new LmsJsonRpcClient(settings, httpClient));
var resolver = new LmsEvaluationSearchResolver(searchClient);
var runner = new EvaluationRunner(resolver, TimeProvider.System);
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

try
{
    var loadedCorpus = (CorpusRead)corpusOutcome;
    Console.WriteLine(
        $"Running {loadedCorpus.Corpus.Cases.Count} cases against the LMS baseline...");
    var report = await runner.RunAsync(
        loadedCorpus.Corpus,
        loadedCorpus.ContentHash,
        cancellation.Token);
    Directory.CreateDirectory(Path.GetDirectoryName(options.OutputPath)!);
    await using var output = File.Create(options.OutputPath);
    await JsonSerializer.SerializeAsync(
        output,
        report,
        new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true },
        cancellation.Token);
    await output.FlushAsync(cancellation.Token);
    Console.WriteLine(
        $"Passed {report.Summary.PassedCases}/{report.Summary.TotalCases}; "
        + $"top-1 {report.Summary.Top1Matches}/{report.Summary.PositiveCases}; "
        + $"top-5 {report.Summary.Top5Matches}/{report.Summary.PositiveCases}; "
        + $"errors {report.Summary.ErrorCases}.");
    Console.WriteLine($"Report: {options.OutputPath}");
    return report.Summary.ErrorCases == 0 ? 0 : 1;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Evaluation cancelled.");
    return 130;
}

static void PrintHelp()
{
    Console.WriteLine(
        "Usage: evaluate.sh [--resolver lms-pass-through] [--corpus PATH] [--output PATH]");
    Console.WriteLine();
    Console.WriteLine(
        "The local runner retains the LMS baseline. Evaluate the production resolver through "
        + "the deployed /api/evaluation/search endpoint.");
}
