using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CliFx;

namespace DiscordChatExporter.Cli.Gui;

internal sealed class GuiRunBusyException(GuiRun activeRun)
    : Exception($"The command '{activeRun.Command}' is still running.")
{
    public GuiRun ActiveRun { get; } = activeRun;
}

internal sealed class GuiRunManager(
    string executableName,
    string versionText,
    GuiDebugLog debugLog
) : IDisposable
{
    private const int MaxHistoryCount = 20;

    private readonly object _lock = new();
    private readonly List<GuiRun> _runs = [];

    public string ExecutableName => executableName;

    public GuiRun? Current
    {
        get
        {
            lock (_lock)
            {
                return _runs.LastOrDefault(r => !r.IsFinished);
            }
        }
    }

    public IReadOnlyList<GuiRun> All
    {
        get
        {
            lock (_lock)
            {
                return _runs.ToArray();
            }
        }
    }

    public GuiRun? TryGet(string id)
    {
        lock (_lock)
        {
            return _runs.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// Starts a new run with the specified command-line arguments. Only one command can run at
    /// a time, to prevent the interface from flooding the Discord API with parallel requests.
    /// </summary>
    public GuiRun Start(string commandName, IReadOnlyList<string> arguments, string? token)
    {
        GuiRun run;

        lock (_lock)
        {
            if (_runs.LastOrDefault(r => !r.IsFinished) is { } activeRun)
                throw new GuiRunBusyException(activeRun);

            var commandLine = GuiCommandCatalog.FormatCommandLine(executableName, arguments, token);

            run = new GuiRun(commandName, commandLine);
            _runs.Add(run);

            debugLog.Info(
                "run",
                $"Started '{run.Id}' with: {GuiCommandCatalog.FormatCommandLine(executableName, arguments, token)}"
                    + (string.IsNullOrWhiteSpace(token) ? "" : " (token supplied)")
            );

            // Trim the history to keep the memory usage in check
            while (_runs.Count(r => r.IsFinished) > MaxHistoryCount)
            {
                var oldestRun = _runs.FirstOrDefault(r => r.IsFinished);
                if (oldestRun is null)
                    break;

                _runs.Remove(oldestRun);
                oldestRun.Cancellation.Dispose();
            }
        }

        _ = Task.Run(() => ExecuteAsync(run, arguments));

        return run;
    }

    private async Task ExecuteAsync(GuiRun run, IReadOnlyList<string> arguments)
    {
        var exitCode = 1;

        try
        {
            using var console = new GuiConsole(run);


            var application = new CommandLineApplicationBuilder()
                .AddCommandsFromThisAssembly()
                .SetTitle("DiscordChatExporter")
                .SetExecutableName(executableName)
                .SetVersion(versionText)
                .UseConsole(console)
                .Build();

            exitCode = await application.RunAsync(arguments);

            if (exitCode != 0)
                debugLog.Warn("run", $"'{run.Id}' exited with code {exitCode}.");
        }
        catch (Exception ex)
        {
            run.Append(ex + Environment.NewLine);
            debugLog.Exception("run", ex);
            exitCode = 1;
        }
        finally
        {
            run.Complete(exitCode, run.Cancellation.IsCancellationRequested);

            debugLog.Info(
                "run",
                $"Finished '{run.Id}' ({run.Command}) as {GuiRun.GetStateName(run.State)}"
                    + $" in {(DateTimeOffset.Now - run.StartedAt).TotalSeconds:0.0}s."
            );
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var run in _runs)
            {
                try
                {
                    run.Cancellation.Cancel();
                    run.Cancellation.Dispose();
                }
                catch (ObjectDisposedException) { }
            }

            _runs.Clear();
        }
    }
}
