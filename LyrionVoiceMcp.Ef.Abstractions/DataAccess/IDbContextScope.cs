namespace LyrionVoiceMcp.Ef.Abstractions.DataAccess;

public interface IDbContextScopeBase : IDisposable
{
    IDbContextCollection DbContexts { get; }
}

public interface IDbContextScope : IDbContextScopeBase
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    Task TransactionAsync(
        Func<IDbContextTransactionScope, IDbContextTransaction, Task> executor,
        CancellationToken cancellationToken = default);
}

public interface IDbContextReadOnlyScope : IDbContextScopeBase
{
}
