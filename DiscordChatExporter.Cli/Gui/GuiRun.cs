using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DiscordChatExporter.Cli.Gui;

internal enum GuiRunState
{
    Running,
    Succeeded,
    Failed,
    Cancelled,
}

/// <summary>
/// Represents a single invocation of a CLI command, including its state and captured output.
/// </summary>
internal sealed partial class GuiRun
{
    private const int MaxOutputLength = 4 * 1024 * 1024;

    private readonly object _lock = new();
    private readonly StringBuilder _output = new();

    private volatile bool _isFinished;
    private int _progress = -1;

    public GuiRun(string command, string commandLine)
    {
        Command = command;
        CommandLine = commandLine;
    }

    public string Id { get; } = Guid.NewGuid().ToString("n")[..12];

    public string Command { get; }

    public string CommandLine { get; }

    public DateTimeOffset StartedAt { get; } = DateTimeOffset.Now;

    public CancellationTokenSource Cancellation { get; } = new();

    public GuiRunState State { get; private set; } = GuiRunState.Running;

    public int? ExitCode { get; private set; }

    public DateTimeOffset? FinishedAt { get; private set; }

    public bool IsFinished => _isFinished;

    public void Append(string text)
    {
        if (string.IsNullOrEmpty(text) || _isFinished)
            return;

        lock (_lock)
        {
            if (_output.Length >= MaxOutputLength)
            {
                if (_output.Length == MaxOutputLength)
                {
                    _output.Append(
                        Environment.NewLine
                            + $"[output truncated after {MaxOutputLength / 1024 / 1024} MB]"
                            + Environment.NewLine
                    );
                }

                return;
            }

            _output.Append(text);

            if (TryReadProgress(text) is { } progress)
                _progress = progress;
        }
    }

    public void Complete(int exitCode, bool isCancelled)
    {
        lock (_lock)
        {
            if (_isFinished)
                return;

            ExitCode = exitCode;
            FinishedAt = DateTimeOffset.Now;
            State =
                isCancelled ? GuiRunState.Cancelled
                : exitCode == 0 ? GuiRunState.Succeeded
                : GuiRunState.Failed;

            _isFinished = true;
        }

        try
        {
            Cancellation.Dispose();
        }
        catch (ObjectDisposedException) { }
    }

    public GuiRunDto Snapshot(int cursor)
    {
        lock (_lock)
        {
            var actualCursor = Math.Clamp(cursor, 0, _output.Length);

            return new GuiRunDto(
                Id,
                Command,
                CommandLine,
                GetStateName(State),
                ExitCode,
                StartedAt.ToString("o", CultureInfo.InvariantCulture),
                FinishedAt?.ToString("o", CultureInfo.InvariantCulture),
                _output.Length,
                _output.ToString(actualCursor, _output.Length - actualCursor),
                _progress >= 0 ? _progress : null
            );
        }
    }

    /// <summary>
    /// Returns the output produced since the given cursor, optionally waiting for new output to
    /// appear. This is used to stream the output to the browser without keeping a connection open.
    /// </summary>
    public async Task<GuiRunDto> PollAsync(
        int cursor,
        int waitMilliseconds,
        CancellationToken cancellationToken
    )
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(Math.Clamp(waitMilliseconds, 0, 30_000));

        while (true)
        {
            var snapshot = Snapshot(cursor);

            if (snapshot.Output.Length > 0 || IsFinished)
                return snapshot;

            if (DateTime.UtcNow >= deadline)
                return snapshot;

            await Task.Delay(100, cancellationToken);
        }
    }

    public static string GetStateName(GuiRunState state) =>
        state switch
        {
            GuiRunState.Running => "running",
            GuiRunState.Succeeded => "succeeded",
            GuiRunState.Failed => "failed",
            _ => "cancelled",
        };

    private static int? TryReadProgress(string text)
    {
        // Spectre.Console renders progress bars as "[####----] 45%", so the percentage can be
        // picked up from the output stream to drive the progress bar in the interface.
        var matches = PercentageRegex().Matches(text);
        if (matches.Count <= 0)
            return null;

        var lastMatch = matches[^1];
        if (
            !int.TryParse(
                lastMatch.Groups[1].Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var percentage
            )
        )
            return null;

        return Math.Clamp(percentage, 0, 100);
    }

    [GeneratedRegex(@"(\d{1,3})\s*%")]
    private static partial Regex PercentageRegex();
}
