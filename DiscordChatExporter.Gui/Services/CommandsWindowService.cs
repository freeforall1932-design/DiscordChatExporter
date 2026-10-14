using System;
using Avalonia.Controls;
using DiscordChatExporter.Gui.Framework;
using DiscordChatExporter.Gui.Views;

namespace DiscordChatExporter.Gui.Services;

/// <summary>
/// Keeps advanced tools optional, without resizing/replacing the familiar export window.
/// Closing this window hides the tools; it does not cancel a run or reset its form/history.
/// </summary>
public sealed class CommandsWindowService(ViewModelManager viewModels) : IDisposable
{
    private CommandsWindow? _window;

    public CommandsWindow? Current => _window;

    public CommandsWindow Open(Window owner)
    {
        if (_window is { IsVisible: true })
        {
            if (_window.WindowState == WindowState.Minimized)
                _window.WindowState = WindowState.Normal;
            _window.Activate();
            return _window;
        }
        var window = new CommandsWindow { DataContext = viewModels.GetCommandsViewModel() };
        _window = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_window, window))
                _window = null;
        };
        window.Show(owner);
        return window;
    }

    public void Dispose() => _window?.Close();
}
