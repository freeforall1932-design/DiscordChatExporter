using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using DiscordChatExporter.Core.Discord;
using DiscordChatExporter.Core.Discord.Data;
using DiscordChatExporter.Gui.ViewModels.Components;
using DiscordChatExporter.Gui.Views.Components;
using Ellipse = Avalonia.Controls.Shapes.Ellipse;

namespace DiscordChatExporter.Gui.Tests;

public static partial class Program
{
    /// <summary>
    /// Offline, explicitly labelled illustration of a populated dashboard. No real account
    /// data is fetched. The only client is a dummy used to exercise existing UI enablement;
    /// it is never asked to make a request and is removed before interaction tests resume.
    /// </summary>
    private static async Task CapturePopulatedExportAsync(
        Window window,
        DashboardViewModel dashboard,
        string output
    )
    {
        const string avatarRoot = "avares://DiscordChatExporter.Gui.Tests/Assets/";
        var guild = new Guild(
            new Snowflake(803194314627285022),
            "Development (sample)",
            avatarRoot + "sample-avatar-1.png"
        );
        var general = new Channel(
            new Snowflake(803194314627285101),
            ChannelKind.GuildCategory,
            guild.Id,
            null,
            "general",
            0,
            null,
            null,
            false,
            null
        );
        var development = new Channel(
            new Snowflake(803194314627285102),
            ChannelKind.GuildCategory,
            guild.Id,
            null,
            "development",
            1,
            null,
            null,
            false,
            null
        );
        var names = new[]
        {
            "architecture",
            "devops-and-tools",
            "security",
            "discord-dev",
            "web",
            "gui",
            "mobile",
            "game-dev",
            "databases",
            "roslyn",
            "all-you-can-visual-basic",
        };
        var channels = names
            .Select(
                (name, i) =>
                    new Channel(
                        new Snowflake(803194314627285200UL + (ulong)i),
                        ChannelKind.GuildTextChat,
                        guild.Id,
                        development,
                        name,
                        i,
                        null,
                        "Offline sample channel",
                        false,
                        new Snowflake(803194314627285999)
                    )
            )
            .ToArray();
        var discordField = typeof(DashboardViewModel).GetField(
            "_discord",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        var tokenField = typeof(DashboardViewModel).GetField(
            "_loadedToken",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        Panel? previewPanel = null;
        Border? sampleBadge = null;
        try
        {
            // Flush selection/layout events while commands are disabled, so merely showing
            // fixture guilds cannot trigger the normal PullChannels command.
            dashboard.IsBusy = true;
            dashboard.AvailableGuilds =
            [
                new Guild(
                    Snowflake.Zero,
                    "Direct messages (sample)",
                    avatarRoot + "sample-avatar-0.png"
                ),
                guild,
                new Guild(
                    new Snowflake(803194314627285025),
                    "Design (sample)",
                    avatarRoot + "sample-avatar-2.png"
                ),
                new Guild(
                    new Snowflake(803194314627285026),
                    "Friends (sample)",
                    avatarRoot + "sample-avatar-3.png"
                ),
            ];
            dashboard.SelectedGuild = guild;
            dashboard.AvailableChannels = ChannelConnection.BuildTree([
                general,
                development,
                .. channels,
            ]);
            window.UpdateLayout();
            await Task.Delay(100);
            var view = window.GetVisualDescendants().OfType<DashboardView>().Single();
            var tree = view.FindControl<TreeView>("AvailableChannelsTreeView")!;
            foreach (var root in dashboard.AvailableChannels)
            {
                if (tree.TreeContainerFromItem(root) is TreeViewItem container)
                    container.IsExpanded = true;
            }
            window.UpdateLayout();
            foreach (var channel in channels.Where((_, i) => i is 0 or 3 or 6 or 7))
                dashboard.SelectedChannels.Add(new ChannelConnection(channel, []));
            await Task.Delay(100);
            tokenField.SetValue(dashboard, dashboard.Token?.Trim('"', ' '));
            discordField.SetValue(
                dashboard,
                new DiscordClient(dashboard.Token!, RateLimitPreference.RespectAll)
            );
            dashboard.IsBusy = false;
            window.UpdateLayout();
            await WaitAsync(
                () =>
                    view.GetVisualDescendants()
                        .OfType<Ellipse>()
                        .Count(e => e.Fill is ImageBrush { Source: not null }) >= 4,
                "the local fixture avatars did not load into the original server rail"
            );
            Check(
                dashboard.AvailableGuilds.Count == 4,
                "the preview illustrates a populated four-server rail"
            );
            Check(tree.Items.Count == 2, "the original category/channel hierarchy remains visible");
            Check(
                dashboard.SelectedChannels.Count == 4,
                "the original multi-channel selection is illustrated"
            );
            var export = view.FindControl<Button>("ExportButton")!;
            Check(
                export.IsVisible && export.IsEnabled,
                "the original amber floating export action is retained"
            );
            Check(
                export.Background is ISolidColorBrush brush
                    && brush.Color == Color.Parse("#F9A825"),
                "native preview applies the actual desktop amber palette, not the placeholder theme"
            );

            // Test-only annotation, not a production demo mode. Everything underneath is
            // the actual DashboardView. The token is a known dummy, never a credential.
            // Overlay the annotation in the existing dashboard panel. Do not detach and
            // reparent the DialogHost/tree: that disrupts headless TreeView subscriptions.
            previewPanel = ((DockPanel)view.Content!)
                .Children.OfType<Panel>()
                .First(panel => panel is not StackPanel);
            sampleBadge = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(10),
                Padding = new Thickness(9, 5),
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.Parse("#FFF4DA")),
                Child = new TextBlock
                {
                    Text = "SAMPLE DATA · OFFLINE PREVIEW",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.Parse("#715313")),
                },
            };
            previewPanel.Children.Add(sampleBadge);
            await CaptureAsync(window, Path.Combine(output, "desktop-export.png"));
        }
        finally
        {
            dashboard.IsBusy = true;
            discordField.SetValue(dashboard, null);
            tokenField.SetValue(dashboard, null);
            if (sampleBadge is not null)
                previewPanel?.Children.Remove(sampleBadge);
            dashboard.IsBusy = false;
        }
    }
}
