namespace LyrionVoiceMcp.Ef.Abstractions.DataAccess;

public sealed class ConcurrencyException(
    string message,
    Exception? innerException = null) : Exception(message, innerException);
