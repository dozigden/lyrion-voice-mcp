namespace LyrionVoiceMcp.Ef.Scope;

internal sealed class AmbientContextSuppressor : IDisposable
{
    private readonly DbContextScope? savedScope;
    private bool disposed;

    public AmbientContextSuppressor()
    {
        savedScope = DbContextScope.GetAmbientScope();
        DbContextScope.HideAmbientScope();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        if (savedScope is not null)
        {
            DbContextScope.SetAmbientScope(savedScope);
        }

        disposed = true;
    }
}
