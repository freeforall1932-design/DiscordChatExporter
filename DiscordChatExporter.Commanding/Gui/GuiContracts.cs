using System.Collections.Generic;

namespace DiscordChatExporter.Commanding;

// Requests sent by the browser

public sealed class GuiRunRequest
{
    public string? Command { get; set; }

    public Dictionary<string, string[]>? Options { get; set; }

    public string? RawCommandLine { get; set; }

    public string? Token { get; set; }
}

// Responses sent back to the browser

public sealed record GuiInfoDto(
    string Name,
    string Version,
    string ExecutableName,
    string WorkingDirectory,
    string StartedAt,
    bool IsNetworkExposed,
    bool HasEnvironmentToken,
    IReadOnlyList<GuiCommandDto> Commands,
    IReadOnlyList<GuiPresetDto> Presets
);

public sealed record GuiCommandDto(
    string Name,
    string Title,
    string? Description,
    string Icon,
    bool RequiresToken,
    int Order,
    IReadOnlyList<GuiOptionDto> Options
);

/// <summary>
/// A ready-made combination of a command and its options, shown as a quick-start button.
/// </summary>
public sealed record GuiPresetDto(
    string Id,
    string Title,
    string? Description,
    string Icon,
    string Command,
    IReadOnlyDictionary<string, string[]> Options
);

public sealed record GuiOptionDto(
    string Name,
    string? ShortName,
    string Label,
    string? Description,
    string Kind,
    bool IsRequired,
    bool IsSequence,
    bool HasDefault,
    string? DefaultValue,
    bool RequiresToken,
    IReadOnlyList<GuiOptionChoiceDto>? Choices,
    string? Placeholder,
    bool IsAdvanced
);

public sealed record GuiOptionChoiceDto(string Value, string Label);

public sealed record GuiRunDto(
    string Id,
    string Command,
    string CommandLine,
    string State,
    int? ExitCode,
    string StartedAt,
    string? FinishedAt,
    int Cursor,
    string Output,
    int? Progress
);

public sealed record GuiCurrentRunDto(GuiRunDto? Run);

public sealed record GuiRunSummaryDto(
    string Id,
    string Command,
    string State,
    int? ExitCode,
    string StartedAt
);

public sealed record GuiEnvironmentDto(
    string Name,
    string Version,
    string ExecutableName,
    string WorkingDirectory,
    string ServerUrl,
    bool IsNetworkExposed,
    bool HasEnvironmentToken,
    string StartedAt,
    string Runtime,
    int ProcessId
);

public sealed record GuiDebugDto(
    GuiEnvironmentDto Environment,
    IReadOnlyList<GuiDebugEventDto> Events,
    IReadOnlyList<GuiRunSummaryDto> Runs,
    int RequestCount
);

public sealed record GuiErrorDto(string Message, IReadOnlyList<string>? Details = null);
