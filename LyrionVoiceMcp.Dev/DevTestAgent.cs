using System.Diagnostics;
using System.Text.Json;

namespace LyrionVoiceMcp.Dev;

internal sealed record DevAgentServer(string Name, bool IsStdio);

internal static class DevTestAgent
{
    private const string Endpoint = DevApiLaunchSettings.Endpoint + "/mcp";

    internal static string FindCodex()
    {
        var executable = ExecutableLocator.Find("codex");
        if (executable is not null) return executable;

        var home = Environment.GetEnvironmentVariable("CODEX_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        var bundled = Path.Combine(home, "packages", "app-server-daemon", "current",
            OperatingSystem.IsWindows() ? "codex.exe" : "codex");
        return File.Exists(bundled) ? bundled : throw new InvalidOperationException(
            "Could not find Codex on PATH or in its desktop package.");
    }

    public static async Task RunAsync(string codex, CancellationToken cancellationToken)
    {
        await WaitForApiAsync(cancellationToken);
        var workspace = Directory.CreateTempSubdirectory("lyrion-mcp-agent-");
        try
        {
            var inherited = await ListServersAsync(codex, workspace.FullName, cancellationToken);
            var name = $"lyrion_dev_{Guid.NewGuid():N}";
            await File.WriteAllTextAsync(Path.Combine(workspace.FullName, "AGENTS.md"), $"""
                You are testing the Lyrion Voice MCP development server at {Endpoint}.
                Use the {name} MCP server for all music and player requests. Discover players with get_player_status.
                Exercise the public MCP tools and report their errors. Use playback or queue mutations only when requested.
                """, cancellationToken);
            Console.WriteLine("Opening Codex. Exit the test agent to return to the dashboard.");
            using var process = Process.Start(CreateStartInfo(codex, workspace.FullName, name, inherited))
                ?? throw new InvalidOperationException("Could not start the MCP test agent.");
            await WaitForExitAsync(process, cancellationToken);
            if (process.ExitCode != 0) throw new InvalidOperationException($"Codex exited with code {process.ExitCode}.");
        }
        finally
        {
            workspace.Delete(recursive: true);
        }
    }

    internal static ProcessStartInfo CreateStartInfo(string codex, string workspace, string name,
        IReadOnlyList<DevAgentServer> inherited)
    {
        var start = new ProcessStartInfo(codex) { WorkingDirectory = workspace, UseShellExecute = false };
        var servers = inherited.Select(server =>
        {
            var transport = server.IsStdio ? "command=\"false\"" : $"url=\"{Endpoint}\"";
            return $"{JsonSerializer.Serialize(server.Name)}={{{transport},enabled=false}}";
        }).Append($"{JsonSerializer.Serialize(name)}={{url=\"{Endpoint}\",enabled=true,required=true}}");
        foreach (var arg in new[] { "--no-daemon", "--cd", workspace, "--sandbox", "read-only",
            "--config", $"mcp_servers={{{string.Join(',', servers)}}}",
            "--config", $"projects={{{JsonSerializer.Serialize(workspace)}={{trust_level=\"trusted\"}}}}",
            $"Call get_player_status using {name} to confirm the development connection. Then wait for my testing instructions." })
        {
            start.ArgumentList.Add(arg);
        }
        return start;
    }

    private static async Task<IReadOnlyList<DevAgentServer>> ListServersAsync(string codex, string workspace,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(codex)
        {
            WorkingDirectory = workspace, UseShellExecute = false, RedirectStandardOutput = true,
            ArgumentList = { "--no-daemon", "mcp", "list", "--json" }
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not list Codex MCP servers.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        await WaitForExitAsync(process, cancellationToken);
        if (process.ExitCode != 0) throw new InvalidOperationException("Codex could not list its MCP servers.");
        using var list = JsonDocument.Parse(await output);
        return list.RootElement.EnumerateArray().Select(server => new DevAgentServer(
            server.GetProperty("name").GetString()!, server.GetProperty("transport").GetProperty("type").GetString() == "stdio")).ToArray();
    }

    private static async Task WaitForExitAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }

    private static async Task WaitForApiAsync(CancellationToken cancellationToken)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            while (true)
            {
                try
                {
                    using var response = await http.GetAsync(DevApiLaunchSettings.Endpoint + "/api/health", deadline.Token);
                    if (response.IsSuccessStatusCode) return;
                }
                catch (HttpRequestException) { }
                catch (OperationCanceledException) when (!deadline.IsCancellationRequested) { }
                await Task.Delay(250, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Timed out waiting for the development API. Check its log.");
        }
    }
}
