using System.Text.Json.Serialization;
using DiscordChatExporter.Commanding;

namespace DiscordChatExporter.Cli.Gui;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
[JsonSerializable(typeof(GuiRunRequest))]
[JsonSerializable(typeof(GuiInfoDto))]
[JsonSerializable(typeof(GuiCommandDto))]
[JsonSerializable(typeof(GuiRunDto))]
[JsonSerializable(typeof(GuiCurrentRunDto))]
[JsonSerializable(typeof(GuiErrorDto))]
[JsonSerializable(typeof(GuiDebugDto))]
[JsonSerializable(typeof(GuiRunSummaryDto))]
[JsonSerializable(typeof(GuiPresetDto))]
[JsonSerializable(typeof(GuiRunDto[]))]
internal partial class GuiJsonContext : JsonSerializerContext;
