# Using the GUI

## Native Commands workspace (this branch)

This branch extends the existing Avalonia desktop application; it does not replace the server/channel
picker or embed a browser. The compact 625×665 main window keeps the original export workflow, server-avatar rail,
channel tree and amber export action. There is no replacement top-level tab bar.
The optional **Command tools** window exposes `guilds`, `channels`, `dm`, `export`, `exportguild`, `exportdm`,
`exportall`, and `guide` through native controls.

> [!NOTE]
> These additions are currently on this development branch/PR, not in the upstream stable download.
> Use the `desktop-win-x64` package from the branch's **gui** workflow to try the native build.

### Using Commands

1. Use the regular export screen as before. The terminal icon beside Settings opens **Command tools**
   in a separate, owned window; reopening it activates the same window. Enter your token on either
   window. Both use the same in-memory token
   and the existing desktop *Remember token* setting; there is no browser-local-storage token copy.
   `DISCORD_TOKEN` is also supported for command execution. Do not share your token or debug unmasked
   input with other people.
2. In Command tools, select a command from the flat, icon-labelled list, or choose a **Quick start** preset. Presets only fill the
   fields: nothing executes until you press **Run**. Selected server/channel IDs from Export prefill
   compatible fields when a command form is first opened.
3. Review the fields and the masked command-line preview. **Advanced options** reveals filters and
   less common controls. Paths have native Browse buttons; you can also type file paths or templates.
4. Press **Run** (or **Ctrl+Enter / Cmd+Enter**). The workspace opens **Output** automatically and
   displays captured output and progress. **Cancel** (or **Esc**) requests cancellation of the actual
   command, rather than merely hiding its result.
5. Use **Debug** for runtime details, run history and redacted diagnostic events. **Save debug info**
   exports JSON; **Save output** saves the current log as text.

Closing Command tools preserves its form, output and debug history; it does not cancel an active run.
Use **Cancel** to cancel. Closing the main application still cancels its active work.
Native file dialogs and Settings are routed to the appropriate window.

The original Export workflow and Command tools reserve the same execution slot. Starting a normal export
or loading servers/channels prevents a conflicting command from starting; its activity is also visible
in Output and can be cancelled there. The existing channel exporter still does the export work.

The **Custom command line** option accepts commands such as `export --help`, without the executable
name. Execution is in-process using the real CliFx command classes in the shared
`DiscordChatExporter.Commanding` assembly—no second CLI executable or local HTTP listener is started.
The CLI's optional browser interface uses the same catalog, presets and runner.

### Development verification

```console
dotnet run --project DiscordChatExporter.Gui.Tests --configuration Release -- ./desktop-screenshots
```

This offline harness renders the real Avalonia XAML/styles with the headless Skia platform and tests
native control bindings, presets, token sharing, execution/cancellation, output, diagnostics and the
shared runner. Its screenshots are CI rendering evidence, not a claim that a physical Windows desktop
or authenticated Discord export was tested. Real file dialogs and authenticated exports still need
manual verification on the target system. The populated export screenshot uses visibly labelled offline
sample data and local avatar crops from the repository’s existing README image. It is not evidence of
authenticated account access, and the sample mode is not shipped in the application.

## Video tutorial

[![Video tutorial](https://i.ytimg.com/vi/jjtu0VQXV7I/hqdefault.jpg)](https://youtube.com/watch?v=jjtu0VQXV7I)

> Video by [NoIntro Tutorials](https://youtube.com/channel/UCFezKSxdNKJe77-hYiuXu3Q).

## Guide

### Step 1

After extracting the `.zip`, run `DiscordChatExporter.exe` **(Windows)**, or `DiscordChatExporter` **(Linux)**.

If you're using **macOS**, you'll need to manually grant permission for the app to run.
If you skip these steps, the "DiscordChatExporter is damaged and can’t be opened" error will be shown.

1. Open Terminal.app. You can search for it in Spotlight (press <kbd>⌘</kbd> + <kbd>Space</kbd> and type "Terminal").
2. Paste the following into the terminal window:
   ```bash
   xattr -rd com.apple.quarantine
   ```
3. Hit <kbd>Space</kbd> once to add a space after the command
4. Drag and drop DiscordChatExporter.app into the terminal window
5. Press <kbd>Return</kbd> to run the command
6. Open DiscordChatExporter.app normally

> Apple requires apps to be notarized and signed in order to run on macOS without warnings, which in turn requires an Apple Developer membership ($99/year). This open-source project is distributed for free and without commercial intent.

### Step 2

Please refer to the on-screen instructions to get your token, then paste your token in the upper text box and hit ENTER or click the arrow (→).

> [!WARNING]
> **Never share your token!**
> A token gives full access to an account, treat it like a password.

<img src="https://i.imgur.com/SuLQ5tZ.png" height="400"/>

### Step 3

DCE will display your Direct Messages and a sidebar with your server list. Select the channel you would like to export, then click the ![Screenshot](https://i.imgur.com/dnTOlDa.png) button to continue.

> **Note**:
> You can export multiple channels at once by holding `CTRL` or `SHIFT` while selecting.
> You can also double-click a channel to export it without clicking the ![Screenshot](https://i.imgur.com/dnTOlDa.png) button.

<img src="https://i.imgur.com/JHMFRh2.png" height="400"/>

### Step 4

In this screen you can customize the following:

- **Output path** - The folder where the exported chat(s) will be saved.

- **Export format** - HTML (Dark), HTML (Light), TXT, CSV and JSON

- **Date range (after/before)** (Optional) - If set, only messages sent in the provided date range will be exported. Only one value (either after or before) is required if you want to use this option.

  > **Note**:
  > Please note that the time defaults to **12:00 AM** (midnight/00:00). This means that if you choose to export between Sep 17th and Sep 18th, messages from Sep 18th won't be exported.

- **Partition limit** (Optional) - Split output into partitions, each limited to this number of messages (e.g., 100) or file size (e.g., 10mb). For example, a channel with 36 messages set to be partitioned every 10 messages will output 4 files.

- **Message Filter** (Optional) - Special notation for filtering the messages that get included in the export. See [Message filters](Message-filters.md) for more info.

- **Format markdown** (Optional) - Disable markdown processing when exporting. You can use this to produce JSON or plain text exports without unwrapping mentions, custom emoji, and certain other special tokens.

- **Download assets** (Optional) - If this option is set, the export will include additional files such as user avatars, attached files, images, etc. Only files that are referenced by the export are downloaded, which means that, for example, user avatars will not be downloaded when using the plain text (TXT) export format. A folder containing the assets will be created along with the exported chat. They must be kept together.

- **Reuse assets** (Optional) - If this option is set, the export will reuse already downloaded assets to skip redundant requests. This option is only available when **Download assets** is enabled.

- **Assets directory path** (Optional) - If this option is set, the export will use the specified directory to store assets from all exported channels in the same place.

> **Note**:
> You need to scroll down to see all available options.

## Settings

- **Auto-update** - Perform automatic updates on every launch.
  Default: Enabled

  > **Note**:
  > Keep this option enabled to receive the latest features and bug fixes!

- **Dark mode** - Use darker colors in the UI (User Interface).
  Default: Disabled

- **Persist token** - Persist last used token between sessions.
  Default: Enabled

- **Show threads** - Controls whether threads are shown in the channel list.
  Default: none

- **Locale** - Customize how dates are formatted in the exported files.

- **Date format** - Customize how dates are formatted in the exported files in the settings menu ().

- **Parallel limit** - The number of channels that will be exported at the same time.
  Default: 1

  > **Note**:
  > Try to keep this number low so that your account doesn't get flagged.

- **Normalize to UTC** - Convert all dates to UTC before exporting.
