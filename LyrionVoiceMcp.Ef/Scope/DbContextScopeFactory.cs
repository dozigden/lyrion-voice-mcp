using System.Data;
using LyrionVoiceMcp.Ef.Abstractions.DataAccess;

namespace LyrionVoiceMcp.Ef.Scope;

public sealed class DbContextScopeFactory(
    IDbContextFactory dbContextFactory) : IDbContextScopeFactory
{
    public IDbContextScope Create(
        DbContextScopeOption joiningOption = DbContextScopeOption.JoinExisting) =>
        new DbContextWriteScope(joiningOption, null, dbContextFactory);

    public IDbContextReadOnlyScope CreateReadOnly(
        DbContextScopeOption joiningOption = DbContextScopeOption.JoinExisting) =>
        new DbContextReadOnlyScope(joiningOption, null, dbContextFactory);

    public IDbContextScope CreateWithTransaction(IsolationLevel isolationLevel) =>
        new DbContextWriteScope(
            DbContextScopeOption.ForceCreateNew,
            isolationLevel,
            dbContextFactory);

    public IDbContextReadOnlyScope CreateReadOnlyWithTransaction(
        IsolationLevel isolationLevel) =>
        new DbContextReadOnlyScope(
            DbContextScopeOption.ForceCreateNew,
            isolationLevel,
            dbContextFactory);

    public IDisposable SuppressAmbientContext() => new AmbientContextSuppressor();
}
