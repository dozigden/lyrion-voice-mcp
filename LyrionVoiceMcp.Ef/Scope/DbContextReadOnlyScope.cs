using System.Data;
using LyrionVoiceMcp.Ef.Abstractions.DataAccess;

namespace LyrionVoiceMcp.Ef.Scope;

internal sealed class DbContextReadOnlyScope(
    DbContextScopeOption joiningOption,
    IsolationLevel? isolationLevel,
    IDbContextFactory dbContextFactory)
    : DbContextScope(joiningOption, true, isolationLevel, dbContextFactory),
        IDbContextReadOnlyScope
{
}
