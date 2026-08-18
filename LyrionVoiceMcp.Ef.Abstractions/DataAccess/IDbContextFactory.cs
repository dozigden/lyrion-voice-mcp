using Microsoft.EntityFrameworkCore;

namespace LyrionVoiceMcp.Ef.Abstractions.DataAccess;

public interface IDbContextFactory
{
    TDbContext CreateDbContext<TDbContext>() where TDbContext : DbContext;
}
