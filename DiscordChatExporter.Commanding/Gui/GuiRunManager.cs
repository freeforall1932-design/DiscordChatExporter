using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DiscordChatExporter.Commanding;

public sealed class GuiRunBusyException(GuiRun activeRun)
    : Exception($"The command '{activeRun.Command}' is still running.")
{
    public GuiRun ActiveRun { get; } = activeRun;
}

/// <summary>
/// Owns the application-wide execution slot. Native exports and command runs reserve the
/// same slot, so switching desktop sections cannot accidentally start concurrent exports.
/// </summary>
public sealed class GuiRunManager(string executableName, string versionText, GuiDebugLog debugLog)
    : IDisposable
{
    private const int MaxHistoryCount = 20;
    private readonly object _lock = new();
    private readonly List<GuiRun> _runs = [];
    private bool _isDisposed;

    public string ExecutableName => executableName;

    public GuiRun? Current
    {
        get
        {
            lock (_lock)
                return _runs.LastOrDefault(r => !r.IsFinished);
        }
    }

    public IReadOnlyList<GuiRun> All
    {
        get
        {
            lock (_lock)
                return _runs.ToArray();
        }
    }

    public GuiRun? TryGet(string id)
    {
        lock (_lock)
            return _runs.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.Ordinal));
    }

    private GuiRun Reserve(string name, string commandLine, IReadOnlyList<string> secrets)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (_runs.LastOrDefault(r => !r.IsFinished) is { } activeRun)
                throw new GuiRunBusyException(activeRun);

            var run = new GuiRun(name, commandLine, secrets);
            _runs.Add(run);
            debugLog.Info("run", $"Started '{run.Id}' with: {commandLine}");

            while (_runs.Count(r => r.IsFinished) > MaxHistoryCount)
            {
                var oldestRun = _runs.First(r => r.IsFinished);
                _runs.Remove(oldestRun);
                oldestRun.Cancellation.Dispose();
            }

            return run;
        }
    }

    public GuiRun Start(string commandName, IReadOnlyList<string> arguments, string? token)
    {
        // No UI edit can mutate the arguments of a run after it has started.
        var snapshot = arguments.ToArray();
        var secrets = GuiCommandCatalog
            .GetTokenValues(snapshot)
            .Append(token)
            .Append(Environment.GetEnvironmentVariable("DISCORD_TOKEN"))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        foreach (var secret in secrets)
            debugLog.RegisterSecret(secret);

        var run = Reserve(
            commandName,
            GuiCommandCatalog.FormatCommandLine(executableName, snapshot, token),
            secrets
        );
        _ = Task.Run(() => ExecuteAsync(run, snapshot));
        return run;
    }

    /// <summary>
    /// Reserves the same execution slot for the original desktop workflow. The caller must
    /// complete it in a finally block, and may use its cancellation token and captured log.
    /// </summary>
    public GuiRun BeginActivity(string name, string description, string? token = null)
    {
        debugLog.RegisterSecret(token);
        return Reserve(
            name,
            debugLog.Redact(description),
            string.IsNullOrWhiteSpace(token) ? [] : [token]
        );
    }

    public void CompleteActivity(GuiRun run, int exitCode)
    {
        run.Complete(exitCode, run.Cancellation.IsCancellationRequested);
        debugLog.Info(
            "run",
            $"Finished '{run.Id}' ({run.Command}) as {GuiRun.GetStateName(run.State)}"
                + $" in {(DateTimeOffset.Now - run.StartedAt).TotalSeconds:0.0}s."
        );
    }

    private async Task ExecuteAsync(GuiRun run, IReadOnlyList<string> arguments)
    {
        var exitCode = 1;
        try
        {
            using var console = new GuiConsole(run);
            var application = CommandApplication
                .CreateBuilder()
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
        }
        finally
        {
            CompleteActivity(run, exitCode);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_isDisposed)
                return;
            _isDisposed = true;
            foreach (var run in _runs)
            {
                run.TryCancel();
                // Active writers may still be unwinding. A completed run can release its
                // source now; active ones are left to finish without invalidating Token.
                if (run.IsFinished)
                    run.Cancellation.Dispose();
            }
        }
    }
}
