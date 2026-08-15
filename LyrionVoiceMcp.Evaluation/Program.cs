using System.Text.Json;
using LyrionVoiceMcp.Evaluation;
using LyrionVoiceMcp.Lms;
using LyrionVoiceMcp.Persistence;

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

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

var loadedCorpus = (CorpusRead)corpusOutcome;
HttpClient? httpClient = null;
IEvaluationSearchResolver resolver;
if (options.Resolver == EvaluationResolverSelection.LmsPassThrough)
{
    var settings = LoadEvaluationLmsSettings();
    if (settings is null)
    {
        return 2;
    }

    httpClient = CreateHttpClient(settings);
    var searchClient = new LmsSearchClient(new LmsJsonRpcClient(settings, httpClient));
    resolver = new LmsEvaluationSearchResolver(searchClient);
}
else
{
    try
    {
        var cataloguePath = options.CataloguePath!;
        if (options.RefreshCatalogue || !File.Exists(cataloguePath))
        {
            var settings = LoadEvaluationLmsSettings();
            if (settings is null)
            {
                return 2;
            }

            httpClient = CreateHttpClient(settings);
            var jsonRpcClient = new LmsJsonRpcClient(settings, httpClient);
            var store = new SqliteMediaCatalogueStore(
                new CatalogueSettings(cataloguePath),
                TimeProvider.System);
            var sourceReader = new LmsCatalogueReader(
                jsonRpcClient,
                settings,
                TimeProvider.System);
            var refresher = new EvaluationCatalogueRefresher(
                store,
                sourceReader,
                TimeProvider.System);

            Console.WriteLine($"Refreshing the local evaluation catalogue at {cataloguePath}...");
            var summary = await refresher.RefreshAsync(cancellation.Token);
            Console.WriteLine(
                $"Catalogue refreshed: {summary.ArtistCount} artists, "
                + $"{summary.AlbumCount} albums, {summary.TrackCount} tracks.");
        }

        resolver = await CatalogueLexicalSearchResolver.CreateAsync(
            cataloguePath,
            cancellation.Token);
    }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
    {
        httpClient?.Dispose();
        Console.Error.WriteLine("Evaluation catalogue refresh cancelled.");
        return 130;
    }
    catch (Exception exception) when (exception is not OperationCanceledException)
    {
        httpClient?.Dispose();
        Console.Error.WriteLine($"Could not prepare the catalogue: {exception.Message}");
        return 2;
    }
}

using var httpClientLifetime = httpClient;
var runner = new EvaluationRunner(resolver, TimeProvider.System);

try
{
    Console.WriteLine(
        $"Running {loadedCorpus.Corpus.Cases.Count} cases against {resolver.Name} {resolver.Version}...");
    if (resolver.Metrics.IndexedCandidateCount is { } candidateCount)
    {
        Console.WriteLine(
            $"Prepared {candidateCount} candidates in "
            + $"{resolver.Metrics.PreparationDurationMilliseconds} ms.");
    }

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
        "Usage: evaluate.sh [--resolver lms-pass-through|catalogue-lexical] "
        + "[--refresh-catalogue] [--catalogue PATH] [--corpus PATH] [--output PATH]");
    Console.WriteLine();
    Console.WriteLine("Resolver requirements:");
    Console.WriteLine("  lms-pass-through   LVM_EVALUATION_LMS_BASE_URL must identify the live LMS origin");
    Console.WriteLine(
        "  catalogue-lexical  reads .data/evaluation/catalogue.db; builds it from the live "
        + "LMS when missing");
    Console.WriteLine(
        "                     --refresh-catalogue refreshes that local snapshot before use");
    Console.WriteLine();
    Console.WriteLine("Defaults:");
    Console.WriteLine("  resolver   lms-pass-through");
    Console.WriteLine("  corpus     ../lyrion-voice-evaluation/corpus.json");
    Console.WriteLine("  output     .data/evaluation/<resolver>-<timestamp>.json");
}

static LmsConnectionSettings? LoadEvaluationLmsSettings()
{
    var configurationOutcome = EvaluationConfiguration.LoadFromEnvironment();
    if (configurationOutcome is EvaluationConfigurationRejected rejectedConfiguration)
    {
        Console.Error.WriteLine(rejectedConfiguration.Error);
        return null;
    }

    return ((EvaluationConfigurationLoaded)configurationOutcome).Settings;
}

static HttpClient CreateHttpClient(LmsConnectionSettings settings)
{
    var client = new HttpClient { Timeout = settings.RequestTimeout };
    client.DefaultRequestHeaders.UserAgent.ParseAdd("LyrionVoiceMcp.Evaluation/0.1.0");
    return client;
}
