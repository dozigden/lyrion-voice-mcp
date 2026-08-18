using System.Data.Common;
using LyrionVoiceMcp.Ef.Abstractions.DataAccess;
using LyrionVoiceMcp.Ef.Abstractions.Catalogue;
using LyrionVoiceMcp.Ef.Context;
using LyrionVoiceMcp.Ef.Scope;
using LyrionVoiceMcp.Ef.Repositories;
using LyrionVoiceMcp.Ef.Abstractions.SearchObservations;
using LyrionVoiceMcp.Ef.Abstractions.ErrorLogs;
using LyrionVoiceMcp.Ef.Abstractions.Jobs;
using LyrionVoiceMcp.Ef.Abstractions.ToolCalls;
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
        services.AddTransient<ISearchObservationRepository, SearchObservationRepository>();
        services.AddTransient<IJobRepository, JobRepository>();
        services.AddTransient<IJobLogRepository, JobLogRepository>();
        services.AddTransient<IScheduledJobStateRepository, ScheduledJobStateRepository>();
        services.AddTransient<IErrorLogRepository, ErrorLogRepository>();
        services.AddTransient<IToolCallRepository, ToolCallRepository>();
        services.AddTransient<ICatalogueStateRepository, CatalogueStateRepository>();
        services.AddTransient<ICatalogueArtistRepository, CatalogueArtistRepository>();
        services.AddTransient<ICatalogueAlbumRepository, CatalogueAlbumRepository>();
        services.AddTransient<ICatalogueGenreRepository, CatalogueGenreRepository>();
        services.AddTransient<ICatalogueTrackRepository, CatalogueTrackRepository>();
        services.AddTransient<ICatalogueVirtualLibraryRepository,
            CatalogueVirtualLibraryRepository>();
        services.AddTransient<ICatalogueValidationRepository,
            CatalogueValidationRepository>();
        services.AddTransient<ICatalogueProjectionRepository, CatalogueProjectionRepository>();
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
