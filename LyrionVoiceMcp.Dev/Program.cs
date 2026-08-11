namespace LyrionVoiceMcp.Dev;

internal static class Program
{
    private static async Task<int> Main()
    {
        try
        {
            var repoRoot = RepoRootLocator.Find();
            var orchestrator = new DevOrchestrator(repoRoot);
            return await orchestrator.RunAsync();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Lyrion Voice MCP dev orchestrator failed: {exception.Message}");
            return 1;
        }
    }
}

internal static class RepoRootLocator
{
    public static string Find()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LyrionVoiceMcp.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find LyrionVoiceMcp.slnx.");
    }
}

