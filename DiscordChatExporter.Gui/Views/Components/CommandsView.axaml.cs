using Avalonia.Input;
using Avalonia.Interactivity;
using DiscordChatExporter.Gui.Framework;
using DiscordChatExporter.Gui.ViewModels.Components;

namespace DiscordChatExporter.Gui.Views.Components;

public partial class CommandsView : UserControl<CommandsViewModel>
{
    public CommandsView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.Enter && (args.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0)
        {
            if (DataContext.RunCommand.CanExecute(null))
                DataContext.RunCommand.Execute(null);
            args.Handled = true;
        }
        else if (args.Key == Key.Escape && DataContext.CancelCommand.CanExecute(null))
        {
            DataContext.CancelCommand.Execute(null);
            args.Handled = true;
        }
    }
}
