using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using CliFx;
using CliFx.Binding;
using CliFx.Infrastructure;
using DiscordChatExporter.Cli.Gui;
using PowerKit.Extensions;

namespace DiscordChatExporter.Cli.Commands;

[Command(
    "gui",
    Description = "Starts a web-based graphical interface for this tool "
        + "(all commands, exposed as buttons)."
)]
public partial class GuiCommand : ICommand
{
    [CommandOption("port", 'p', Description = "Port to listen on.")]
    public int Port { get; set; } = 5000;

    [CommandOption(
        "host",
        Description = "Network interface to bind to. "
            + "Use 'localhost' to only allow connections from this machine, "
            + "or 'any' to also allow connections from the local network."
    )]
    public string Host { get; set; } = "localhost";

    [CommandOption("no-browser", Description = "Don't open the web browser automatically.")]
    public bool IsBrowserDisabled { get; set; }

    [CommandOption(
        "verbose",
        Description = "Print the diagnostic events of the web interface to the console."
    )]
    public bool IsVerbose { get; set; }

    public async ValueTask ExecuteAsync(IConsole console)
    {
        var cancellationToken = console.RegisterCancellationHandler();

        var isNetworkExposed = IsAnyHost(Host);
        var displayUrl = $"http://localhost:{Port}/";

        var executableName = Environment.ProcessPath is { Length: > 0 } processPath
            ? System.IO.Path.GetFileName(processPath)
            : "DiscordChatExporter.Cli";

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown";

        var prefixes = new List<string>();

        if (isNetworkExposed)
        {
            prefixes.Add($"http://+:{Port}/");
        }
        else
        {
            prefixes.Add($"http://localhost:{Port}/");
            prefixes.Add($"http://127.0.0.1:{Port}/");
        }

        var debugLog = new GuiDebugLog { IsVerbose = IsVerbose };
        debugLog.Written += @event =>
            console.Output.WriteLine($"[{@event.Timestamp}] {@event.Level}: {@event.Message}");

        debugLog.Info(
            "server",
            $"Starting '{executableName}' v{version} on {string.Join(", ", prefixes)} "
                + $"from '{System.IO.Directory.GetCurrentDirectory()}'."
        );

        using var runManager = new GuiRunManager(executableName, version, debugLog);
        using var server = new GuiServer(
            new GuiServerOptions
            {
                DisplayUrl = displayUrl,
                Prefixes = prefixes,
                IsNetworkExposed = isNetworkExposed,
                ExecutableName = executableName,
                VersionText = version,
            },
            runManager
        );

        try
        {
            server.Start();
            debugLog.Info("server", "The web server is listening.");
        }
        catch (Exception ex)
        {
            debugLog.Exception("server", ex);

            throw new CommandException(
                $"Failed to start the web server on port {Port}: {ex.Message} "
                    + "Try specifying a different port with '--port <port>'.",
                innerException: ex
            );
        }

        // Banner
        await console.Output.WriteLineAsync();
        await console.Output.WriteLineAsync($"DiscordChatExporter {version}");
        await console.Output.WriteLineAsync($"Web interface: {displayUrl}");
        await console.Output.WriteLineAsync($"Debug information: {displayUrl}api/debug");
        await console.Output.WriteLineAsync();

        if (isNetworkExposed)
        {
            await console.Output.WriteLineAsync(
                "The interface is bound to all network interfaces, so anyone on your network "
                    + "can access it (and the Discord token you enter) while it's running."
            );
            await console.Output.WriteLineAsync();
        }

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISCORD_TOKEN")))
        {
            await console.Output.WriteLineAsync(
                "A token was found in the DISCORD_TOKEN environment variable and will be used "
                    + "unless you provide another one in the interface."
            );
            await console.Output.WriteLineAsync();
        }

        await console.Output.WriteLineAsync(
            "Available commands: "
                + string.Join(" ", GuiCommandCatalog.GetCommands().Select(c => c.Name))
        );
        await console.Output.WriteLineAsync(
            $"Files are exported relative to: {System.IO.Directory.GetCurrentDirectory()}"
        );
        await console.Output.WriteLineAsync(
            "Press Ctrl+C to stop. Add '--no-browser' to skip opening the browser next time."
                + (IsVerbose ? "" : " Add '--verbose' to print diagnostics here.")
        );
        await console.Output.WriteLineAsync();

        if (!IsBrowserDisabled)
            TryOpenBrowser(displayUrl, console);

        // Serve requests until the command is cancelled
        await server.RunAsync(cancellationToken);

        debugLog.Info("server", "The web server was stopped.");
        await console.Output.WriteLineAsync("Web interface stopped.");
    }

    private static bool IsAnyHost(string host) =>
        host.Trim().ToLowerInvariant() switch
        {
            "any" or "all" or "*" or "+" or "0.0.0.0" or "::" => true,
            _ => false,
        };

    private static void TryOpenBrowser(string url, IConsole console)
    {
        try
        {
            Process.StartShellExecute(url);
        }
        catch (Exception ex)
        {
            console.Output.WriteLine(
                $"Failed to open the browser automatically ({ex.Message.TrimEnd('.')}). "
                    + $"Open the link above manually."
            );
            console.Output.WriteLine();
        }
    }
}
