using Microsoft.EntityFrameworkCore;

namespace LyrionVoiceMcp.Ef.Abstractions.DataAccess;

public interface IAmbientDbContextLocator
{
    TDbContext? Get<TDbContext>() where TDbContext : DbContext;
}
