using System.Data;
using System.Runtime.CompilerServices;
using LyrionVoiceMcp.Ef.Abstractions.DataAccess;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LyrionVoiceMcp.Ef.Scope;

internal class DbContextScope : IDbContextScopeBase
{
    private static readonly ConditionalWeakTable<InstanceIdentifier, DbContextScope>
        ScopeInstances = new();
    private static readonly AsyncLocal<InstanceIdentifier?> AmbientIdentifier = new();

    private readonly InstanceIdentifier instanceIdentifier = new();
    private readonly bool readOnly;
    private readonly bool nested;
    private readonly bool hasScopedTransaction;
    private readonly DbContextScope? parentScope;
    private readonly IDbContextCollection dbContexts;
    private bool disposed;
    private bool completed;

    public IDbContextCollection DbContexts => dbContexts;

    public DbContextScope(
        DbContextScopeOption joiningOption,
        bool readOnly,
        IsolationLevel? isolationLevel,
        IDbContextFactory dbContextFactory)
    {
        if (isolationLevel.HasValue && joiningOption == DbContextScopeOption.JoinExisting)
        {
            throw new ArgumentException(
                "An explicit transaction cannot join an ambient context scope.",
                nameof(joiningOption));
        }

        this.readOnly = readOnly;
        hasScopedTransaction = isolationLevel.HasValue;
        parentScope = GetAmbientScope();
        if (parentScope is not null && joiningOption == DbContextScopeOption.JoinExisting)
        {
            if (parentScope.readOnly && !readOnly)
            {
                throw new InvalidOperationException(
                    "A read/write scope cannot be nested inside a read-only scope.");
            }

            nested = true;
            dbContexts = parentScope.dbContexts;
        }
        else
        {
            dbContexts = new DbContextCollection(
                readOnly,
                isolationLevel,
                dbContextFactory);
        }

        SetAmbientScope(this);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (completed)
        {
            throw new InvalidOperationException(
                "SaveChangesAsync can only be called once per context scope.");
        }

        return SaveInternalAsync(cancellationToken);
    }

    public async Task TransactionAsync(
        Func<IDbContextTransactionScope, IDbContextTransaction, Task> executor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executor);
        ObjectDisposedException.ThrowIf(disposed, this);
        if (completed)
        {
            throw new InvalidOperationException(
                "TransactionAsync can only be called once per context scope.");
        }

        if (readOnly)
        {
            throw new InvalidOperationException(
                "An explicit write transaction cannot run in a read-only context scope.");
        }

        if (nested)
        {
            throw new InvalidOperationException(
                "An explicit write transaction cannot run in a joined context scope. Create an independent scope instead.");
        }

        if (hasScopedTransaction)
        {
            throw new InvalidOperationException(
                "This context scope already owns a transaction. Use SaveChangesAsync to complete it.");
        }

        var context = dbContexts.Get<LyrionVoiceMcpDbContext>();
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(
                cancellationToken);
            var transactionScope = new DbContextTransactionScope(context);
            await using var transactionWrapper = new DbContextTransaction(transaction);
            await executor(transactionScope, transactionWrapper);
        });
        completed = true;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        if (GetAmbientScope() != this)
        {
            throw new InvalidOperationException(
                "Context scopes must be disposed in creation order.");
        }

        try
        {
            if (!nested)
            {
                if (!completed)
                {
                    try
                    {
                        if (readOnly)
                        {
                            CommitInternal();
                        }
                        else
                        {
                            dbContexts.Rollback();
                        }
                    }
                    catch (Exception exception)
                    {
                        System.Diagnostics.Debug.WriteLine(exception);
                    }

                    completed = true;
                }

                dbContexts.Dispose();
            }
        }
        finally
        {
            RemoveAmbientScope();
            if (parentScope is not null)
            {
                if (parentScope.disposed)
                {
                    System.Diagnostics.Debug.WriteLine(
                        "The parent context scope was disposed before its child scope.");
                }
                else
                {
                    SetAmbientScope(parentScope);
                }
            }

            disposed = true;
        }
    }

    internal static DbContextScope? GetAmbientScope()
    {
        var identifier = AmbientIdentifier.Value;
        if (identifier is null)
        {
            return null;
        }

        if (ScopeInstances.TryGetValue(identifier, out var scope))
        {
            return scope;
        }

        System.Diagnostics.Debug.WriteLine(
            "Found an ambient context identifier without a corresponding scope.");
        return null;
    }

    internal static void SetAmbientScope(DbContextScope scope)
    {
        if (AmbientIdentifier.Value == scope.instanceIdentifier)
        {
            return;
        }

        AmbientIdentifier.Value = scope.instanceIdentifier;
        ScopeInstances.GetValue(scope.instanceIdentifier, _ => scope);
    }

    internal static void RemoveAmbientScope()
    {
        var current = AmbientIdentifier.Value;
        AmbientIdentifier.Value = null;
        if (current is not null)
        {
            ScopeInstances.Remove(current);
        }
    }

    internal static void HideAmbientScope()
    {
        AmbientIdentifier.Value = null;
    }

    private async Task<int> SaveInternalAsync(CancellationToken cancellationToken)
    {
        var affectedRows = nested
            ? 0
            : await CommitInternalAsync(cancellationToken);
        completed = true;
        return affectedRows;
    }

    private int CommitInternal()
    {
        try
        {
            return dbContexts.Commit();
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConcurrencyException(exception.Message, exception);
        }
        catch (DbUpdateException exception) when (IsPersistenceConflict(exception))
        {
            throw new PersistenceConflictException(exception.Message, exception);
        }
    }

    private async Task<int> CommitInternalAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await dbContexts.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConcurrencyException(exception.Message, exception);
        }
        catch (DbUpdateException exception) when (IsPersistenceConflict(exception))
        {
            throw new PersistenceConflictException(exception.Message, exception);
        }
    }

    private static bool IsPersistenceConflict(DbUpdateException exception) =>
        exception.InnerException is SqliteException
        {
            SqliteExtendedErrorCode: 1555 or 2067
        };

    private sealed class InstanceIdentifier : MarshalByRefObject
    {
    }
}

internal sealed class DbContextWriteScope(
    DbContextScopeOption joiningOption,
    IsolationLevel? isolationLevel,
    IDbContextFactory dbContextFactory)
    : DbContextScope(joiningOption, false, isolationLevel, dbContextFactory),
        IDbContextScope;

internal sealed class DbContextTransactionScope(
    DbContext dbContext) : IDbContextTransactionScope
{
    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);

    public void ClearTrackedEntities()
    {
        dbContext.ChangeTracker.Clear();
    }
}

internal sealed class DbContextTransaction(
    Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
    : IDbContextTransaction
{
    public Task CommitAsync(CancellationToken cancellationToken = default) =>
        transaction.CommitAsync(cancellationToken);

    public void Dispose()
    {
        transaction.Dispose();
    }

    public ValueTask DisposeAsync() => transaction.DisposeAsync();
}
