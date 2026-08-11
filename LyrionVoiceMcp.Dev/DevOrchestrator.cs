namespace LyrionVoiceMcp.Dev;

internal sealed class DevOrchestrator
{
    private readonly string repoRoot;
    private readonly PortConflictResolver portConflictResolver = new();
    private readonly List<ManagedService> services;
    private readonly CancellationTokenSource shutdown = new();
    private Task? activeOperation;
    private int selectedServiceIndex;
    private int selectedLogIndex;
    private string statusMessage = "Ready. Press A to start all services.";

    public DevOrchestrator(string repoRoot)
    {
        this.repoRoot = repoRoot;
        services = [CreateApiService(), CreateWebService()];
    }

    public async Task<int> RunAsync()
    {
        if (Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            Console.Error.WriteLine(
                "The interactive orchestrator requires a terminal. Use dev-startall for unattended startup.");
            return 1;
        }

        var originalCancelHandling = Console.TreatControlCAsInput;
        Console.TreatControlCAsInput = true;

        try
        {
            Console.CursorVisible = false;
            await DrawLoopAsync(shutdown.Token);
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        finally
        {
            shutdown.Cancel();
            await AwaitActiveOperationAsync();
            await StopAllAsync();
            Console.CursorVisible = true;
            Console.TreatControlCAsInput = originalCancelHandling;
            Console.ResetColor();
            Console.Clear();
        }
    }

    private async Task DrawLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            Render();
            var delayTask = Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
            while (Console.KeyAvailable)
            {
                HandleKey(Console.ReadKey(intercept: true), cancellationToken);
                Render();
            }

            await delayTask;
        }
    }

    private void HandleKey(ConsoleKeyInfo key, CancellationToken cancellationToken)
    {
        if (key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.Key == ConsoleKey.C)
        {
            shutdown.Cancel();
            return;
        }

        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
            case ConsoleKey.K:
                SelectService(Math.Max(0, selectedServiceIndex - 1));
                break;
            case ConsoleKey.DownArrow:
            case ConsoleKey.J:
                SelectService(Math.Min(services.Count - 1, selectedServiceIndex + 1));
                break;
            case ConsoleKey.D1:
            case ConsoleKey.NumPad1:
                SelectService(0);
                break;
            case ConsoleKey.D2:
            case ConsoleKey.NumPad2:
                SelectService(1);
                break;
            case ConsoleKey.Spacebar:
                BeginOperation(() => ToggleSelectedAsync(cancellationToken));
                break;
            case ConsoleKey.R:
                BeginOperation(() => RestartSelectedAsync(cancellationToken));
                break;
            case ConsoleKey.A:
                BeginOperation(() => StartAllAsync(cancellationToken));
                break;
            case ConsoleKey.X:
                BeginOperation(StopAllWithStatusAsync);
                break;
            case ConsoleKey.L:
                selectedLogIndex = (selectedLogIndex + 1) % services.Count;
                break;
            case ConsoleKey.Q:
            case ConsoleKey.Escape:
                shutdown.Cancel();
                break;
        }
    }

    private void BeginOperation(Func<Task> operation)
    {
        if (activeOperation is { IsCompleted: false })
        {
            statusMessage = "A service operation is already in progress.";
            return;
        }

        activeOperation = RunOperationAsync(operation);
    }

    private async Task RunOperationAsync(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            statusMessage = $"Operation failed: {exception.Message}";
        }
    }

    private async Task AwaitActiveOperationAsync()
    {
        if (activeOperation is null)
        {
            return;
        }

        try
        {
            await activeOperation;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void SelectService(int index)
    {
        selectedServiceIndex = index;
        selectedLogIndex = index;
    }

    private async Task ToggleSelectedAsync(CancellationToken cancellationToken)
    {
        var service = services[selectedServiceIndex];
        if (service.IsRunning)
        {
            await service.StopAsync();
            statusMessage = $"Stopped {service.Name}.";
            return;
        }

        await StartServiceAsync(service, cancellationToken);
    }

    private async Task RestartSelectedAsync(CancellationToken cancellationToken)
    {
        var service = services[selectedServiceIndex];
        await service.StopAsync();
        await StartServiceAsync(service, cancellationToken);
    }

    private async Task StartAllAsync(CancellationToken cancellationToken)
    {
        foreach (var service in services.Where(service => !service.IsRunning))
        {
            await StartServiceAsync(service, cancellationToken);
        }
    }

    private async Task StopAllWithStatusAsync()
    {
        await StopAllAsync();
        statusMessage = "Stopped all services.";
    }

    private async Task StopAllAsync()
    {
        foreach (var service in services)
        {
            await service.StopAsync();
        }
    }

    private async Task StartServiceAsync(ManagedService service, CancellationToken cancellationToken)
    {
        statusMessage = $"Starting {service.Name}...";
        await service.StartAsync(cancellationToken);
        statusMessage = $"Started {service.Name}.";
    }

    private ManagedService CreateApiService()
    {
        var apiProject = Path.Combine(repoRoot, "LyrionVoiceMcp.Api", "LyrionVoiceMcp.Api.csproj");
        var logPath = Path.Combine(repoRoot, ".data", "dev", "logs", "api.log");
        var dotnet = ExecutableLocator.Find("dotnet") ?? "dotnet";

        return new ManagedService(
            "API",
            DevApiLaunchSettings.Endpoint,
            dotnet,
            DevApiLaunchSettings.CreateRunArguments(apiProject),
            repoRoot,
            logPath,
            DevApiLaunchSettings.CreateEnvironment(),
            async (service, cancellationToken) =>
            {
                EnsureExecutableExists("dotnet", dotnet);
                await portConflictResolver.StopRecognisedListenerAsync(
                    DevApiLaunchSettings.Port,
                    ["LyrionVoiceMcp.Api"],
                    cancellationToken);
                await service.RunPreparationCommandAsync(
                    dotnet,
                    ["build", apiProject, "-maxcpucount:1", "-nodeReuse:false"],
                    repoRoot,
                    cancellationToken);
            });
    }

    private ManagedService CreateWebService()
    {
        var webDirectory = Path.Combine(repoRoot, "LyrionVoiceMcp.Web");
        var logPath = Path.Combine(repoRoot, ".data", "dev", "logs", "web.log");
        var npm = ExecutableLocator.Find("npm") ?? "npm";

        return new ManagedService(
            "Web",
            DevWebLaunchSettings.Endpoint,
            npm,
            ["run", "dev"],
            webDirectory,
            logPath,
            DevWebLaunchSettings.CreateEnvironment(),
            async (_, cancellationToken) =>
            {
                EnsureExecutableExists("npm", npm);
                if (!Directory.Exists(Path.Combine(webDirectory, "node_modules")))
                {
                    throw new InvalidOperationException(
                        "LyrionVoiceMcp.Web/node_modules is missing. Run npm ci in LyrionVoiceMcp.Web first.");
                }

                await portConflictResolver.StopRecognisedListenerAsync(
                    DevWebLaunchSettings.Port,
                    ["vite"],
                    cancellationToken);
            });
    }

    private static void EnsureExecutableExists(string displayName, string resolvedPath)
    {
        if (ExecutableLocator.Find(displayName) is null && !File.Exists(resolvedPath))
        {
            throw new InvalidOperationException($"{displayName} is required but was not found on PATH.");
        }
    }

    private void Render()
    {
        var width = Math.Max(1, Console.WindowWidth - 1);
        var height = Math.Max(1, Console.WindowHeight);
        var logHeight = Math.Max(5, height - 11 - services.Count);

        Console.SetCursorPosition(0, 0);
        WriteLine("Lyrion Voice MCP Dev", width, ConsoleColor.Cyan);
        WriteLine(new string('=', Math.Min(width, 120)), width, ConsoleColor.DarkGray);
        WriteLine("Services", width, ConsoleColor.White);

        for (var index = 0; index < services.Count; index++)
        {
            var service = services[index];
            var selector = index == selectedServiceIndex ? ">" : " ";
            var marker = "[--]";
            if (service.State.HasFailed)
            {
                marker = "[!!]";
            }
            else if (service.IsRunning)
            {
                marker = "[OK]";
            }
            var processId = service.ProcessId?.ToString() ?? "-";
            var uptime = service.IsRunning ? FormatDuration(service.Uptime) : "--:--";
            var line = $"{selector} {index + 1}. {marker} {service.Name,-4} {service.State.Text,-10} pid {processId,-7} up {uptime,-8} {service.Endpoint}";
            WriteLine(line, width, service.IsRunning ? ConsoleColor.Green : ConsoleColor.Gray);
        }

        WriteLine(string.Empty, width);
        WriteLine(
            "Keys: Up/Down  Space start/stop  R restart  A start all  X stop all  L logs  1-2 select  Q quit",
            width,
            ConsoleColor.Yellow);
        WriteLine($"Status: {statusMessage}", width, ConsoleColor.White);
        WriteLine(string.Empty, width);

        var logService = services[selectedLogIndex];
        WriteLine($"Logs: {logService.Name} ({logService.LogPath})", width, ConsoleColor.White);
        WriteLine(new string('-', Math.Min(width, 120)), width, ConsoleColor.DarkGray);
        foreach (var line in logService.GetLogLines(logHeight))
        {
            WriteLine(line, width, ConsoleColor.DarkGray);
        }
    }

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes:00}:{duration.Seconds:00}";

    private static void WriteLine(string text, int width, ConsoleColor? colour = null)
    {
        if (colour is not null)
        {
            Console.ForegroundColor = colour.Value;
        }

        var clipped = text.Length > width ? text[..width] : text;
        Console.Write(clipped);
        if (clipped.Length < width)
        {
            Console.Write(new string(' ', width - clipped.Length));
        }

        Console.WriteLine();
        if (colour is not null)
        {
            Console.ResetColor();
        }
    }
}
