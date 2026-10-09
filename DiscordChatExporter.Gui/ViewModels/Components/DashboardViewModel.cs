using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiscordChatExporter.Commanding;
using DiscordChatExporter.Core.Discord;
using DiscordChatExporter.Core.Discord.Data;
using DiscordChatExporter.Core.Exceptions;
using DiscordChatExporter.Core.Exporting;
using DiscordChatExporter.Gui.Framework;
using DiscordChatExporter.Gui.Localization;
using DiscordChatExporter.Gui.Models;
using DiscordChatExporter.Gui.Services;
using DiscordChatExporter.Gui.Views;
using Gress;
using Gress.Completable;
using PowerKit;
using PowerKit.Extensions;

namespace DiscordChatExporter.Gui.ViewModels.Components;

public partial class DashboardViewModel : ViewModelBase
{
    private readonly ViewModelManager _viewModelManager;
    private readonly SnackbarManager _snackbarManager;
    private readonly DialogManager _dialogManager;
    private readonly SettingsService _settingsService;
    private readonly DesktopCommandService _commands;
    private readonly CommandsWindowService _commandsWindow;
    private string? _loadedToken;

    private readonly IDisposable _eventSubscription;
    private readonly AutoResetProgressMuxer _progressMuxer;

    private DiscordClient? _discord;

    public DashboardViewModel(
        ViewModelManager viewModelManager,
        DialogManager dialogManager,
        SnackbarManager snackbarManager,
        SettingsService settingsService,
        DesktopCommandService commands,
        CommandsWindowService commandsWindow,
        LocalizationManager localizationManager
    )
    {
        _viewModelManager = viewModelManager;
        _dialogManager = dialogManager;
        _snackbarManager = snackbarManager;
        _settingsService = settingsService;
        _commands = commands;
        _commandsWindow = commandsWindow;
        LocalizationManager = localizationManager;

        _progressMuxer = Progress.CreateMuxer().WithAutoReset();

        _eventSubscription = Disposable.Merge(
            Progress.WatchProperty(
                o => o.Current,
                _ => OnPropertyChanged(nameof(IsProgressIndeterminate))
            ),
            SelectedChannels.WatchProperty(
                o => o.Count,
                _ =>
                {
                    _commands.SelectedChannelIds = SelectedChannels
                        .Select(c => c.Channel.Id.ToString())
                        .ToArray();
                    ExportCommand.NotifyCanExecuteChanged();
                }
            ),
            commands.WatchProperty(s => s.Token, value => Token = value),
            commands.WatchProperty(
                s => s.IsBusy,
                _ =>
                {
                    OnPropertyChanged(nameof(IsWorkAllowed));
                    PullGuildsCommand.NotifyCanExecuteChanged();
                    PullChannelsCommand.NotifyCanExecuteChanged();
                    ExportCommand.NotifyCanExecuteChanged();
                }
            )
        );
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProgressIndeterminate))]
    [NotifyPropertyChangedFor(nameof(IsWorkAllowed))]
    [NotifyCanExecuteChangedFor(nameof(PullGuildsCommand))]
    [NotifyCanExecuteChangedFor(nameof(PullChannelsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    public partial bool IsBusy { get; set; }

    public bool IsWorkAllowed => !IsBusy && !_commands.IsBusy;

    public LocalizationManager LocalizationManager { get; }

    public ProgressContainer<Percentage> Progress { get; } = new();

    public bool IsProgressIndeterminate => IsBusy && Progress.Current.Fraction is <= 0 or >= 1;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PullGuildsCommand))]
    public partial string? Token { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<Guild>? AvailableGuilds { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PullChannelsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    public partial Guild? SelectedGuild { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<ChannelConnection>? AvailableChannels { get; set; }

    public ObservableCollection<ChannelConnection> SelectedChannels { get; } = [];

    partial void OnTokenChanged(string? value)
    {
        _commands.Token = value;
        // A token edited in either section must not leave channels authenticated with
        // the previous account available for export.
        if (_discord is not null && _loadedToken != value?.Trim('"', ' '))
        {
            _discord = null;
            AvailableGuilds = null;
            SelectedGuild = null;
            AvailableChannels = null;
            SelectedChannels.Clear();
            ExportCommand.NotifyCanExecuteChanged();
        }
    }

    partial void OnSelectedGuildChanged(Guild? value) =>
        _commands.SelectedGuildId = value is { IsDirect: false } ? value.Id.ToString() : null;

    public override Task InitializeAsync()
    {
        Token = _commands.Token;
        return Task.CompletedTask;
    }

    public CommandsWindow OpenCommands(Window owner) => _commandsWindow.Open(owner);

    [RelayCommand]
    private async Task ShowSettingsAsync() =>
        await _dialogManager.ShowDialogAsync(_viewModelManager.GetSettingsViewModel());

    private bool CanPullGuilds() => IsWorkAllowed && !string.IsNullOrWhiteSpace(Token);

    [RelayCommand(CanExecute = nameof(CanPullGuilds))]
    private async Task PullGuildsAsync()
    {
        IsBusy = true;
        var progress = _progressMuxer.CreateInput();
        GuiRun? activity = null;
        var exitCode = 1;
        try
        {
            var token = Token?.Trim('"', ' ');
            if (string.IsNullOrWhiteSpace(token))
                return;
            activity = _commands.BeginActivity("desktop-list", "Load servers and channels");
            AvailableGuilds = null;
            SelectedGuild = null;
            AvailableChannels = null;
            SelectedChannels.Clear();
            _loadedToken = token;
            _discord = new DiscordClient(token, _settingsService.RateLimitPreference);
            _settingsService.LastToken = token;
            var guilds = await _discord.GetUserGuildsAsync(activity.Cancellation.Token);
            AvailableGuilds = guilds;
            SelectedGuild = guilds.FirstOrDefault();
            activity.Append($"Fetched {guilds.Count} server(s), including direct messages.\n");
            await PullChannelsCoreAsync(activity.Cancellation.Token);
            exitCode = 0;
        }
        catch (OperationCanceledException)
        {
            activity?.Append("Cancelled.\n");
            _snackbarManager.Notify("Cancelled");
        }
        catch (DiscordChatExporterException ex) when (!ex.IsFatal)
        {
            activity?.Append(ex + "\n");
            _snackbarManager.Notify(_commands.DebugLog.Redact(ex.Message.TrimEnd('.')));
        }
        catch (Exception ex)
        {
            activity?.Append(ex + "\n");
            _commands.DebugLog.Exception("desktop", ex);
            await _dialogManager.ShowDialogAsync(
                _viewModelManager.GetMessageBoxViewModel(
                    LocalizationManager.ErrorPullingGuildsTitle,
                    _commands.DebugLog.Redact(ex.ToString())
                )
            );
        }
        finally
        {
            if (activity is not null)
                _commands.CompleteActivity(activity, exitCode);
            progress.ReportCompletion();
            IsBusy = false;
        }
    }

    private bool CanPullChannels() =>
        IsWorkAllowed && _discord is not null && SelectedGuild is not null;

    [RelayCommand(CanExecute = nameof(CanPullChannels))]
    private async Task PullChannelsAsync()
    {
        IsBusy = true;
        var progress = _progressMuxer.CreateInput();
        GuiRun? activity = null;
        var exitCode = 1;
        try
        {
            if (_discord is null || SelectedGuild is null)
                return;
            activity = _commands.BeginActivity(
                "desktop-list",
                $"Load channels in {SelectedGuild.Name}"
            );
            await PullChannelsCoreAsync(activity.Cancellation.Token);
            activity.Append($"Fetched channels in {SelectedGuild.Name}.\n");
            exitCode = 0;
        }
        catch (OperationCanceledException)
        {
            activity?.Append("Cancelled.\n");
            _snackbarManager.Notify("Cancelled");
        }
        catch (DiscordChatExporterException ex) when (!ex.IsFatal)
        {
            activity?.Append(ex + "\n");
            _snackbarManager.Notify(_commands.DebugLog.Redact(ex.Message.TrimEnd('.')));
        }
        catch (Exception ex)
        {
            activity?.Append(ex + "\n");
            _commands.DebugLog.Exception("desktop", ex);
            await _dialogManager.ShowDialogAsync(
                _viewModelManager.GetMessageBoxViewModel(
                    LocalizationManager.ErrorPullingChannelsTitle,
                    _commands.DebugLog.Redact(ex.ToString())
                )
            );
        }
        finally
        {
            if (activity is not null)
                _commands.CompleteActivity(activity, exitCode);
            progress.ReportCompletion();
            IsBusy = false;
        }
    }

    private async Task PullChannelsCoreAsync(CancellationToken cancellationToken)
    {
        if (_discord is null || SelectedGuild is null)
            return;
        AvailableChannels = null;
        SelectedChannels.Clear();
        var channels = new List<Channel>();
        await foreach (
            var channel in _discord.GetGuildChannelsAsync(SelectedGuild.Id, cancellationToken)
        )
            channels.Add(channel);
        if (_settingsService.ThreadInclusionMode != ThreadInclusionMode.None)
        {
            await foreach (
                var thread in _discord.GetGuildThreadsAsync(
                    SelectedGuild.Id,
                    _settingsService.ThreadInclusionMode == ThreadInclusionMode.All,
                    cancellationToken: cancellationToken
                )
            )
                channels.Add(thread);
        }
        AvailableChannels = ChannelConnection.BuildTree(
            channels
                .OrderByDescending(c => c.IsDirect ? c.LastMessageId : null)
                .ThenBy(c => c.Position)
                .ToArray()
        );
        SelectedChannels.Clear();
    }

    private bool CanExport() =>
        IsWorkAllowed
        && _discord is not null
        && SelectedGuild is not null
        && SelectedChannels.Any();

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        IsBusy = true;
        GuiRun? activity = null;
        var exitCode = 1;

        try
        {
            if (_discord is null || SelectedGuild is null || !SelectedChannels.Any())
                return;

            var dialog = _viewModelManager.GetExportSetupViewModel(
                SelectedGuild,
                SelectedChannels.Select(c => c.Channel).ToArray()
            );

            if (await _dialogManager.ShowDialogAsync(dialog) != true)
                return;

            activity = _commands.BeginActivity(
                "desktop-export",
                $"Export {dialog.Channels!.Count} selected channel(s)"
            );
            activity.Append(
                $"Exporting {dialog.Channels.Count} channel(s) as {dialog.SelectedFormat}.\n"
            );
            var exporter = new ChannelExporter(_discord);

            var channelProgressPairs = dialog
                .Channels!.Select(c => new { Channel = c, Progress = _progressMuxer.CreateInput() })
                .ToArray();

            var successfulExportCount = 0;
            var completedExportCount = 0;
            var failedExportCount = 0;

            await Parallel.ForEachAsync(
                channelProgressPairs,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Max(1, _settingsService.ParallelLimit),
                    CancellationToken = activity.Cancellation.Token,
                },
                async (pair, cancellationToken) =>
                {
                    var channel = pair.Channel;
                    var progress = pair.Progress;

                    try
                    {
                        var request = new ExportRequest(
                            dialog.Guild!,
                            channel,
                            dialog.OutputPath!,
                            dialog.AssetsDirPath,
                            dialog.SelectedFormat,
                            dialog.After?.Pipe(Snowflake.FromDate),
                            dialog.Before?.Pipe(Snowflake.FromDate),
                            dialog.PartitionLimit,
                            dialog.MessageFilter,
                            dialog.IsReverseMessageOrder,
                            dialog.ShouldFormatMarkdown,
                            dialog.ShouldDownloadAssets,
                            dialog.ShouldReuseAssets,
                            _settingsService.Locale,
                            _settingsService.IsUtcNormalizationEnabled
                        );

                        await exporter.ExportChannelAsync(request, progress, cancellationToken);

                        Interlocked.Increment(ref successfulExportCount);
                        activity.Append($"Exported {channel.GetHierarchicalName()}.\n");
                    }
                    catch (ChannelEmptyException ex)
                    {
                        activity.Append($"{channel.Name}: {ex.Message}\n");
                        _snackbarManager.Notify(_commands.DebugLog.Redact(ex.Message.TrimEnd('.')));
                    }
                    catch (DiscordChatExporterException ex) when (!ex.IsFatal)
                    {
                        Interlocked.Increment(ref failedExportCount);
                        activity.Append($"{channel.Name}: {ex.Message}\n");
                        _snackbarManager.Notify(_commands.DebugLog.Redact(ex.Message.TrimEnd('.')));
                    }
                    finally
                    {
                        progress.ReportCompletion();
                        activity.ReportProgress(
                            Interlocked.Increment(ref completedExportCount)
                                * 100
                                / channelProgressPairs.Length
                        );
                    }
                }
            );

            exitCode = failedExportCount == 0 ? 0 : 1;
            activity.Append(
                $"Done: {successfulExportCount} exported, {failedExportCount} failed.\n"
            );

            // Notify of the overall completion
            if (successfulExportCount > 0)
            {
                _snackbarManager.Notify(
                    string.Format(
                        LocalizationManager.SuccessfulExportMessage,
                        successfulExportCount
                    )
                );
            }
        }
        catch (OperationCanceledException)
        {
            activity?.Append("Cancelled.\n");
            _snackbarManager.Notify("Cancelled");
        }
        catch (Exception ex)
        {
            activity?.Append(ex + "\n");
            _commands.DebugLog.Exception("desktop", ex);
            var dialog = _viewModelManager.GetMessageBoxViewModel(
                LocalizationManager.ErrorExportingTitle,
                _commands.DebugLog.Redact(ex.ToString())
            );

            await _dialogManager.ShowDialogAsync(dialog);
        }
        finally
        {
            if (activity is not null)
                _commands.CompleteActivity(activity, exitCode);
            IsBusy = false;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _eventSubscription.Dispose();
        }

        base.Dispose(disposing);
    }
}
