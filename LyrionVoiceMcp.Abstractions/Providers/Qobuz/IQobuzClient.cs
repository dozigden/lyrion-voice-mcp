namespace LyrionVoiceMcp.Abstractions.Providers.Qobuz;

public interface IQobuzClient
{
    Task<QobuzConfigurationStatus> GetConfigurationStatusAsync(CancellationToken cancellationToken);
}

public enum QobuzConfigurationStatus
{
    NotInstalled,
    NotConfigured,
    Configured
}
