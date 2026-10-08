using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace DiscordChatExporter.Cli.Gui;

/// <summary>
/// A single diagnostic event, as shown in the "Debug" tab of the interface.
/// </summary>
internal sealed record GuiDebugEventDto(
    string Timestamp,
    string Level,
    string Category,
    string Message
);

/// <summary>
/// Keeps a rolling log of everything the web interface does, so that problems can be
/// diagnosed without a debugger attached. Secrets are removed before they get here.
/// </summary>
internal sealed partial class GuiDebugLog(int capacity = 500)
{
    private readonly object _lock = new();
    private readonly Queue<GuiDebugEventDto> _events = new();

    /// <summary>
    /// Whether events should also be printed to the console that hosts the web interface.
    /// </summary>
    public bool IsVerbose { get; init; }

    public event Action<GuiDebugEventDto>? Written;

    public void Write(string level, string category, string message)
    {
        var @event = new GuiDebugEventDto(
            DateTimeOffset.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
            level,
            category,
            GuiRedaction.Redact(message)
        );

        lock (_lock)
        {
            _events.Enqueue(@event);

            while (_events.Count > capacity)
                _events.Dequeue();
        }

        if (IsVerbose)
            Written?.Invoke(@event);
    }

    public void Info(string category, string message) => Write("info", category, message);

    public void Warn(string category, string message) => Write("warn", category, message);

    public void Error(string category, string message) => Write("error", category, message);

    public void Exception(string category, Exception exception) =>
        Write("error", category, exception.ToString());

    public IReadOnlyList<GuiDebugEventDto> Snapshot()
    {
        lock (_lock)
        {
            return _events.ToArray();
        }
    }
}

/// <summary>
/// Removes secrets (Discord tokens in particular) from diagnostic text, so that the debug
/// information can be shared without leaking credentials.
/// </summary>
internal static partial class GuiRedaction
{
    private const string Placeholder = "***";

    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var result = text;

        // --token <value> and --token=<value>
        result = TokenArgumentRegex().Replace(result, match => match.Groups[1].Value + Placeholder);

        // "token": "<value>" in request bodies
        result = TokenJsonRegex().Replace(result, match => match.Groups[1].Value + Placeholder);

        // Authorization: Bearer <value>
        result = BearerRegex().Replace(result, match => match.Groups[1].Value + Placeholder);

        return result;
    }

    [GeneratedRegex(
        @"(--token[\s=]+)\S+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex TokenArgumentRegex();

    [GeneratedRegex(
        @"(""token""\s*:\s*"")[^""]*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex TokenJsonRegex();

    [GeneratedRegex(
        @"(authorization:\s*bearer\s+)\S+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex BearerRegex();
}
