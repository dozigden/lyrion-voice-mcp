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

var now = TimeProvider.System.GetUtcNow();
var arguments = EvaluationCommandOptions.Parse(args, repositoryRoot, now);
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
var reader = new EvaluationCorpusReader();
var corpusOutcome = await reader.ReadFileAsync(options.CorpusPath, CancellationToken.None);
if (corpusOutcome is CorpusRejected rejectedCorpus)
{
    foreach (var error in rejectedCorpus.Errors)
    {
        Console.Error.WriteLine(error);
    }

    return 2;
}

var configurationOutcome = await EvaluationConfiguration.LoadAsync(
    options.SettingsPath,
    CancellationToken.None);
if (configurationOutcome is EvaluationConfigurationRejected rejectedConfiguration)
{
    Console.Error.WriteLine(rejectedConfiguration.Error);
    return 2;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

var loadedCorpus = (CorpusRead)corpusOutcome;
var settings = ((EvaluationConfigurationLoaded)configurationOutcome).Settings;
using var httpClient = new HttpClient
{
    Timeout = settings.RequestTimeout
};
httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("LyrionVoiceMcp.Evaluation/0.1.0");
var searchClient = new LmsSearchClient(new LmsJsonRpcClient(settings, httpClient));
var runner = new EvaluationRunner(searchClient, TimeProvider.System);

try
{
    Console.WriteLine(
        $"Running {loadedCorpus.Corpus.Cases.Count} cases against lms-pass-through 1...");
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
    Console.WriteLine("Usage: evaluate.sh [--corpus PATH] [--settings PATH] [--output PATH]");
    Console.WriteLine();
    Console.WriteLine("Defaults:");
    Console.WriteLine("  corpus   ../lyrion-voice-evaluation/corpus.json");
    Console.WriteLine("  settings .data/dev/appsettings.local.json");
    Console.WriteLine("  output   .data/evaluation/lms-pass-through-<timestamp>.json");
}
