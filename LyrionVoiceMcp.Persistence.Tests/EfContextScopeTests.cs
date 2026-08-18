using System.Data;
using LyrionVoiceMcp.Ef;
using LyrionVoiceMcp.Ef.Abstractions.DataAccess;
using LyrionVoiceMcp.Ef.Context;
using LyrionVoiceMcp.Ef.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LyrionVoiceMcp.Persistence.Tests;

public sealed class EfContextScopeTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"lyrion-ef-{Guid.NewGuid():N}");
    private readonly ServiceProvider serviceProvider;
    private readonly IDbContextFactory dbContextFactory;
    private readonly IDbContextScopeFactory scopeFactory;
    private readonly IAmbientDbContextLocator ambientDbContextLocator;

    public EfContextScopeTests()
    {
        var services = new ServiceCollection();
        services.AddLyrionVoiceMcpEf(new ApplicationDatabaseSettings(
            Path.Combine(directory, "application.db")));
        serviceProvider = services.BuildServiceProvider();
        serviceProvider.InitialiseLyrionVoiceMcpEfAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        dbContextFactory = serviceProvider.GetRequiredService<IDbContextFactory>();
        scopeFactory = serviceProvider.GetRequiredService<IDbContextScopeFactory>();
        ambientDbContextLocator = serviceProvider.GetRequiredService<IAmbientDbContextLocator>();
    }

    [Fact]
    public async Task InitialisationShouldApplyBaselineMigrationAndSqlitePragmasIdempotently()
    {
        await serviceProvider.InitialiseLyrionVoiceMcpEfAsync(
            TestContext.Current.CancellationToken);

        await using var context = dbContextFactory.CreateDbContext<LyrionVoiceMcpDbContext>();
        await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
        try
        {
            Assert.Equal(1L, await ExecuteScalarAsync(context, "PRAGMA foreign_keys;"));
            await using var timeoutProbe = context.Database.GetDbConnection().CreateCommand();
            Assert.Equal(5, timeoutProbe.CommandTimeout);
            Assert.Equal("wal", await ExecuteTextScalarAsync(context, "PRAGMA journal_mode;"));
            Assert.Equal(
                1L,
                await ExecuteScalarAsync(
                    context,
                    "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId LIKE '%InitialApplicationDatabase';"));
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    [Fact]
    public async Task NestedScopesShouldJoinUnlessANewScopeIsForced()
    {
        using var outerScope = scopeFactory.Create();
        var outerContext = outerScope.DbContexts.Get<LyrionVoiceMcpDbContext>();

        using (var joinedScope = scopeFactory.Create())
        {
            Assert.Same(outerContext, joinedScope.DbContexts.Get<LyrionVoiceMcpDbContext>());
            Assert.Same(outerContext, ambientDbContextLocator.Get<LyrionVoiceMcpDbContext>());
            Assert.Equal(0, await joinedScope.SaveChangesAsync(TestContext.Current.CancellationToken));
        }

        Assert.Same(outerContext, ambientDbContextLocator.Get<LyrionVoiceMcpDbContext>());

        using (var forcedScope = scopeFactory.Create(DbContextScopeOption.ForceCreateNew))
        {
            Assert.NotSame(outerContext, forcedScope.DbContexts.Get<LyrionVoiceMcpDbContext>());
        }

        Assert.Same(outerContext, ambientDbContextLocator.Get<LyrionVoiceMcpDbContext>());
    }

    [Fact]
    public void ReadWriteScopeShouldNotJoinReadOnlyScope()
    {
        using var readOnlyScope = scopeFactory.CreateReadOnly();

        var exception = Assert.Throws<InvalidOperationException>(() => scopeFactory.Create());

        Assert.Contains("read/write scope", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadOnlyScopeShouldNotExposeWriteOperations()
    {
        using var readOnlyScope = scopeFactory.CreateReadOnly();

        Assert.IsNotAssignableFrom<IDbContextScope>(readOnlyScope);
        Assert.DoesNotContain(
            typeof(IDbContextReadOnlyScope).GetMethods(),
            method => method.Name is nameof(IDbContextScope.SaveChangesAsync)
                or nameof(IDbContextScope.TransactionAsync));
    }

    [Fact]
    public async Task ExplicitTransactionShouldRejectAJoinedScope()
    {
        using var outerScope = scopeFactory.Create();
        using var joinedScope = scopeFactory.Create();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            joinedScope.TransactionAsync(
                (_, _) => Task.CompletedTask,
                TestContext.Current.CancellationToken));

        Assert.Contains("joined context scope", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExplicitTransactionShouldRejectAScopeThatAlreadyOwnsATransaction()
    {
        using var transactionalScope = scopeFactory.CreateWithTransaction(
            IsolationLevel.Serializable);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            transactionalScope.TransactionAsync(
                (_, _) => Task.CompletedTask,
                TestContext.Current.CancellationToken));

        Assert.Contains("already owns a transaction", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AmbientSuppressionShouldHideAndThenRestoreParentScope()
    {
        using var outerScope = scopeFactory.Create();
        var outerContext = outerScope.DbContexts.Get<LyrionVoiceMcpDbContext>();

        using (scopeFactory.SuppressAmbientContext())
        {
            Assert.Null(ambientDbContextLocator.Get<LyrionVoiceMcpDbContext>());
            using var isolatedScope = scopeFactory.Create();
            Assert.NotSame(
                outerContext,
                isolatedScope.DbContexts.Get<LyrionVoiceMcpDbContext>());
        }

        Assert.Same(outerContext, ambientDbContextLocator.Get<LyrionVoiceMcpDbContext>());
    }

    [Fact]
    public void IncorrectDisposalOrderShouldNotDestroyTheSharedContext()
    {
        var outerScope = scopeFactory.Create();
        var outerContext = outerScope.DbContexts.Get<LyrionVoiceMcpDbContext>();
        var joinedScope = scopeFactory.Create();
        try
        {
            var exception = Assert.Throws<InvalidOperationException>(outerScope.Dispose);

            Assert.Contains("creation order", exception.Message, StringComparison.Ordinal);
            Assert.Same(
                outerContext,
                joinedScope.DbContexts.Get<LyrionVoiceMcpDbContext>());
            outerContext.Database.OpenConnection();
            Assert.Equal(
                System.Data.ConnectionState.Open,
                outerContext.Database.GetDbConnection().State);
            outerContext.Database.CloseConnection();
        }
        finally
        {
            joinedScope.Dispose();
            outerScope.Dispose();
        }

        Assert.Null(ambientDbContextLocator.Get<LyrionVoiceMcpDbContext>());
    }

    [Fact]
    public async Task TransactionScopeShouldCommitOnlyWhenCompleted()
    {
        await CreateProbeTableAsync();

        using (var committedScope = scopeFactory.CreateWithTransaction(IsolationLevel.Serializable))
        {
            var context = committedScope.DbContexts.Get<LyrionVoiceMcpDbContext>();
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO scope_probe(value) VALUES ('committed');",
                TestContext.Current.CancellationToken);
            await committedScope.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using (var rolledBackScope = scopeFactory.CreateWithTransaction(IsolationLevel.Serializable))
        {
            var context = rolledBackScope.DbContexts.Get<LyrionVoiceMcpDbContext>();
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO scope_probe(value) VALUES ('rolled back');",
                TestContext.Current.CancellationToken);
        }

        Assert.Equal(1L, await CountProbeRowsAsync());
    }

    [Fact]
    public async Task ExplicitTransactionShouldExposeSaveAndCommitControl()
    {
        await CreateProbeTableAsync();
        using var scope = scopeFactory.Create();
        var context = scope.DbContexts.Get<LyrionVoiceMcpDbContext>();

        await scope.TransactionAsync(
            async (transactionScope, transaction) =>
            {
                await context.Database.ExecuteSqlRawAsync(
                    "INSERT INTO scope_probe(value) VALUES ('explicit');",
                    TestContext.Current.CancellationToken);
                await transactionScope.SaveChangesAsync(TestContext.Current.CancellationToken);
                await transaction.CommitAsync(TestContext.Current.CancellationToken);
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(1L, await CountProbeRowsAsync());
    }

    [Fact]
    public void RepositoryShouldRequireAnAmbientContextScope()
    {
        var repository = new ProbeRepository(ambientDbContextLocator);

        var exception = Assert.Throws<InvalidOperationException>(() => repository.Query());

        Assert.Contains("ambient DbContext", exception.Message, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        serviceProvider.Dispose();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    private async Task CreateProbeTableAsync()
    {
        await using var context = dbContextFactory.CreateDbContext<LyrionVoiceMcpDbContext>();
        await context.Database.ExecuteSqlRawAsync(
            "CREATE TABLE scope_probe (id INTEGER PRIMARY KEY, value TEXT NOT NULL);",
            TestContext.Current.CancellationToken);
    }

    private async Task<long> CountProbeRowsAsync()
    {
        await using var context = dbContextFactory.CreateDbContext<LyrionVoiceMcpDbContext>();
        await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
        try
        {
            return await ExecuteScalarAsync(context, "SELECT COUNT(*) FROM scope_probe;");
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    private static async Task<long> ExecuteScalarAsync(
        LyrionVoiceMcpDbContext context,
        string commandText)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = commandText;
        return Convert.ToInt64(await command.ExecuteScalarAsync(
            TestContext.Current.CancellationToken));
    }

    private static async Task<string> ExecuteTextScalarAsync(
        LyrionVoiceMcpDbContext context,
        string commandText)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = commandText;
        return Convert.ToString(await command.ExecuteScalarAsync(
            TestContext.Current.CancellationToken))!;
    }

    private sealed class ProbeEntity;

    private sealed class ProbeRepository(IAmbientDbContextLocator locator)
        : RepositoryBase<ProbeEntity>(locator);
}
