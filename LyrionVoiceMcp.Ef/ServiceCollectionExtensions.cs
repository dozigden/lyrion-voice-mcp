using System.Data.Common;
using LyrionVoiceMcp.Ef.Abstractions.DataAccess;
using LyrionVoiceMcp.Ef.Context;
using LyrionVoiceMcp.Ef.Scope;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LyrionVoiceMcp.Ef;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLyrionVoiceMcpEf(
        this IServiceCollection services,
        ApplicationDatabaseSettings settings)
    {
        services.AddSingleton(settings);
        services.AddSingleton<IDbContextFactory, LyrionVoiceMcpDbContextFactory>();
        services.AddTransient<IDbContextScopeFactory, DbContextScopeFactory>();
        services.AddTransient<IAmbientDbContextLocator, AmbientDbContextLocator>();
        return services;
    }

    public static async Task InitialiseLyrionVoiceMcpEfAsync(
        this IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<ApplicationDatabaseSettings>();
        Directory.CreateDirectory(Path.GetDirectoryName(settings.DatabasePath)!);

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory>();
        await using var context = factory.CreateDbContext<LyrionVoiceMcpDbContext>();
        await context.Database.MigrateAsync(cancellationToken);
        await EnableWriteAheadLoggingAsync(context, cancellationToken);
    }

    private static async Task EnableWriteAheadLoggingAsync(
        LyrionVoiceMcpDbContext context,
        CancellationToken cancellationToken)
    {
        await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using DbCommand command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "PRAGMA journal_mode=WAL;";
            await command.ExecuteScalarAsync(cancellationToken);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }
}
