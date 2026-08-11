namespace LyrionVoiceMcp.Dev;

internal static class DevApiLaunchSettings
{
    public const int Port = 5600;
    public const string Endpoint = "http://127.0.0.1:5600";
    public const string LoadLocalSettingsKey = "LyrionVoiceMcpDevelopment__LoadLocalSettings";

    public static IReadOnlyList<string> CreateRunArguments(string apiProject) =>
    [
        "run",
        "--no-launch-profile",
        "--no-build",
        "--project",
        apiProject
    ];

    public static IReadOnlyDictionary<string, string> CreateEnvironment() =>
        new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["DOTNET_ENVIRONMENT"] = "Development",
            ["ASPNETCORE_URLS"] = Endpoint,
            [LoadLocalSettingsKey] = "true"
        };
}

internal static class DevWebLaunchSettings
{
    public const int Port = 5175;
    public const string Endpoint = "http://localhost:5175";

    public static IReadOnlyDictionary<string, string> CreateEnvironment() =>
        new Dictionary<string, string>();
}
