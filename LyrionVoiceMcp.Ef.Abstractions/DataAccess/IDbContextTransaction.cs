namespace LyrionVoiceMcp.Ef.Abstractions.DataAccess;

public interface IDbContextTransaction : IDisposable, IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);
}

public interface IDbContextTransactionScope
{
    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    void ClearTrackedEntities();
}
