using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Reflection;
using CliFx;
using CliFx.Binding;

namespace DiscordChatExporter.Commanding;

/// <summary>
/// Describes and validates all commands exposed by the CLI, so that the graphical interface
/// can present them as buttons and forms without duplicating any of the actual logic.
/// </summary>
public static class GuiCommandCatalog
{
    private const string TokenOptionName = "token";

    // Commands that are not meant to be exposed in the interface
    private static readonly string[] ExcludedCommandNames = ["gui"];

    // Display order of the buttons. Commands that are not listed here are appended at the
    // end, which makes the interface automatically pick up newly added commands. The token
    // guide is kept last so that everything fits in the sidebar without scrolling.
    private static readonly string[] CommandOrder =
    [
        "guilds",
        "channels",
        "dm",
        "export",
        "exportguild",
        "exportdm",
        "exportall",
        "guide",
    ];

    private static readonly IReadOnlyDictionary<string, GuiCommandOverlay> CommandOverlays =
        new Dictionary<string, GuiCommandOverlay>(StringComparer.OrdinalIgnoreCase)
        {
            ["guilds"] = new GuiCommandOverlay("List servers", "server"),
            ["channels"] = new GuiCommandOverlay("List channels", "hash"),
            ["dm"] = new GuiCommandOverlay("List direct messages", "chat"),
            ["export"] = new GuiCommandOverlay("Export channels", "download"),
            ["exportguild"] = new GuiCommandOverlay(
                "Export a whole server",
                "server",
                "Exports every channel in the specified server."
            ),
            ["exportdm"] = new GuiCommandOverlay(
                "Export all direct messages",
                "chat",
                "Exports every direct message channel."
            ),
            ["exportall"] = new GuiCommandOverlay(
                "Export everything",
                "globe",
                "Exports all channels that are accessible with the provided token."
            ),
            ["guide"] = new GuiCommandOverlay("How to get a token", "help"),
        };

    // Option labels shared by most commands. Command-specific overrides take precedence.
    private static readonly IReadOnlyDictionary<string, GuiOptionOverlay> OptionOverlays =
        new Dictionary<string, GuiOptionOverlay>(StringComparer.OrdinalIgnoreCase)
        {
            ["guild"] = new GuiOptionOverlay("Server ID", Placeholder: "803194314627285022"),
            ["channel"] = new GuiOptionOverlay(
                "Channel IDs",
                Placeholder: "Paste channel IDs separated by space, comma or newline"
            ),
            ["output"] = new GuiOptionOverlay(
                "Output path",
                Kind: "path",
                Placeholder: "./exports/ or ./exports/%G/%C.html"
            ),
            ["format"] = new GuiOptionOverlay("Export format"),
            ["after"] = new GuiOptionOverlay("After", Kind: "date", Placeholder: "2024-01-01"),
            ["before"] = new GuiOptionOverlay("Before", Kind: "date", Placeholder: "2024-12-31"),
            ["partition"] = new GuiOptionOverlay(
                "Partition limit",
                Placeholder: "100 or 10mb",
                IsAdvanced: true
            ),
            ["include-threads"] = new GuiOptionOverlay("Include threads"),
            ["filter"] = new GuiOptionOverlay(
                "Message filter",
                Placeholder: "from:userId has:image",
                IsAdvanced: true
            ),
            ["parallel"] = new GuiOptionOverlay("Parallel limit", Kind: "number", IsAdvanced: true),
            ["reverse"] = new GuiOptionOverlay("Newest messages first"),
            ["markdown"] = new GuiOptionOverlay("Format markdown, mentions and emojis"),
            ["media"] = new GuiOptionOverlay("Download media (avatars, files, images)"),
            ["reuse-media"] = new GuiOptionOverlay("Reuse previously downloaded media"),
            ["media-dir"] = new GuiOptionOverlay(
                "Media directory",
                Kind: "path",
                Placeholder: "./exports/media/",
                IsAdvanced: true
            ),
            ["locale"] = new GuiOptionOverlay("Locale", Placeholder: "en-US", IsAdvanced: true),
            ["utc"] = new GuiOptionOverlay("Normalize timestamps to UTC"),
            ["include-vc"] = new GuiOptionOverlay("Include voice channels"),
            ["include-dm"] = new GuiOptionOverlay("Include direct messages"),
            ["include-guilds"] = new GuiOptionOverlay("Include server channels"),
            ["data-package"] = new GuiOptionOverlay(
                "Discord data package",
                Kind: "path",
                Placeholder: "path/to/package.zip",
                IsAdvanced: true
            ),
            ["respect-rate-limits"] = new GuiOptionOverlay(
                "Respect advisory rate limits",
                IsAdvanced: true
            ),
            ["bot"] = new GuiOptionOverlay("Bot token (deprecated)", IsAdvanced: true),
            ["dateformat"] = new GuiOptionOverlay("Date format (deprecated)", IsAdvanced: true),
            ["fuck-russia"] = new GuiOptionOverlay(
                "Hide the Ukraine support message",
                IsAdvanced: true
            ),
        };

    /// <summary>
    /// Ready-made combinations for the tasks that are asked for most often. They only fill
    /// in the fields of a command, exactly like a user would, so nothing here can do
    /// anything that the command itself can't do.
    /// </summary>
    private static readonly IReadOnlyList<GuiPresetDto> Presets =
    [
        new GuiPresetDto(
            "export-everything-html",
            "Export everything (HTML)",
            "Exports every channel the token can access, as browsable HTML files.",
            "globe",
            "exportall",
            new Dictionary<string, string[]> { ["output"] = ["./exports/"] }
        ),
        new GuiPresetDto(
            "export-everything-json",
            "Export everything (JSON)",
            "Same as above, but as machine-readable JSON.",
            "globe",
            "exportall",
            new Dictionary<string, string[]> { ["output"] = ["./exports/"], ["format"] = ["Json"] }
        ),
        new GuiPresetDto(
            "export-server",
            "Export a server",
            "Fill in the server ID, then run. Exports every channel in that server.",
            "server",
            "exportguild",
            new Dictionary<string, string[]> { ["output"] = ["./exports/"] }
        ),
        new GuiPresetDto(
            "export-dms",
            "Back up all direct messages",
            "Exports every direct message channel.",
            "chat",
            "exportdm",
            new Dictionary<string, string[]> { ["output"] = ["./exports/"] }
        ),
        new GuiPresetDto(
            "export-one-person",
            "Only one person's messages",
            "Fill in the server ID, then complete the filter: from:username (also accepts a user ID).",
            "filter",
            "exportguild",
            new Dictionary<string, string[]> { ["output"] = ["./exports/"], ["filter"] = ["from:"] }
        ),
        new GuiPresetDto(
            "export-with-media",
            "Export everything with media",
            "Also downloads avatars, attachments and other files, reusing them if they are already on disk.",
            "download",
            "exportall",
            new Dictionary<string, string[]>
            {
                ["output"] = ["./exports/"],
                ["media"] = ["true"],
                ["reuse-media"] = ["true"],
            }
        ),
        new GuiPresetDto(
            "list-servers",
            "List my servers",
            "Shows every server the token can access, with their IDs.",
            "server",
            "guilds",
            new Dictionary<string, string[]>()
        ),
        new GuiPresetDto(
            "guide",
            "How to get a token",
            "Explains how to find your token, a server ID or a channel ID.",
            "help",
            "guide",
            new Dictionary<string, string[]>()
        ),
    ];

    private static readonly Lazy<IReadOnlyList<GuiCommandDto>> Commands = new(BuildCommands);

    private static readonly Lazy<IReadOnlyDictionary<string, GuiCommandDto>> CommandsByName = new(
        () =>
            Commands.Value.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase)
    );

    public static IReadOnlyList<GuiCommandDto> GetCommands() => Commands.Value;

    public static IReadOnlyList<GuiPresetDto> GetPresets() => Presets;

    public static GuiCommandDto? TryGetCommand(string? name) =>
        !string.IsNullOrWhiteSpace(name) && CommandsByName.Value.TryGetValue(name, out var command)
            ? command
            : null;

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "All command types are rooted by the source-generated command registration."
    )]
    private static CommandDescriptor? TryGetCommandDescriptor(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type type
    )
    {
        if (type.IsAbstract || !typeof(ICommand).IsAssignableFrom(type))
            return null;

        // Commands that are not decorated with this attribute are not registered with the
        // command-line application, so they must not be exposed to the user either.
        if (type.GetCustomAttribute<CommandAttribute>() is null)
            return null;

        var descriptorProperty = type.GetProperty(
            "Descriptor",
            BindingFlags.Public | BindingFlags.Static
        );

        return descriptorProperty?.GetValue(null) as CommandDescriptor;
    }

    // The command types are rooted by the source-generated command registration, so they are
    // always present, even in trimmed builds
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "Command types are rooted by the source-generated command registration."
    )]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2072",
        Justification = "Command types are rooted by the source-generated command registration."
    )]
    private static IReadOnlyList<GuiCommandDto> BuildCommands()
    {
        var commands = new List<GuiCommandDto>();

        foreach (var type in typeof(GuiCommandCatalog).Assembly.GetTypes())
        {
            var descriptor = TryGetCommandDescriptor(type);
            if (descriptor?.Name is not { } name)
                continue;

            if (ExcludedCommandNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                continue;

            // Create a throwaway instance to read the default values of all options
            var instance = TryCreateInstance(type);

            var options = new List<GuiOptionDto>();

            foreach (var option in descriptor.Inputs.OfType<CommandOptionDescriptor>())
            {
                // Ignore conventional options that are handled by the interface itself
                if (
                    string.IsNullOrWhiteSpace(option.Name)
                    || option.Property.Name is "IsHelpRequested" or "IsVersionRequested"
                )
                {
                    continue;
                }

                // The token is entered once for all commands, so it doesn't need a field
                if (string.Equals(option.Name, TokenOptionName, StringComparison.OrdinalIgnoreCase))
                    continue;

                options.Add(BuildOption(option, instance));
            }

            var overlay = CommandOverlays.GetValueOrDefault(name);

            commands.Add(
                new GuiCommandDto(
                    name,
                    overlay?.Title ?? descriptor.Description ?? name,
                    overlay?.Description ?? descriptor.Description,
                    overlay?.Icon ?? "terminal",
                    descriptor
                        .Inputs.OfType<CommandOptionDescriptor>()
                        .Any(o =>
                            string.Equals(
                                o.Name,
                                TokenOptionName,
                                StringComparison.OrdinalIgnoreCase
                            )
                        ),
                    GetCommandOrder(name),
                    options
                )
            );
        }

        return
        [
            .. commands.OrderBy(c => c.Order).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase),
        ];
    }

    private static GuiOptionDto BuildOption(CommandOptionDescriptor option, object? instance)
    {
        var name = option.Name!;
        var propertyType = option.Property.Type;

        var overlay = OptionOverlays.GetValueOrDefault(name);

        var underlyingType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        var isSequence = option.Converter.CanConvertSequence;

        // Enumerations are rendered as dropdowns. Some of them provide custom labels.
        IReadOnlyList<GuiOptionChoiceDto>? choices = null;
        if (!isSequence && underlyingType.IsEnum)
        {
            choices =
            [
                .. Enum.GetNames(underlyingType)
                    .Select(v => new GuiOptionChoiceDto(v, GetChoiceLabel(v))),
            ];
        }

        var kind = overlay?.Kind ?? InferKind(name, underlyingType, isSequence, choices);

        var defaultValue = GetDefaultValue(overlay, propertyType, underlyingType, instance, option);

        var label =
            overlay?.Label
            ?? GetAutoLabel(name)
            ?? (
                option.Description is { Length: > 0 } description
                    ? char.ToUpperInvariant(description[0]) + description[1..].TrimEnd('.')
                    : name
            );

        return new GuiOptionDto(
            name,
            option.ShortName?.ToString(),
            label,
            option.Description,
            kind,
            option.IsRequired,
            isSequence,
            defaultValue is not null,
            defaultValue,
            string.Equals(option.EnvironmentVariable, "DISCORD_TOKEN", StringComparison.Ordinal),
            choices,
            overlay?.Placeholder,
            overlay?.IsAdvanced ?? false
        );
    }

    private static int GetCommandOrder(string name)
    {
        var index = Array.FindIndex(
            CommandOrder,
            n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)
        );

        return index >= 0 ? index : CommandOrder.Length;
    }

    private static string InferKind(
        string name,
        Type type,
        bool isSequence,
        IReadOnlyList<GuiOptionChoiceDto>? choices
    )
    {
        if (isSequence)
            return "list";

        if (type == typeof(bool))
            return "bool";

        if (choices is not null)
            return "select";

        if (type == typeof(int) || type == typeof(long))
            return "number";

        return name switch
        {
            "token" => "password",
            "after" or "before" => "date",
            "output" or "media-dir" or "data-package" => "path",
            "partition" => "partition",
            "filter" => "filter",
            _ => "text",
        };
    }

    private static string GetChoiceLabel(string value) =>
        value switch
        {
            "None" => "None",
            "Active" => "Active (not archived)",
            "All" => "All (including archived)",
            "PlainText" => "Plain text (.txt)",
            "HtmlDark" => "HTML, dark theme (.html)",
            "HtmlLight" => "HTML, light theme (.html)",
            "Csv" => "CSV (.csv)",
            "Json" => "JSON (.json)",
            _ => value,
        };

    private static string? GetAutoLabel(string name) =>
        name switch
        {
            "channel" => "Channel IDs",
            "guild" => "Server ID",
            "output" => "Output path",
            "format" => "Export format",
            _ => null,
        };

    private static string? GetDefaultValue(
        GuiOptionOverlay? overlay,
        Type propertyType,
        Type underlyingType,
        object? instance,
        CommandOptionDescriptor option
    )
    {
        if (overlay?.DefaultValue is { } overlayDefaultValue)
            return overlayDefaultValue;

        // Sequences and booleans that default to 'false' don't need an explicit prefilled value
        if (option.Converter.CanConvertSequence || instance is null)
            return null;

        object? value;
        try
        {
            value = option.Property.GetValue(instance);
        }
        catch (Exception)
        {
            return null;
        }

        if (value is null)
            return null;

        if (underlyingType == typeof(bool))
            return (bool)value ? "true" : "false";

        if (underlyingType.IsEnum)
            return value.ToString();

        if (propertyType == typeof(string))
            return (string)value;

        if (underlyingType == typeof(int) || underlyingType == typeof(long))
            return Convert.ToString(value, CultureInfo.InvariantCulture);

        // Complex values (dates, filters, partitions) are rendered as free-form text fields,
        // so their default value would be meaningless for the user
        return null;
    }

    private static object? TryCreateInstance(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
            Type type
    )
    {
        try
        {
            return Activator.CreateInstance(type);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Converts the values collected in the interface into command-line arguments that are
    /// equivalent to what the user would have typed in the terminal.
    /// </summary>
    public static IReadOnlyList<string> BuildArguments(
        GuiCommandDto command,
        string? token,
        IReadOnlyDictionary<string, string[]> values
    )
    {
        var arguments = new List<string> { command.Name };

        // Only inject the token into commands that actually accept one (for example, the
        // "guide" command does not), otherwise the command-line parser rejects the arguments
        if (command.RequiresToken && !string.IsNullOrWhiteSpace(token))
        {
            arguments.Add("--token");
            arguments.Add(token);
        }

        foreach (var option in command.Options)
        {
            if (!values.TryGetValue(option.Name, out var optionValues) || optionValues.Length <= 0)
                continue;

            if (option.IsSequence)
            {
                foreach (var value in optionValues)
                {
                    if (string.IsNullOrWhiteSpace(value))
                        continue;

                    arguments.Add("--" + option.Name);
                    arguments.Add(value);
                }
            }
            else
            {
                var value = optionValues[0];
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                arguments.Add("--" + option.Name);
                arguments.Add(value);
            }
        }

        return arguments;
    }

    /// <summary>
    /// Verifies that the values collected in the interface are sufficient to run the command.
    /// </summary>
    public static IReadOnlyList<string> Validate(
        GuiCommandDto command,
        string? token,
        IReadOnlyDictionary<string, string[]> values,
        bool isTokenAvailableFromEnvironment
    )
    {
        var errors = new List<string>();

        if (
            command.RequiresToken
            && string.IsNullOrWhiteSpace(token)
            && !isTokenAvailableFromEnvironment
        )
        {
            errors.Add("A Discord token is required. Enter it in the field at the top.");
        }

        foreach (var option in command.Options)
        {
            if (!option.IsRequired)
                continue;

            if (
                !values.TryGetValue(option.Name, out var optionValues)
                || optionValues.All(string.IsNullOrWhiteSpace)
            )
            {
                errors.Add($"'{option.Label}' is required.");
            }
        }

        foreach (var (name, optionValues) in values)
        {
            var option = command.Options.FirstOrDefault(o =>
                string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase)
            );

            if (option is null)
            {
                errors.Add($"Unknown option '--{name}'.");
                continue;
            }

            if (option.IsSequence && optionValues.All(string.IsNullOrWhiteSpace))
                continue;

            if (!option.IsSequence && optionValues.Length > 1)
                errors.Add($"'{option.Label}' only accepts a single value.");
        }

        return errors;
    }

    /// <summary>
    /// Splits a raw command line (as typed by the user) into individual arguments, honoring
    /// single and double quotes.
    /// </summary>
    public static IReadOnlyList<string> SplitRawCommandLine(string commandLine)
    {
        var arguments = new List<string>();
        var buffer = new System.Text.StringBuilder();
        var isQuoted = false;
        var quoteChar = '\0';

        foreach (var c in commandLine)
        {
            if (isQuoted)
            {
                if (c == quoteChar)
                    isQuoted = false;
                else
                    buffer.Append(c);
            }
            else if (c is '"' or '\'')
            {
                isQuoted = true;
                quoteChar = c;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (buffer.Length > 0)
                {
                    arguments.Add(buffer.ToString());
                    buffer.Clear();
                }
            }
            else
            {
                buffer.Append(c);
            }
        }

        if (buffer.Length > 0)
            arguments.Add(buffer.ToString());

        return arguments;
    }

    /// <summary>
    /// Renders the equivalent command line for the given arguments, masking the token so that
    /// it never ends up in the log or on the screen.
    /// </summary>
    public static IReadOnlyList<string> GetTokenValues(IReadOnlyList<string> arguments)
    {
        var tokens = new List<string>();
        for (var i = 0; i < arguments.Count; i++)
        {
            var value = arguments[i];
            if (
                string.Equals(value, "--token", StringComparison.OrdinalIgnoreCase)
                || value == "-t"
            )
            {
                if (i + 1 < arguments.Count)
                    tokens.Add(arguments[++i]);
            }
            else if (value.StartsWith("--token=", StringComparison.OrdinalIgnoreCase))
                tokens.Add(value[8..]);
            else if (value.StartsWith("-t=", StringComparison.Ordinal))
                tokens.Add(value[3..]);
        }
        return tokens;
    }

    public static IReadOnlyList<string> BuildRawArguments(string commandLine, string? token)
    {
        var arguments = SplitRawCommandLine(commandLine);
        if (arguments.Count == 0)
            throw new ArgumentException("Enter a command, for example: export --help.");
        if (
            !string.IsNullOrWhiteSpace(token)
            && GetTokenValues(arguments).Count == 0
            && TryGetCommand(arguments[0]) is { RequiresToken: true }
        )
            return [arguments[0], "--token", token, .. arguments.Skip(1)];
        return arguments;
    }

    public static string FormatCommandLine(
        string executableName,
        IReadOnlyList<string> arguments,
        string? token
    )
    {
        var buffer = new System.Text.StringBuilder(executableName);
        for (var i = 0; i < arguments.Count; i++)
        {
            var argument = arguments[i];
            buffer.Append(' ');
            if (
                string.Equals(argument, "--token", StringComparison.OrdinalIgnoreCase)
                || argument == "-t"
            )
            {
                buffer.Append(argument);
                if (i + 1 < arguments.Count)
                {
                    buffer.Append(" ***");
                    i++;
                }
            }
            else if (argument.StartsWith("--token=", StringComparison.OrdinalIgnoreCase))
                buffer.Append("--token=***");
            else if (argument.StartsWith("-t=", StringComparison.Ordinal))
                buffer.Append("-t=***");
            else if (argument.Length == 0 || argument.Any(char.IsWhiteSpace))
                buffer.Append('"').Append(argument).Append('"');
            else
                buffer.Append(argument);
        }
        return GuiRedaction.Redact(buffer.ToString());
    }
}

internal sealed record GuiCommandOverlay(string Title, string Icon, string? Description = null);

internal sealed record GuiOptionOverlay(
    string? Label = null,
    string? Kind = null,
    string? Placeholder = null,
    bool IsAdvanced = false,
    string? DefaultValue = null
);
