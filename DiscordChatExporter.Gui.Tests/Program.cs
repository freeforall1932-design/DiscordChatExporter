using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DiscordChatExporter.Commanding;
using DiscordChatExporter.Core.Discord;
using DiscordChatExporter.Core.Discord.Data;
using DiscordChatExporter.Gui;
using DiscordChatExporter.Gui.Framework;
using DiscordChatExporter.Gui.Localization;
using DiscordChatExporter.Gui.Services;
using DiscordChatExporter.Gui.ViewModels;
using DiscordChatExporter.Gui.ViewModels.Components;
using DiscordChatExporter.Gui.ViewModels.Dialogs;
using DiscordChatExporter.Gui.Views;
using DiscordChatExporter.Gui.Views.Components;
using Microsoft.Extensions.DependencyInjection;

namespace DiscordChatExporter.Gui.Tests;

/// <summary>
/// Offline native smoke/interaction harness. It uses the real app styles, XAML and command
/// classes; no Discord credentials, HTTP requests, or mocked command execution are needed.
/// </summary>
public static class Program
{
    private static int _checks;

    private static void Check(bool condition, string message)
    {
        _checks++;
        if (!condition)
            throw new InvalidOperationException("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseHarfBuzz().UseSkia().UseHeadless(
            new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }
        );

    public static async Task<int> Main(string[] args)
    {
        var output = Path.GetFullPath(args.FirstOrDefault() ?? "desktop-screenshots");
        Directory.CreateDirectory(output);
        var settingsDirectory = Path.Combine(Path.GetTempPath(), "dce-desktop-tests-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(settingsDirectory);
        Environment.SetEnvironmentVariable("DISCORDCHATEXPORTER_SETTINGS_PATH", Path.Combine(settingsDirectory, "Settings.dat"));
        Environment.SetEnvironmentVariable("DISCORDCHATEXPORTER_ALLOW_AUTO_UPDATE", "false");
        Environment.SetEnvironmentVariable("DISCORD_TOKEN", null);
        try
        {
            await TestSharedRunnerAsync();
            using var session = HeadlessUnitTestSession.StartNew(typeof(Program));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            await session.Dispatch(async () =>
            {
                await TestNativeAsync(output);
                return 0;
            }, timeout.Token);
            Console.WriteLine($"NATIVE GUI TEST PASSED: {_checks}/{_checks} checks. Screenshots: {output}");
            await File.WriteAllTextAsync(Path.Combine(output, "proof.txt"),
                $"{_checks}/{_checks} checks passed.\nReal Avalonia controls rendered by the headless Skia platform.\n"
                + "The guide command executed in-process through the shared CliFx runner.\n"
                + "No authenticated Discord exports or real Windows desktop interactions were tested.\n");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            Directory.Delete(settingsDirectory, true);
        }
    }

    private static async Task WaitAsync(Func<bool> condition, string description)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException(description);
            await Task.Delay(20);
        }
    }

    private static async Task TestSharedRunnerAsync()
    {
        var commands = GuiCommandCatalog.GetCommands();
        Check(commands.Select(c => c.Name).SequenceEqual(new[] { "guilds", "channels", "dm", "export", "exportguild", "exportdm", "exportall", "guide" }), "the shared catalog exposes all eight commands, with the guide last");
        Check(!commands.Any(c => c.Name == "gui"), "the host command is not recursively offered");
        foreach (var preset in GuiCommandCatalog.GetPresets())
        {
            var command = GuiCommandCatalog.TryGetCommand(preset.Command);
            Check(command is not null, $"preset '{preset.Id}' targets a real command");
            foreach (var pair in preset.Options)
                Check(command!.Options.Any(o => o.Name == pair.Key), $"preset option '{pair.Key}' exists on '{command.Name}'");
        }

        const string secret = "offline-test-secret-not-a-discord-token";
        foreach (var arguments in new[]
        {
            new[] { "guilds", "--token", secret },
            new[] { "guilds", "-t", secret },
            new[] { "guilds", "--token=" + secret },
            new[] { "guilds", "-t=" + secret },
        })
        {
            Check(!GuiCommandCatalog.FormatCommandLine("DCE", arguments, null).Contains(secret, StringComparison.Ordinal), "explicit raw tokens are masked without relying on the shared token field");
            Check(GuiCommandCatalog.GetTokenValues(arguments).Contains(secret), "explicit token extraction recognizes each spelling");
        }
        Check(GuiCommandCatalog.BuildRawArguments("guide", secret).SequenceEqual(new[] { "guide" }), "the guide never receives an unrelated token option");
        Check(GuiCommandCatalog.BuildRawArguments("guilds -t existing", secret).Count == 3, "a short explicit token is not injected twice");

        using var manager = new GuiRunManager("DCE", "999.9.9", new GuiDebugLog());
        var argumentsSnapshot = new List<string> { "guide" };
        var guide = manager.Start("guide", argumentsSnapshot, secret);
        argumentsSnapshot[0] = "not-a-command";
        await WaitAsync(() => guide.IsFinished, "real guide run did not finish");
        Check(guide.State == GuiRunState.Succeeded && guide.ExitCode == 0, "the real guide command executes successfully from the shared assembly");
        Check(guide.Snapshot(0).Output.Contains("personal account", StringComparison.Ordinal), "real command output is captured, not simulated");
        Check(!guide.CommandLine.Contains(secret, StringComparison.Ordinal), "the command-line preview does not leak a token");
        Check(!guide.TryCancel(), "a late cancel cannot relabel a completed run");

        var activity = manager.BeginActivity("desktop-export", "Native export reservation");
        var busyRejected = false;
        try { manager.Start("guide", ["guide"], null); }
        catch (GuiRunBusyException) { busyRejected = true; }
        Check(busyRejected, "native activity and CLI commands share one execution slot");
        activity.TryCancel();
        manager.CompleteActivity(activity, 1);
        Check(activity.State == GuiRunState.Cancelled, "native cancellation is captured distinctly from failure");
        var error = manager.Start("not-a-command", ["not-a-command", "--token", secret], secret);
        await WaitAsync(() => error.IsFinished, "unknown-command failure did not finish");
        Check(error.State == GuiRunState.Failed, "real parser failures are reported as failures");
        Check(!error.Snapshot(0).Output.Contains(secret, StringComparison.Ordinal), "tokens echoed by parser failures are redacted");

        var bounded = new GuiRun("test", "test");
        bounded.Append(new string('x', 8 * 1024 * 1024));
        var boundedOutput = bounded.Snapshot(0).Output;
        Check(boundedOutput.Length < 4 * 1024 * 1024 + 100, "a single oversized output write is actually bounded");
        Check(boundedOutput.Contains("truncated", StringComparison.Ordinal), "the output cap is disclosed");
        bounded.Complete(0, false);
        bounded.Cancellation.Dispose();
    }

    private static ServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new SettingsService { IsAutoUpdateEnabled = false, IsUkraineSupportMessageEnabled = false, IsTokenPersisted = false });
        services.AddSingleton<DesktopCommandService>();
        services.AddSingleton<DialogManager>();
        services.AddSingleton<SnackbarManager>();
        services.AddSingleton<ViewManager>();
        services.AddSingleton<ViewModelManager>();
        services.AddSingleton<UpdateService>();
        services.AddSingleton<LocalizationManager>();
        services.AddTransient<MainViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<CommandsViewModel>();
        services.AddTransient<ExportSetupViewModel>();
        services.AddTransient<MessageBoxViewModel>();
        services.AddTransient<SettingsViewModel>();
        return services.BuildServiceProvider(true);
    }

    private static async Task CaptureAsync(Window window, string path)
    {
        window.UpdateLayout();
        await Task.Delay(100);
        using var frame = window.CaptureRenderedFrame();
        Check(frame is not null, "the real native window renders a frame");
        frame!.Save(path);
        Check(new FileInfo(path).Length > 1000, "the screenshot contains rendered UI pixels");
    }

    private static async Task TestNativeAsync(string output)
    {
        using var services = CreateServices();
        var model = services.GetRequiredService<MainViewModel>();
        var service = services.GetRequiredService<DesktopCommandService>();
        var commands = model.Commands;
        var window = new MainView { DataContext = model };
        window.Show();
        try
        {
            await Task.Delay(100);
            Check(model.SelectedSectionIndex == 0, "the original Export screen remains the default");
            Check(window.GetVisualDescendants().OfType<DashboardView>().Any(), "the original desktop dashboard still renders");
            await CaptureAsync(window, Path.Combine(output, "desktop-export.png"));

            model.Dashboard.Token = "native-offline-test-token";
            Check(service.Token == model.Dashboard.Token, "the Export token is shared with Commands");
            service.Token = "commands-offline-test-token";
            Check(model.Dashboard.Token == service.Token, "the Commands token is shared back to Export");
            service.SelectedGuildId = "803194314627285022";
            service.SelectedChannelIds = ["803194314627285023", "803194314627285024"];

            model.SelectedSectionIndex = 1;
            await Task.Delay(100);
            var view = window.GetVisualDescendants().OfType<CommandsView>().Single();
            var list = view.FindControl<ListBox>("CommandListBox")!;
            Check(list.Items.Count == 8, "the actual desktop list contains all eight CLI commands");
            Check(list.Bounds.Height > 300, "the command list has room for the flat eight-command layout");
            var presetBox = view.FindControl<ComboBox>("PresetComboBox")!;
            presetBox.SelectedItem = commands.Presets.Single(p => p.Id == "export-everything-json");
            Check(commands.SelectedCommand?.Name == "exportall", "the native preset control selects its real command");
            Check(commands.Options.Single(o => o.Name == "format").SelectedChoice?.Value == "Json", "the JSON preset fills the native format selector");
            Check(commands.Options.Single(o => o.Name == "output").Text == "./exports/", "the preset fills the output path");
            Check(service.Manager.All.Count == 0, "applying a preset does not execute anything");
            Check(commands.CommandPreview.Contains("--format Json", StringComparison.Ordinal), "the native preview reflects preset values");
            Check(!commands.CommandPreview.Contains(service.Token!, StringComparison.Ordinal), "the native preview masks the shared token");

            var outputOption = commands.Options.Single(o => o.Name == "output");
            outputOption.Text = "./changed/";
            Check(commands.SelectedPreset is null, "editing a field clears the matching preset indication");
            outputOption.Text = "./exports/";
            Check(commands.SelectedPreset?.Id == "export-everything-json", "restoring fields restores the matching preset indication");
            await CaptureAsync(window, Path.Combine(output, "desktop-commands.png"));

            presetBox.SelectedItem = commands.Presets.Single(p => p.Id == "export-one-person");
            Check(commands.SelectedCommand?.Name == "exportguild", "the one-person preset selects the server export");
            Check(commands.Options.Single(o => o.Name == "guild").Text == service.SelectedGuildId, "selected server context is prefilled");
            Check(commands.Options.Single(o => o.Name == "filter").Text == "from:", "the one-person filter is prefilled for completion");
            Check(commands.ShowAdvanced && commands.VisibleOptions.Any(o => o.Name == "filter"), "an advanced field filled by a preset is visible");
            commands.SelectedCommand = commands.Commands.Single(c => c.Name == "export");
            Check(commands.Options.Single(o => o.Name == "channel").GetValues().Length == 2, "selected native channels prefill the sequence editor");
            commands.Options.Single(o => o.Name == "channel").Text = string.Empty;
            Check(!commands.RunCommand.CanExecute(null), "Run is disabled when required IDs are missing");

            commands.SelectedCommand = commands.Commands.Single(c => c.Name == "guide");
            Check(commands.RunCommand.CanExecute(null), "the guide can run without required IDs");
            var reservation = service.BeginActivity("desktop-export", "Native export in progress");
            Check(!commands.RunCommand.CanExecute(null), "an original desktop activity disables the Commands Run button");
            Check(!model.Dashboard.IsWorkAllowed, "the original screen also respects the shared busy state");
            Check(commands.CancelCommand.CanExecute(null), "native activities expose cancellation in the Commands workspace");
            commands.CancelCommand.Execute(null);
            Check(reservation.Cancellation.IsCancellationRequested, "Cancel requests real native activity cancellation");
            service.CompleteActivity(reservation, 1);

            var runButton = view.FindControl<Button>("RunButton")!;
            Check(runButton.Command == commands.RunCommand && runButton.IsEnabled, "the actual Run button is wired to the native command");
            window.UpdateLayout();
            var point = runButton.TranslatePoint(new Point(runButton.Bounds.Width / 2, runButton.Bounds.Height / 2), window);
            Check(point is not null, "the Run button has a clickable position");
            window.MouseDown(point!.Value, MouseButton.Left);
            window.MouseUp(point.Value, MouseButton.Left);
            await WaitAsync(() => service.Manager.All.LastOrDefault()?.Command == "guide" && service.Manager.All.Last().IsFinished, "native Run click did not execute the guide");
            service.Refresh();
            Check(commands.SelectedPanelIndex == 1, "running switches to the native Output tab");
            Check(service.StatusText == "Done guide", "the short completion status is shown");
            Check(service.OutputText.Contains("personal account", StringComparison.Ordinal), "the real guide output appears in the native service");
            Check(service.Token == "commands-offline-test-token" && model.Dashboard.Token == service.Token, "do-not-remember persistence does not clear the in-memory shared token");
            var outputBox = view.FindControl<TextBox>("OutputTextBox")!;
            Check(outputBox.Text?.Contains("personal account", StringComparison.Ordinal) == true, "real command output reaches the actual native text control");
            await CaptureAsync(window, Path.Combine(output, "desktop-output.png"));

            commands.SelectedPanelIndex = 2;
            service.DebugLog.Info("test", "Sensitive value: " + service.Token);
            service.Refresh();
            Check(!service.DebugText.Contains(service.Token!, StringComparison.Ordinal), "debug text redacts known tokens even without a --token prefix");
            var debugJson = service.ExportDebugJson();
            Check(!debugJson.Contains(service.Token!, StringComparison.Ordinal), "exported native debug JSON does not contain the token");
            Check(debugJson.Contains("Native desktop", StringComparison.Ordinal), "debug JSON identifies the native interface");
            Check(service.DebugText.Contains("no HTTP server", StringComparison.Ordinal), "the desktop does not pretend to start a browser server");
            await CaptureAsync(window, Path.Combine(output, "desktop-debug.png"));

            commands.IsCustomCommand = true;
            commands.RawCommandLine = "not-a-command";
            commands.RunCommand.Execute(null);
            await WaitAsync(() => service.Manager.All.Last().IsFinished, "native invalid command did not finish");
            service.Refresh();
            Check(service.StatusText.StartsWith("Failed", StringComparison.Ordinal), "native raw-command parser errors show failure");
            Check(!commands.CancelCommand.CanExecute(null), "Cancel is disabled after completion");
            Check(commands.RunCommand.CanExecute(null), "the execution slot is released after failure");
        }
        finally
        {
            window.Close();
        }
    }
}
