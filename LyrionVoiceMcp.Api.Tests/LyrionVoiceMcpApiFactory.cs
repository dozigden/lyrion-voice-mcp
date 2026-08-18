using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace LyrionVoiceMcp.Api.Tests;

public sealed class LyrionVoiceMcpApiFactory : WebApplicationFactory<Program>
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"lyrion-voice-mcp-api-tests-{Guid.NewGuid():N}");

    public string ApplicationDatabasePath => Path.Combine(directory, "lyrion-voice-mcp.db");
    public string SearchIndexDirectoryPath => Path.Combine(directory, "search-index");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(directory);
        builder.UseSetting(
            "LyrionVoiceMcpPersistence:DatabasePath",
            ApplicationDatabasePath);
        builder.UseSetting(
            "LyrionVoiceMcpSearch:IndexDirectoryPath",
            SearchIndexDirectoryPath);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(directory))
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }
}
