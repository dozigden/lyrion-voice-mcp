using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LyrionVoiceMcp.Evaluation;

public abstract record EvaluationServerArgumentsOutcome;

public sealed record EvaluationServerArgumentsParsed(
    string CataloguePath,
    string IndexDirectoryPath,
    string Url) : EvaluationServerArgumentsOutcome;

public sealed record EvaluationServerHelpRequested : EvaluationServerArgumentsOutcome;

public sealed record EvaluationServerArgumentsRejected(
    string Error) : EvaluationServerArgumentsOutcome;

public static class EvaluationServerCommandOptions
{
    public static EvaluationServerArgumentsOutcome Parse(
        IReadOnlyList<string> arguments,
        string currentDirectory)
    {
        string? cataloguePath = null;
        string? indexDirectoryPath = null;
        var url = "http://127.0.0.1:5610";
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (argument is "--help" or "-h")
            {
                return new EvaluationServerHelpRequested();
            }

            if (argument is not ("--catalogue" or "--index-directory" or "--url"))
            {
                return new EvaluationServerArgumentsRejected($"Unknown serve argument: {argument}");
            }

            if (++index >= arguments.Count || string.IsNullOrWhiteSpace(arguments[index]))
            {
                return new EvaluationServerArgumentsRejected($"{argument} requires a value.");
            }

            var value = arguments[index];
            switch (argument)
            {
                case "--catalogue":
                    cataloguePath = Path.GetFullPath(value, currentDirectory);
                    break;
                case "--index-directory":
                    indexDirectoryPath = Path.GetFullPath(value, currentDirectory);
                    break;
                case "--url":
                    url = value;
                    break;
            }
        }

        cataloguePath ??= Path.Combine(currentDirectory, ".data", "evaluation", "catalogue.db");
        indexDirectoryPath ??= Path.Combine(
            Path.GetDirectoryName(cataloguePath) ?? currentDirectory,
            "search-indexes");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttp
            || string.IsNullOrEmpty(uri.Host)
            || uri.IsDefaultPort
            || uri.Port <= 0)
        {
            return new EvaluationServerArgumentsRejected(
                "--url must be an absolute HTTP URL with an explicit port.");
        }

        if (!File.Exists(cataloguePath))
        {
            return new EvaluationServerArgumentsRejected(
                $"Catalogue database does not exist: {cataloguePath}");
        }

        return new EvaluationServerArgumentsParsed(cataloguePath, indexDirectoryPath, url);
    }
}

public static class EvaluationHttpServer
{
    public static async Task RunAsync(
        EvaluationServerArgumentsParsed options,
        CancellationToken cancellationToken)
    {
        await using var application = BuildApplication(options);
        await application.RunAsync(cancellationToken);
    }

    public static WebApplication BuildApplication(EvaluationServerArgumentsParsed options)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls(options.Url);
        builder.Services.AddSingleton<IEvaluationDiagnosticResolverProvider>(
            _ => new EvaluationDiagnosticResolverHost(options));
        builder.Services.AddSingleton<EvaluationSearchExecutor>();
        builder.Services.ConfigureHttpJsonOptions(httpOptions =>
        {
            httpOptions.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            httpOptions.SerializerOptions.Converters.Add(
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        });
        var application = builder.Build();
        application.MapGet(
            "/api/evaluation",
            () => Results.Ok(new EvaluationServerDescription(
                1,
                ["catalogue-phuzzy-indexed", "catalogue-lucene"])));
        application.MapPost(
            "/api/evaluation/search",
            async (
                EvaluationHttpSearchRequest request,
                EvaluationSearchExecutor executor,
                CancellationToken requestCancellation) =>
            {
                var validationError = Validate(request);
                if (validationError is not null)
                {
                    return Results.BadRequest(new EvaluationHttpError(validationError));
                }

                try
                {
                    var response = await executor.ExecuteAsync(request, requestCancellation);
                    return Results.Ok(response);
                }
                catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    return Results.Problem(
                        title: "Evaluation search failed",
                        detail: exception.Message,
                        statusCode: StatusCodes.Status500InternalServerError);
                }
            });
        return application;
    }

    private static string? Validate(EvaluationHttpSearchRequest? request)
    {
        if (request is null)
        {
            return "A JSON request body is required.";
        }

        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return "query is required.";
        }

        if (request.Query.Length > 500)
        {
            return "query must contain no more than 500 characters.";
        }

        if (PhuzzyTextForms.Create(request.Query).Tokens.Count > 20)
        {
            return "query must contain no more than 20 words.";
        }

        return request.Resolver is "catalogue-phuzzy-indexed" or "catalogue-lucene"
            ? null
            : "resolver must be catalogue-phuzzy-indexed or catalogue-lucene.";
    }
}

public sealed record EvaluationHttpSearchRequest(string Resolver, string Query);

public sealed record EvaluationHttpSearchResponse(
    bool ResolverPreparedForThisRequest,
    EvaluationProcessMemory ProcessMemory,
    EvaluationDiagnosticSearchResponse Search);

public sealed record EvaluationProcessMemory(
    long WorkingSetBeforeResolverBytes,
    long WorkingSetAfterResolverBytes,
    long WorkingSetAfterSearchBytes,
    long ProcessPeakWorkingSetBytes);

public sealed record EvaluationServerDescription(
    int SchemaVersion,
    IReadOnlyList<string> Resolvers);

public sealed record EvaluationHttpError(string Error);

internal sealed class EvaluationDiagnosticResolverHost(
    EvaluationServerArgumentsParsed options) : IEvaluationDiagnosticResolverProvider, IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, IEvaluationDiagnosticSearchResolver> resolvers =
        new(StringComparer.Ordinal);

    public async Task<ResolvedDiagnosticResolver> GetAsync(
        string name,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (resolvers.TryGetValue(name, out var existing))
            {
                return new ResolvedDiagnosticResolver(existing, false);
            }

            Directory.CreateDirectory(options.IndexDirectoryPath);
            IEvaluationDiagnosticSearchResolver created = name switch
            {
                "catalogue-phuzzy-indexed" =>
                    await CataloguePhuzzyIndexedSearchResolver.CreateAsync(
                        options.CataloguePath,
                        Path.Combine(options.IndexDirectoryPath, "catalogue-phuzzy-index.db"),
                        cancellationToken),
                "catalogue-lucene" =>
                    await CatalogueLuceneSearchResolver.CreateAsync(
                        options.CataloguePath,
                        Path.Combine(options.IndexDirectoryPath, "catalogue-lucene-index"),
                        cancellationToken),
                _ => throw new InvalidOperationException(
                    $"Evaluation resolver '{name}' is not supported.")
            };
            resolvers.Add(name, created);
            return new ResolvedDiagnosticResolver(created, true);
        }
        finally
        {
            gate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        foreach (var resolver in resolvers.Values.OfType<IDisposable>())
        {
            resolver.Dispose();
        }

        gate.Dispose();
        return ValueTask.CompletedTask;
    }
}

internal interface IEvaluationDiagnosticResolverProvider
{
    Task<ResolvedDiagnosticResolver> GetAsync(
        string name,
        CancellationToken cancellationToken);
}

internal sealed class EvaluationSearchExecutor(
    IEvaluationDiagnosticResolverProvider resolverProvider) : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<EvaluationHttpSearchResponse> ExecuteAsync(
        EvaluationHttpSearchRequest request,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            var workingSetBefore = process.WorkingSet64;
            var resolved = await resolverProvider.GetAsync(
                request.Resolver,
                cancellationToken);
            process.Refresh();
            var workingSetAfterResolver = process.WorkingSet64;
            var search = await resolved.Resolver.SearchDetailedAsync(
                request.Query,
                cancellationToken);
            process.Refresh();
            return new EvaluationHttpSearchResponse(
                resolved.PreparedForThisRequest,
                new EvaluationProcessMemory(
                    workingSetBefore,
                    workingSetAfterResolver,
                    process.WorkingSet64,
                    process.PeakWorkingSet64),
                search);
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose() => gate.Dispose();
}

internal sealed record ResolvedDiagnosticResolver(
    IEvaluationDiagnosticSearchResolver Resolver,
    bool PreparedForThisRequest);
