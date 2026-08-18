namespace LyrionVoiceMcp.Ef.Abstractions.DataAccess;

public sealed class PersistenceConflictException(string message, Exception innerException)
    : Exception(message, innerException);
