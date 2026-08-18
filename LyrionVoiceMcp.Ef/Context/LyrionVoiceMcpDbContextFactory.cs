using LyrionVoiceMcp.Ef.Abstractions.DataAccess;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LyrionVoiceMcp.Ef.Context;

public sealed class LyrionVoiceMcpDbContextFactory(
    ApplicationDatabaseSettings settings) : IDbContextFactory
{
    private readonly string connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = settings.DatabasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Shared,
        ForeignKeys = true,
        DefaultTimeout = 5
    }.ToString();

    public TDbContext CreateDbContext<TDbContext>() where TDbContext : DbContext
    {
        if (typeof(TDbContext) != typeof(LyrionVoiceMcpDbContext))
        {
            throw new InvalidOperationException(
                $"Unsupported DbContext type: {typeof(TDbContext).Name}");
        }

        var migrationsAssembly = typeof(LyrionVoiceMcpDbContext).Assembly.GetName().Name;
        var options = new DbContextOptionsBuilder<LyrionVoiceMcpDbContext>()
            .UseSqlite(
                connectionString,
                sqliteOptions => sqliteOptions.MigrationsAssembly(migrationsAssembly))
            .Options;

        return (TDbContext)(DbContext)new LyrionVoiceMcpDbContext(options);
    }
}
