using System.Data;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using LyrionVoiceMcp.Ef.Abstractions.DataAccess;
using Microsoft.EntityFrameworkCore;
using EfTransaction = Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction;

namespace LyrionVoiceMcp.Ef.Scope;

internal sealed class DbContextCollection(
    bool readOnly,
    IsolationLevel? isolationLevel,
    IDbContextFactory dbContextFactory) : IDbContextCollection
{
    private readonly Dictionary<Type, DbContext> initialisedDbContexts = [];
    private readonly Dictionary<DbContext, EfTransaction> transactions = [];
    private bool disposed;
    private bool completed;

    public TDbContext Get<TDbContext>() where TDbContext : DbContext
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        var requestedType = typeof(TDbContext);
        if (initialisedDbContexts.TryGetValue(requestedType, out var existingContext))
        {
            return (TDbContext)existingContext;
        }

        var context = dbContextFactory.CreateDbContext<TDbContext>();
        initialisedDbContexts.Add(requestedType, context);

        if (readOnly)
        {
            context.ChangeTracker.AutoDetectChangesEnabled = false;
        }

        if (isolationLevel.HasValue)
        {
            transactions.Add(
                context,
                context.Database.BeginTransaction(isolationLevel.Value));
        }

        return context;
    }

    public int Commit()
    {
        EnsureCanComplete();

        ExceptionDispatchInfo? lastError = null;
        var affectedRows = 0;
        foreach (var context in initialisedDbContexts.Values)
        {
            try
            {
                if (!readOnly)
                {
                    affectedRows += context.SaveChanges();
                }

                CommitTransactionIfPresent(context);
            }
            catch (Exception exception)
            {
                lastError = ExceptionDispatchInfo.Capture(exception);
            }
        }

        transactions.Clear();
        completed = true;
        lastError?.Throw();
        return affectedRows;
    }

    public async Task<int> CommitAsync(CancellationToken cancellationToken = default)
    {
        EnsureCanComplete();

        ExceptionDispatchInfo? lastError = null;
        var affectedRows = 0;
        foreach (var context in initialisedDbContexts.Values)
        {
            try
            {
                if (!readOnly)
                {
                    affectedRows += await SaveChangesAsync(context, cancellationToken);
                }

                await CommitTransactionIfPresentAsync(context, cancellationToken);
            }
            catch (Exception exception)
            {
                lastError = ExceptionDispatchInfo.Capture(exception);
            }
        }

        transactions.Clear();
        completed = true;
        lastError?.Throw();
        return affectedRows;
    }

    public void Rollback()
    {
        EnsureCanComplete();

        ExceptionDispatchInfo? lastError = null;
        foreach (var transaction in transactions.Values)
        {
            try
            {
                transaction.Rollback();
                transaction.Dispose();
            }
            catch (Exception exception)
            {
                lastError = ExceptionDispatchInfo.Capture(exception);
            }
        }

        transactions.Clear();
        completed = true;
        lastError?.Throw();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        if (!completed)
        {
            try
            {
                if (readOnly)
                {
                    Commit();
                }
                else
                {
                    Rollback();
                }
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine(exception);
            }
        }

        foreach (var context in initialisedDbContexts.Values)
        {
            context.Dispose();
        }

        initialisedDbContexts.Clear();
        disposed = true;
    }

    private static async Task<int> SaveChangesAsync(
        DbContext context,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                return await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException exception) when (attempt == 0)
            {
                var canRetry = false;
                foreach (var entry in exception.Entries)
                {
                    if (entry.State != EntityState.Deleted)
                    {
                        continue;
                    }

                    canRetry = true;
                    await entry.ReloadAsync(cancellationToken);
                }

                if (!canRetry)
                {
                    throw;
                }
            }
        }

        throw new UnreachableException();
    }

    private void EnsureCanComplete()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (completed)
        {
            throw new InvalidOperationException(
                "Commit or rollback can only be called once per context collection.");
        }
    }

    private void CommitTransactionIfPresent(DbContext context)
    {
        if (!transactions.TryGetValue(context, out var transaction))
        {
            return;
        }

        transaction.Commit();
        transaction.Dispose();
    }

    private async Task CommitTransactionIfPresentAsync(
        DbContext context,
        CancellationToken cancellationToken)
    {
        if (!transactions.TryGetValue(context, out var transaction))
        {
            return;
        }

        await transaction.CommitAsync(cancellationToken);
        await transaction.DisposeAsync();
    }
}
