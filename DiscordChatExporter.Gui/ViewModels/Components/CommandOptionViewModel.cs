using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiscordChatExporter.Commanding;
using DiscordChatExporter.Gui.Framework;

namespace DiscordChatExporter.Gui.ViewModels.Components;

/// <summary>A native editor for one option described by the actual CLI command.</summary>
public sealed partial class CommandOptionViewModel : ViewModelBase
{
    private readonly Action _changed;
    private readonly DialogManager _dialogs;

    public CommandOptionViewModel(GuiOptionDto option, string[] values, DialogManager dialogs, Action changed)
    {
        Option = option;
        _dialogs = dialogs;
        _changed = changed;
        Text = option.IsSequence ? string.Join("\n", values) : values.FirstOrDefault() ?? string.Empty;
        BooleanValue = string.Equals(values.FirstOrDefault(), "true", StringComparison.OrdinalIgnoreCase);
        SelectedChoice = Choices.FirstOrDefault(c => c.Value == values.FirstOrDefault());
    }

    public GuiOptionDto Option { get; }
    public string Name => Option.Name;
    public string Label => Option.Label + (Option.IsRequired ? " *" : string.Empty);
    public string? Description => Option.Description;
    public string? Placeholder => Option.Placeholder;
    public bool IsAdvanced => Option.IsAdvanced;
    public bool IsBoolean => Option.Kind == "bool";
    public bool IsChoice => Option.Kind == "select";
    public bool IsText => !IsBoolean && !IsChoice;
    public bool IsPath => Option.Kind == "path";
    public bool IsSequence => Option.IsSequence;
    public IReadOnlyList<GuiOptionChoiceDto> Choices => Option.Choices ?? [];

    [ObservableProperty]
    public partial string Text { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool BooleanValue { get; set; }

    [ObservableProperty]
    public partial GuiOptionChoiceDto? SelectedChoice { get; set; }

    partial void OnTextChanged(string value) => _changed();
    partial void OnBooleanValueChanged(bool value) => _changed();
    partial void OnSelectedChoiceChanged(GuiOptionChoiceDto? value) => _changed();

    public string[] GetValues()
    {
        if (IsBoolean)
            return [BooleanValue ? "true" : "false"];
        if (IsChoice)
            return SelectedChoice is null ? [] : [SelectedChoice.Value];
        if (IsSequence)
            return Text.Split([' ', ',', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.IsNullOrWhiteSpace(Text) ? [] : [Text];
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        var path = Name == "data-package"
            ? await _dialogs.PromptOpenFilePathAsync([new FilePickerFileType("Discord data package") { Patterns = ["*.zip"] }])
            : await _dialogs.PromptDirectoryPathAsync();
        if (!string.IsNullOrWhiteSpace(path))
            Text = path;
    }
}
