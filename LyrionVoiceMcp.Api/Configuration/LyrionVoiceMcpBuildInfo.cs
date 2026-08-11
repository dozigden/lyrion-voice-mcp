using System.Reflection;

namespace LyrionVoiceMcp.Api.Configuration;

public sealed record LyrionVoiceMcpBuildInfo(
    string Version,
    string Channel,
    string Build,
    string Commit)
{
    public static LyrionVoiceMcpBuildInfo FromConfiguration(
        IConfiguration configuration,
        IHostEnvironment environment,
        Assembly assembly)
    {
        var section = configuration.GetSection("LyrionVoiceMcpBuild");
        var assemblyVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "0.1.0";

        return new LyrionVoiceMcpBuildInfo(
            Read(section, "Version", assemblyVersion),
            Read(section, "Channel", environment.IsDevelopment() ? "development" : "release"),
            Read(section, "Build", "local"),
            Read(section, "Commit", "unknown"));
    }

    private static string Read(IConfigurationSection section, string key, string fallback)
    {
        var configured = section[key]?.Trim();
        return string.IsNullOrWhiteSpace(configured) ? fallback : configured;
    }
}

