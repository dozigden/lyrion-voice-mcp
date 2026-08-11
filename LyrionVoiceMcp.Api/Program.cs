using LyrionVoiceMcp.Api.Configuration;
using LyrionVoiceMcp.Api.Endpoints;
using LyrionVoiceMcp.Abstractions;
using LyrionVoiceMcp.Lms;
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

builder.Services.AddSingleton(buildInfo);
builder.Services.AddSingleton(lmsSettings);
builder.Services.AddHttpClient<ILmsConnectionProbe, LmsConnectionProbe>(client =>
{
    client.Timeout = lmsSettings.RequestTimeout;
    client.DefaultRequestHeaders.UserAgent.ParseAdd($"LyrionVoiceMcp/{buildInfo.Version}");
});
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
    .WithListToolsHandler((_, cancellationToken) =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new ListToolsResult
        {
            Tools = []
        });
    });

var app = builder.Build();

app.Logger.LogWarning(
    "Lyrion Voice MCP is unauthenticated trusted-LAN software. Do not expose this service to untrusted networks.");

app.MapOperationalEndpoints();
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
