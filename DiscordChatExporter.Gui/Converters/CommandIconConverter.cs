using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Material.Icons;

namespace DiscordChatExporter.Gui.Converters;

/// <summary>Maps the shared, presentation-neutral icon names to the desktop's existing icon set.</summary>
public sealed class CommandIconConverter : IValueConverter
{
    public static CommandIconConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            "server" => MaterialIconKind.Server,
            "hash" => MaterialIconKind.Pound,
            "chat" => MaterialIconKind.MessageText,
            "download" => MaterialIconKind.Download,
            "globe" => MaterialIconKind.Earth,
            "help" => MaterialIconKind.HelpCircleOutline,
            "filter" => MaterialIconKind.FilterOutline,
            _ => MaterialIconKind.Console,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
