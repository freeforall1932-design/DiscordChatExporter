using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiscordChatExporter.Commanding;
using DiscordChatExporter.Gui.Framework;
using DiscordChatExporter.Gui.Services;
using DiscordChatExporter.Gui.Utils.Extensions;

namespace DiscordChatExporter.Gui.ViewModels.Components;

/// <summary>Native form/preset bindings over the shared command catalog and real runner.</summary>
public sealed partial class CommandsViewModel : ViewModelBase
{
    private readonly DialogManager _dialogs;
    private readonly ViewModelManager _viewModels;
    private readonly Dictionary<string, Dictionary<string, string[]>> _savedValues = new();
    private bool _building;
    private bool _matchingPreset;
    private GuiPresetDto? _lastPreset;
    private Dictionary<string, string[]>? _presetSnapshot;

    public CommandsViewModel(DesktopCommandService service, DialogManager dialogs, ViewModelManager viewModels)
    {
        Service = service;
        _dialogs = dialogs;
        _viewModels = viewModels;
        Service.PropertyChanged += OnServiceChanged;
        SelectedCommand = Commands.First();
    }

    public DesktopCommandService Service { get; }
    public IReadOnlyList<GuiCommandDto> Commands { get; } = GuiCommandCatalog.GetCommands();
    public IReadOnlyList<GuiPresetDto> Presets { get; } = GuiCommandCatalog.GetPresets();
    public ObservableCollection<CommandOptionViewModel> Options { get; } = [];
    public IEnumerable<CommandOptionViewModel> VisibleOptions => Options.Where(o => ShowAdvanced || !o.IsAdvanced);
    public bool HasOptions => Options.Count > 0;
    public bool NeedsToken => SelectedCommand?.RequiresToken == true && !IsCustomCommand;

    [ObservableProperty]
    public partial GuiCommandDto? SelectedCommand { get; set; }

    [ObservableProperty]
    public partial GuiPresetDto? SelectedPreset { get; set; }

    [ObservableProperty]
    public partial bool ShowAdvanced { get; set; }

    [ObservableProperty]
    public partial bool IsCustomCommand { get; set; }

    [ObservableProperty]
    public partial string RawCommandLine { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int SelectedPanelIndex { get; set; }

    [ObservableProperty]
    public partial string CommandPreview { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string ValidationMessage { get; private set; } = string.Empty;

    private bool _formValid;

    partial void OnSelectedCommandChanged(GuiCommandDto? value)
    {
        if (value is null)
            return;
        _lastPreset = null;
        _presetSnapshot = null;
        SelectedPreset = null;
        IsCustomCommand = false;
        BuildForm(value, _savedValues.GetValueOrDefault(value.Name));
        SelectedPanelIndex = 0;
        OnPropertyChanged(nameof(NeedsToken));
    }

    partial void OnShowAdvancedChanged(bool value) => OnPropertyChanged(nameof(VisibleOptions));
    partial void OnIsCustomCommandChanged(bool value)
    {
        OnPropertyChanged(nameof(NeedsToken));
        RefreshForm();
    }
    partial void OnRawCommandLineChanged(string value) => RefreshForm();

    private void BuildForm(GuiCommandDto command, IReadOnlyDictionary<string, string[]>? values)
    {
        _building = true;
        try
        {
            Options.Clear();
            foreach (var option in command.Options)
            {
                var initial = GetDefaultValues(option);
                if (values is not null && values.TryGetValue(option.Name, out var saved))
                    initial = saved;
                else if (option.Name == "guild" && Service.SelectedGuildId is { } guildId)
                    initial = [guildId];
                else if (option.Name == "channel" && Service.SelectedChannelIds.Count > 0)
                    initial = Service.SelectedChannelIds.ToArray();
                Options.Add(new CommandOptionViewModel(option, initial, _dialogs, OnOptionsChanged));
            }
        }
        finally
        {
            _building = false;
        }
        OnPropertyChanged(nameof(VisibleOptions));
        OnPropertyChanged(nameof(HasOptions));
        RefreshForm();
    }

    private static string[] GetDefaultValues(GuiOptionDto option) =>
        option.DefaultValue is { } value ? [value] : option.Kind == "bool" ? ["false"] : [];

    public Dictionary<string, string[]> CollectOptions() =>
        Options.ToDictionary(o => o.Name, o => o.GetValues(), StringComparer.OrdinalIgnoreCase);

    private void OnOptionsChanged()
    {
        if (_building)
            return;
        if (SelectedCommand is { } command)
            _savedValues[command.Name] = CollectOptions();
        if (_lastPreset is not null && _presetSnapshot is not null)
        {
            var values = CollectOptions();
            var matches = _presetSnapshot.All(p => values.TryGetValue(p.Key, out var current) && current.SequenceEqual(p.Value));
            _matchingPreset = true;
            SelectedPreset = matches ? _lastPreset : null;
            _matchingPreset = false;
        }
        RefreshForm();
    }

    partial void OnSelectedPresetChanged(GuiPresetDto? value)
    {
        if (value is null || _matchingPreset)
            return;
        ApplyPreset(value);
    }

    public void ApplyPreset(GuiPresetDto preset)
    {
        var command = GuiCommandCatalog.TryGetCommand(preset.Command)
            ?? throw new ArgumentException("This preset no longer targets an available command.");
        SelectedCommand = command;
        IsCustomCommand = false;
        var values = command.Options.ToDictionary(o => o.Name, GetDefaultValues, StringComparer.OrdinalIgnoreCase);
        if (values.ContainsKey("guild") && Service.SelectedGuildId is { } guildId)
            values["guild"] = [guildId];
        if (values.ContainsKey("channel") && Service.SelectedChannelIds.Count > 0)
            values["channel"] = Service.SelectedChannelIds.ToArray();
        foreach (var pair in preset.Options)
            values[pair.Key] = pair.Value.ToArray();
        BuildForm(command, values);
        if (command.Options.Any(o => o.IsAdvanced && preset.Options.ContainsKey(o.Name)))
            ShowAdvanced = true;
        _lastPreset = preset;
        _presetSnapshot = CollectOptions();
        _matchingPreset = true;
        SelectedPreset = preset;
        _matchingPreset = false;
        SelectedPanelIndex = 0;
    }

    private void RefreshForm()
    {
        if (_building)
            return;
        try
        {
            if (IsCustomCommand)
            {
                var arguments = GuiCommandCatalog.BuildRawArguments(RawCommandLine, Service.Token?.Trim('"', ' '));
                CommandPreview = GuiCommandCatalog.FormatCommandLine("DiscordChatExporter.Cli", arguments, Service.Token);
                ValidationMessage = string.Empty;
                _formValid = true;
            }
            else if (SelectedCommand is { } command)
            {
                var values = CollectOptions();
                var token = Service.Token?.Trim('"', ' ');
                CommandPreview = GuiCommandCatalog.FormatCommandLine("DiscordChatExporter.Cli", GuiCommandCatalog.BuildArguments(command, token, values), token);
                var errors = GuiCommandCatalog.Validate(command, token, values, Service.HasEnvironmentToken);
                ValidationMessage = string.Join(" ", errors);
                _formValid = errors.Count == 0;
            }
            else
            {
                _formValid = false;
                ValidationMessage = "Select a command.";
            }
        }
        catch (ArgumentException ex)
        {
            _formValid = false;
            CommandPreview = string.Empty;
            ValidationMessage = ex.Message;
        }
        RunCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    private void OnServiceChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(DesktopCommandService.Token) or nameof(DesktopCommandService.IsBusy))
            RefreshForm();
    }

    private bool CanRun() => _formValid && !Service.IsBusy;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private void Run()
    {
        try
        {
            if (IsCustomCommand)
                Service.StartRaw(RawCommandLine);
            else if (SelectedCommand is { } command)
                Service.Start(command, CollectOptions());
            SelectedPanelIndex = 1;
        }
        catch (Exception ex)
        {
            ValidationMessage = Service.DebugLog.Redact(ex.Message);
            Service.DebugLog.Exception("desktop", ex);
        }
    }

    private bool CanCancel() => Service.IsBusy;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => Service.Cancel();

    [RelayCommand]
    private async Task SaveOutputAsync()
    {
        var path = await _dialogs.PromptSaveFilePathAsync(
            [new FilePickerFileType("Text log") { Patterns = ["*.txt"] }], "DiscordChatExporter-output.txt"
        );
        if (!string.IsNullOrWhiteSpace(path))
            await File.WriteAllTextAsync(path, Service.OutputText);
    }

    [RelayCommand]
    private async Task SaveDebugAsync()
    {
        var path = await _dialogs.PromptSaveFilePathAsync(
            [new FilePickerFileType("Debug JSON") { Patterns = ["*.json"] }], "DiscordChatExporter-debug.json"
        );
        if (!string.IsNullOrWhiteSpace(path))
            await File.WriteAllTextAsync(path, Service.ExportDebugJson());
    }

    [RelayCommand]
    private async Task CopyCommandAsync()
    {
        var topLevel = Application.Current?.ApplicationLifetime?.TryGetTopLevel();
        if (topLevel?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(CommandPreview);
    }

    [RelayCommand]
    private async Task ShowSettingsAsync() => await _dialogs.ShowDialogAsync(_viewModels.GetSettingsViewModel());

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Service.PropertyChanged -= OnServiceChanged;
            foreach (var option in Options)
                option.Dispose();
        }
        base.Dispose(disposing);
    }
}
