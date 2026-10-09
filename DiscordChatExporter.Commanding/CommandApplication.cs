using CliFx;

namespace DiscordChatExporter.Commanding;

/// <summary>
/// Registers the real export, discovery and guide commands once, for the CLI and both GUIs.
/// Each host can add its own commands (such as the CLI's web-server command) afterwards.
/// </summary>
public static class CommandApplication
{
    public static CommandLineApplicationBuilder CreateBuilder() =>
        new CommandLineApplicationBuilder().AddCommandsFromThisAssembly();
}
