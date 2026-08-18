using LyrionVoiceMcp.Ef.Context;
using Microsoft.EntityFrameworkCore.Design;

namespace LyrionVoiceMcp.Ef;

public sealed class LyrionVoiceMcpDesignTimeDbContextFactory
    : IDesignTimeDbContextFactory<LyrionVoiceMcpDbContext>
{
    public LyrionVoiceMcpDbContext CreateDbContext(string[] args)
    {
        var databasePath = Environment.GetEnvironmentVariable(
            "LYRION_VOICE_MCP_DESIGNTIME_DATABASE_PATH");
        var settings = new ApplicationDatabaseSettings(
            string.IsNullOrWhiteSpace(databasePath)
                ? "lyrion-voice-mcp.designtime.db"
                : databasePath);
        return new LyrionVoiceMcpDbContextFactory(settings)
            .CreateDbContext<LyrionVoiceMcpDbContext>();
    }
}
