using LyrionVoiceMcp.Api.Configuration;
using LyrionVoiceMcp.Api.Diagnostics;
using LyrionVoiceMcp.Api.Endpoints;
using LyrionVoiceMcp.Api.Tools;
using LyrionVoiceMcp.Api;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Ef;
using LyrionVoiceMcp.Lms;
using LyrionVoiceMcp.Search;
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
var applicationDatabaseSettings = ApplicationDatabaseSettings.FromValues(
    builder.Environment.ContentRootPath,
    builder.Configuration["LyrionVoiceMcpPersistence:DatabasePath"]);
var observationSettings = SearchObservationSettings.FromValue(
    builder.Configuration["LyrionVoiceMcpObservations:RetentionDays"]);
var operationalSettings = OperationalSettings.FromValues(
    builder.Configuration["LyrionVoiceMcpOperations:JobRetentionDays"],
    builder.Configuration["LyrionVoiceMcpOperations:ErrorRetentionDays"],
    builder.Configuration["LyrionVoiceMcpOperations:ToolCallRetentionDays"],
    builder.Configuration["LyrionVoiceMcpOperations:ToolCallJsonMaximumCharacters"],
    builder.Configuration["LyrionVoiceMcpOperations:TimeZoneId"]);
var operationalSchedules = OperationalSettings.CreateSchedulePolicy(
    ReadBoolean(builder.Configuration["LyrionVoiceMcpOperations:Schedules:CatalogueRefresh:Enabled"]),
    builder.Configuration["LyrionVoiceMcpOperations:Schedules:CatalogueRefresh:Cron"],
    ReadBoolean(builder.Configuration["LyrionVoiceMcpOperations:Schedules:ErrorLogPurge:Enabled"], true),
    builder.Configuration["LyrionVoiceMcpOperations:Schedules:ErrorLogPurge:Cron"],
    ReadBoolean(builder.Configuration["LyrionVoiceMcpOperations:Schedules:JobHistoryPurge:Enabled"], true),
    builder.Configuration["LyrionVoiceMcpOperations:Schedules:JobHistoryPurge:Cron"],
    ReadBoolean(builder.Configuration["LyrionVoiceMcpOperations:Schedules:ToolCallHistoryPurge:Enabled"], true),
    builder.Configuration["LyrionVoiceMcpOperations:Schedules:ToolCallHistoryPurge:Cron"]);
var searchSettings = ProductionSearchSettings.FromValues(
    builder.Environment.ContentRootPath,
    builder.Configuration["LyrionVoiceMcpSearch:IndexDirectoryPath"]);

builder.Services.AddSingleton(buildInfo);
builder.Services.AddSingleton(new SearchObservationRetentionPolicy(
    observationSettings.RetentionDays));
builder.Services.AddLyrionVoiceMcpEf(applicationDatabaseSettings);
builder.Services.AddSingleton(operationalSettings.ToPolicy());
builder.Services.AddSingleton(operationalSchedules);
builder.Services.AddLyrionVoiceMcpLms(lmsSettings, buildInfo.Version);
builder.Services.AddLyrionVoiceMcpProductionSearch(searchSettings);
builder.Services.AddSingleton<ProductionSearchDiagnosticService>();
builder.Services.AddLyrionVoiceMcpServices();
builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new Implementation
        {
            Name = "Lyrion Voice MCP",
            Version = buildInfo.Version
        };
        options.ServerInstructions = """
            Discover players with get_player_status, then use a returned raw player ID or exact unique player name; never invent either. Use search for requests expressible by a media name, one exact genre, an inclusive year range, a rating, or a combination; name may be omitted for filtered track discovery, and every input may be omitted for broad varied discovery. Use browse to navigate known library locations and for deterministic rating navigation through the Ratings tree. When search returns exactArtistMatch, treat that artist as the resolved interpretation; artists is empty, the albums group is a varied discography preview only for an unconstrained named search, and discographyBrowseRef browses every album credited to that album artist. Otherwise artists and albums contain ordinary search candidates. Genre, year, and rating constraints apply to tracks and constrained searches do not return albums or playlists. Pass browse references to browse to navigate the library tree, and pass playRef values to play or manage_queue. Treat all references as opaque. Artists are navigation only; albums and playlists can be browsed or deliberately played, and tracks can be played. Rating and ratingMatch must be supplied together. Never put a rating in name. exact means exactly that rating, while at_least includes that rating and higher ratings, so 4 with at_least means 4+. The play tool replaces the queue, powers the player on when required, and starts playback; do not append the same items immediately before calling play. The manage_queue append and insert_next actions change the queue without changing power or playback state; its clear action empties the queue and stops playback. Multi-item play and queue additions skip individual unusable items, so inspect completedItemCount and skippedItems before describing the result. Ask the user when multiple players or media candidates are genuinely ambiguous.
            """;
    })
    .WithRequestFilters(filters => filters.AddCallToolFilter(McpToolCallFilter.Create()))
    .WithHttpTransport()
    .WithTools<BrowseTools>(McpToolJson.Options)
    .WithTools<PlayerTools>(McpToolJson.Options)
    .WithTools<QueueTools>(McpToolJson.Options)
    .WithTools<PlaybackTools>(McpToolJson.Options)
    .WithTools([SearchToolRegistration.Create()])
    .WithTools([PlayerControlToolRegistration.Create()])
    .WithTools([QueueManagementToolRegistration.Create()]);

var app = builder.Build();

await app.Services.InitialiseLyrionVoiceMcpEfAsync(CancellationToken.None);
await app.Services.GetRequiredService<ISearchObservationStore>()
    .InitialiseAsync(CancellationToken.None);
await app.Services.GetRequiredService<ICatalogueLifecycleService>()
    .RecoverInterruptedRefreshAsync(CancellationToken.None);
await app.Services.GetRequiredService<IToolCallHistoryService>()
    .MarkRunningInterruptedAsync(CancellationToken.None);

app.Logger.LogWarning(
    "Lyrion Voice MCP is unauthenticated trusted-LAN software. Do not expose this service to untrusted networks.");

app.UseMiddleware<LyrionVoiceMcp.Api.ApiExceptionLoggingMiddleware>();
app.MapOperationalEndpoints();
app.MapOperationalHistoryEndpoints();
app.MapCatalogueEndpoints();
app.MapEvaluationEndpoints();
app.MapSearchIndexEndpoints();
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

static bool ReadBoolean(string? value, bool defaultValue = false) =>
    string.IsNullOrWhiteSpace(value)
        ? defaultValue
        : bool.TryParse(value, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"The configured boolean value '{value}' is invalid.");

public partial class Program;
