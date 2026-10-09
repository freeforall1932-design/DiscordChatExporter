using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using DiscordChatExporter.Commanding;

namespace DiscordChatExporter.Gui.Services;

/// <summary>
/// The desktop's shared token, execution history and diagnostics. It opens no web server
/// and launches no child CLI process: the real command classes run through the shared runner.
/// </summary>
public sealed partial class DesktopCommandService : ObservableObject, IDisposable
{
    private readonly SettingsService _settings;
    private readonly DispatcherTimer _timer;
    private readonly StringBuilder _output = new();
    private readonly DateTimeOffset _startedAt = DateTimeOffset.Now;
    private string? _runId;
    private int _cursor;
    private bool _isDisposed;

    public DesktopCommandService(SettingsService settings)
    {
        _settings = settings;
        Manager = new GuiRunManager("DiscordChatExporter.Cli", Program.VersionString, DebugLog);
        Token = settings.LastToken;
        DebugLog.RegisterSecret(Token);
        DebugLog.Info("desktop", "Native Commands workspace initialized. No HTTP listener is started.");
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _timer.Tick += OnTick;
        _timer.Start();
        Refresh();
    }

    public GuiDebugLog DebugLog { get; } = new();

    public GuiRunManager Manager { get; }

    public SettingsService Settings => _settings;

    // Keep the in-memory token stable while SettingsService.Save() temporarily removes
    // LastToken to implement its existing "do not remember" persistence policy.
    [ObservableProperty]
    public partial string? Token { get; set; }

    partial void OnTokenChanged(string? value)
    {
        _settings.LastToken = value;
        DebugLog.RegisterSecret(value?.Trim('"', ' '));
        OnPropertyChanged(nameof(TokenStatus));
    }

    public bool HasEnvironmentToken =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISCORD_TOKEN"));

    public string TokenStatus =>
        !string.IsNullOrWhiteSpace(Token) ? "Token entered" : HasEnvironmentToken ? "Using DISCORD_TOKEN" : "Token not set";

    public string? SelectedGuildId { get; set; }

    public IReadOnlyList<string> SelectedChannelIds { get; set; } = [];

    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial string StatusText { get; private set; } = "Idle";

    [ObservableProperty]
    public partial string OutputText { get; private set; } = "Run a command to see its output here.";

    [ObservableProperty]
    public partial string DebugText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial double ProgressValue { get; private set; }

    [ObservableProperty]
    public partial bool IsProgressIndeterminate { get; private set; }

    [ObservableProperty]
    public partial GuiRunDto? LatestRun { get; private set; }

    public GuiRun Start(GuiCommandDto command, IReadOnlyDictionary<string, string[]> values)
    {
        var token = Token?.Trim('"', ' ');
        var errors = GuiCommandCatalog.Validate(command, token, values, HasEnvironmentToken);
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors));
        _settings.Save();
        var run = Manager.Start(command.Name, GuiCommandCatalog.BuildArguments(command, token, values), token);
        Refresh();
        return run;
    }

    public GuiRun StartRaw(string commandLine)
    {
        var token = Token?.Trim('"', ' ');
        var arguments = GuiCommandCatalog.BuildRawArguments(commandLine, token);
        _settings.Save();
        var run = Manager.Start(arguments[0], arguments, token);
        Refresh();
        return run;
    }

    public GuiRun BeginActivity(string name, string description)
    {
        var run = Manager.BeginActivity(name, description, Token?.Trim('"', ' '));
        Refresh();
        return run;
    }

    public void CompleteActivity(GuiRun run, int exitCode)
    {
        Manager.CompleteActivity(run, exitCode);
        Refresh();
    }

    public void Cancel()
    {
        if (Manager.Current is { } run && run.TryCancel())
            DebugLog.Info("run", $"Cancellation requested for '{run.Id}'.");
        Refresh();
    }

    private void OnTick(object? sender, EventArgs args) => Refresh();

    public void Refresh()
    {
        if (_isDisposed)
            return;
        IsBusy = Manager.Current is not null;
        var run = Manager.All.LastOrDefault();
        if (run is not null)
        {
            if (_runId != run.Id)
            {
                _runId = run.Id;
                _cursor = 0;
                _output.Clear();
                _output.Append("$ ").Append(run.CommandLine).AppendLine().AppendLine();
            }
            var snapshot = run.Snapshot(_cursor);
            _cursor = snapshot.Cursor;
            if (snapshot.Output.Length > 0)
                _output.Append(snapshot.Output);
            LatestRun = snapshot;
            OutputText = _output.ToString();
            StatusText = snapshot.State switch
            {
                "running" => $"Running {snapshot.Command}…",
                "succeeded" => $"Done {snapshot.Command}",
                "cancelled" => $"Cancelled {snapshot.Command}",
                _ => $"Failed {snapshot.Command}",
            };
            ProgressValue = snapshot.Progress ?? 0;
            IsProgressIndeterminate = IsBusy && snapshot.Progress is null;
        }
        DebugText =
            $"Version: {Program.VersionString}\nRuntime: {RuntimeInformation.FrameworkDescription}\n"
            + $"Working directory: {Environment.CurrentDirectory}\nInterface: native Avalonia (no HTTP server)\n"
            + $"Started: {_startedAt:O}\nToken: {TokenStatus}\n\n"
            + string.Join("\n", DebugLog.Snapshot().Select(e => $"[{e.Timestamp}] {e.Level} / {e.Category}: {e.Message}"));
    }

    public GuiDebugDto SnapshotDebug() =>
        new(
            new GuiEnvironmentDto(
                Program.Name, Program.VersionString, Program.Name, Environment.CurrentDirectory,
                "Native desktop (no HTTP listener)", false, HasEnvironmentToken, _startedAt.ToString("O"),
                RuntimeInformation.FrameworkDescription, Environment.ProcessId
            ),
            DebugLog.Snapshot(),
            Manager.All.Select(r => new GuiRunSummaryDto(r.Id, r.Command, GuiRun.GetStateName(r.State), r.ExitCode, r.StartedAt.ToString("O"))).ToArray(),
            0
        );

    public string ExportDebugJson() => JsonSerializer.Serialize(SnapshotDebug(), DesktopJsonContext.Default.GuiDebugDto);

    public void Dispose()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
        Manager.Dispose();
    }
}
