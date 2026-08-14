using LyrionVoiceMcp.Api.Configuration;
using LyrionVoiceMcp.Api.Endpoints;
using LyrionVoiceMcp.Api.Tools;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Lms;
using LyrionVoiceMcp.Persistence;
using LyrionVoiceMcp.Services;
using ModelContextProtocol.Protocol;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment()
    && string.Equals(
        builder.Configuration["LyrionVoiceMcpDevelopment:LoadLocalSettings"],
        "true",
        StringComparison.OrdinalIgnoreCase))
{
    var localSettingsPath = Path.GetFullPath(Path.Combine(
        builder.Environment.ContentRootPath,
        "..",
        ".data",
        "dev",
        "appsettings.local.json"));
    builder.Configuration.AddJsonFile(localSettingsPath, optional: true, reloadOnChange: false);
    builder.Configuration.AddEnvironmentVariables();
}

var buildInfo = LyrionVoiceMcpBuildInfo.FromConfiguration(
    builder.Configuration,
    builder.Environment,
    typeof(Program).Assembly);
var lmsSettings = LmsConnectionSettings.FromValues(
    builder.Configuration["LyrionVoiceMcpLms:ServerId"],
    builder.Configuration["LyrionVoiceMcpLms:BaseUrl"],
    builder.Configuration["LyrionVoiceMcpLms:RequestTimeoutSeconds"]);
var observationSettings = SearchObservationSettings.FromValues(
    builder.Environment.ContentRootPath,
    builder.Configuration["LyrionVoiceMcpObservations:DatabasePath"],
    builder.Configuration["LyrionVoiceMcpObservations:RetentionDays"]);

builder.Services.AddSingleton(buildInfo);
builder.Services.AddSingleton(lmsSettings);
builder.Services.AddSearchObservationPersistence(observationSettings);
builder.Services.AddHttpClient<LmsJsonRpcClient>(client =>
{
    client.Timeout = lmsSettings.RequestTimeout;
    client.DefaultRequestHeaders.UserAgent.ParseAdd($"LyrionVoiceMcp/{buildInfo.Version}");
});
builder.Services.AddTransient<ILmsConnectionProbe, LmsConnectionProbe>();
builder.Services.AddTransient<ILmsPlaybackClient, LmsPlaybackClient>();
builder.Services.AddTransient<ILmsPlayerControlClient, LmsPlayerControlClient>();
builder.Services.AddTransient<ILmsPlayerClient, LmsPlayerClient>();
builder.Services.AddTransient<ILmsQueueClient, LmsQueueClient>();
builder.Services.AddTransient<ILmsSearchClient, LmsSearchClient>();
builder.Services.AddLyrionVoiceMcpServices();
builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new Implementation
        {
            Name = "Lyrion Voice MCP",
            Version = buildInfo.Version
        };
    })
    .WithHttpTransport()
    .WithTools<SearchTools>()
    .WithTools<PlayerTools>()
    .WithTools<QueueTools>()
    .WithTools([PlayerControlToolRegistration.Create()])
    .WithTools([PlaybackToolRegistration.Create()]);

var app = builder.Build();

await app.Services.GetRequiredService<ISearchObservationStore>()
    .InitialiseAsync(CancellationToken.None);

app.Logger.LogWarning(
    "Lyrion Voice MCP is unauthenticated trusted-LAN software. Do not expose this service to untrusted networks.");

app.MapOperationalEndpoints();
app.MapSearchObservationEndpoints();
app.MapMcp("/mcp");

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        if (string.Equals(context.File.Name, "index.html", StringComparison.OrdinalIgnoreCase))
        {
            ApplySpaShellCacheHeaders(context.Context.Response);
        }
    }
});

app.MapFallback(async context =>
{
    if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
        || context.Request.Path.StartsWithSegments("/mcp", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    var webRootPath = app.Environment.WebRootPath;
    var indexPath = string.IsNullOrWhiteSpace(webRootPath)
        ? null
        : Path.Combine(webRootPath, "index.html");

    if (indexPath is null || !File.Exists(indexPath))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    ApplySpaShellCacheHeaders(context.Response);
    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.SendFileAsync(indexPath);
});

app.Run();

static void ApplySpaShellCacheHeaders(HttpResponse response)
{
    response.Headers.CacheControl = "no-cache, must-revalidate";
    response.Headers.Pragma = "no-cache";
}

public partial class Program;
