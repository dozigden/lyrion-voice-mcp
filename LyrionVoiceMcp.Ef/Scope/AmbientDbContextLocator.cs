using LyrionVoiceMcp.Ef.Abstractions.DataAccess;
using Microsoft.EntityFrameworkCore;

namespace LyrionVoiceMcp.Ef.Scope;

public sealed class AmbientDbContextLocator : IAmbientDbContextLocator
{
    public TDbContext? Get<TDbContext>() where TDbContext : DbContext
    {
        var ambientScope = DbContextScope.GetAmbientScope();
        return ambientScope?.DbContexts.Get<TDbContext>();
    }
}
