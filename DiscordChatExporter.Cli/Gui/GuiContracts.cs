using System.Collections.Generic;

namespace DiscordChatExporter.Cli.Gui;

// Requests sent by the browser

internal sealed class GuiRunRequest
{
    public string? Command { get; set; }

    public Dictionary<string, string[]>? Options { get; set; }

    public string? RawCommandLine { get; set; }

    public string? Token { get; set; }
}

// Responses sent back to the browser

internal sealed record GuiInfoDto(
    string Name,
    string Version,
    string ExecutableName,
    string WorkingDirectory,
    string StartedAt,
    bool IsNetworkExposed,
    bool HasEnvironmentToken,
    IReadOnlyList<GuiCommandDto> Commands
);

internal sealed record GuiCommandDto(
    string Name,
    string Title,
    string? Description,
    string Group,
    string Icon,
    bool RequiresToken,
    int Order,
    IReadOnlyList<GuiOptionDto> Options
);

internal sealed record GuiOptionDto(
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

internal sealed record GuiOptionChoiceDto(string Value, string Label);

internal sealed record GuiRunDto(
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

internal sealed record GuiCurrentRunDto(GuiRunDto? Run);

internal sealed record GuiEnvironmentDto(
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

internal sealed record GuiDebugDto(
    GuiEnvironmentDto Environment,
    IReadOnlyList<GuiDebugEventDto> Events,
    IReadOnlyList<GuiRunSummaryDto> Runs,
    int RequestCount
);

internal sealed record GuiErrorDto(string Message, IReadOnlyList<string>? Details = null);
