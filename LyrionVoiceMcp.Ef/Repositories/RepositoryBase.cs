using LyrionVoiceMcp.Ef.Abstractions.DataAccess;
using Microsoft.EntityFrameworkCore;

namespace LyrionVoiceMcp.Ef.Repositories;

public abstract class RepositoryBase(
    IAmbientDbContextLocator ambientDbContextLocator)
{
    protected LyrionVoiceMcpDbContext DbContext =>
        ambientDbContextLocator.Get<LyrionVoiceMcpDbContext>()
        ?? throw new InvalidOperationException(
            "No ambient DbContext is available. Wrap repository access in a read-only or read/write context scope.");
}

public abstract class RepositoryBase<TEntity>(
    IAmbientDbContextLocator ambientDbContextLocator)
    : RepositoryBase(ambientDbContextLocator), IRepositoryBase<TEntity>
    where TEntity : class
{
    protected DbSet<TEntity> DbSet => DbContext.Set<TEntity>();

    public virtual IQueryable<TEntity> Query() => DbSet;

    public virtual TEntity? Get(int id) => DbSet.Find(id);

    public virtual void Add(TEntity entity) => DbSet.Add(entity);

    public virtual void AddRange(IEnumerable<TEntity> entities) => DbSet.AddRange(entities);

    public virtual void Remove(TEntity entity) => DbSet.Remove(entity);

    public virtual void RemoveRange(IEnumerable<TEntity> entities) => DbSet.RemoveRange(entities);
}
