using System.Text.Json.Serialization;
using DiscordChatExporter.Commanding;

namespace DiscordChatExporter.Gui.Services;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true
)]
[JsonSerializable(typeof(GuiDebugDto))]
internal partial class DesktopJsonContext : JsonSerializerContext;
