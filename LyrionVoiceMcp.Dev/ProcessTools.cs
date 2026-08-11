using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace LyrionVoiceMcp.Dev;

internal static class ExecutableLocator
{
    public static string? Find(string executable)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var candidates = OperatingSystem.IsWindows()
            ? new[] { executable + ".exe", executable + ".cmd", executable + ".bat", executable }
            : new[] { executable };

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var candidate in candidates)
            {
                var fullPath = Path.Combine(directory, candidate);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
        }

        return null;
    }
}

internal sealed partial class PortConflictResolver
{
    [GeneratedRegex(@"pid=(?<pid>[0-9]+)", RegexOptions.CultureInvariant)]
    private static partial Regex ProcessIdPattern();

    public async Task StopRecognisedListenerAsync(
        int port,
        IReadOnlyList<string> requiredCommandFragments,
        CancellationToken cancellationToken)
    {
        if (IsPortAvailable(port))
        {
            return;
        }

        if (!OperatingSystem.IsLinux())
        {
            throw new InvalidOperationException($"Port {port} is already in use.");
        }

        var processIds = await FindLinuxListenerProcessIdsAsync(port, cancellationToken);
        foreach (var processId in processIds)
        {
            var commandLine = ReadLinuxCommandLine(processId);
            if (!IsRecognised(commandLine, requiredCommandFragments))
            {
                continue;
            }

            try
            {
                using var process = Process.GetProcessById(processId);
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (ArgumentException)
            {
            }
        }

        if (!IsPortAvailable(port))
        {
            throw new InvalidOperationException(
                $"Port {port} is occupied by a process that was not recognised as part of Lyrion Voice MCP.");
        }
    }

    internal static bool IsRecognised(
        string commandLine,
        IReadOnlyList<string> requiredCommandFragments) =>
        requiredCommandFragments.All(fragment =>
            commandLine.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private static bool IsPortAvailable(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static async Task<IReadOnlyList<int>> FindLinuxListenerProcessIdsAsync(
        int port,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "ss",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-ltnp");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not inspect listening ports with ss.");
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.Contains($":{port}", StringComparison.Ordinal))
            .SelectMany(line => ProcessIdPattern().Matches(line).Select(match => match.Groups["pid"].Value))
            .Select(value => int.TryParse(value, out var processId) ? processId : 0)
            .Where(processId => processId > 0)
            .Distinct()
            .ToArray();
    }

    private static string ReadLinuxCommandLine(int processId)
    {
        try
        {
            var bytes = File.ReadAllBytes($"/proc/{processId}/cmdline");
            return System.Text.Encoding.UTF8.GetString(bytes).Replace('\0', ' ');
        }
        catch (IOException)
        {
            return string.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }
}

