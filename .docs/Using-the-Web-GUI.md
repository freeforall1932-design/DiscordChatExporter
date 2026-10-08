# Using the web interface

The command-line interface can also be used from a browser: the `gui` command starts a small web
server on your machine and exposes **every command as a button**, so you don't have to remember
option names.

The interface is a front-end for the CLI, not a re-implementation — pressing a button runs the
same command classes that you would get by typing the command in a terminal.

## Starting it

```console
DiscordChatExporter.Cli gui
```

The interface opens automatically at <http://localhost:5000/>. Press
<kbd>Ctrl</kbd>+<kbd>C</kbd> in the terminal to stop the server.

| Option         | Description                                                                |
| -------------- | -------------------------------------------------------------------------- |
| `-p, --port`   | Port to listen on (default: `5000`)                                        |
| `--host`       | `localhost` (default) only accepts connections from this machine, `any` also accepts connections from your local network |
| `--no-browser` | Don't open the browser automatically                                       |
| `--verbose`    | Print the diagnostic events of the interface to the console                |

For example, to use the interface from another device on your network:

```console
DiscordChatExporter.Cli gui --host any --port 8080
```

> [!WARNING]
> Use `--host any` only on a network you trust.
> While the server is running, anybody who can reach it can use your token and export data with it.

## What you can do

### 1. Enter your token

Paste the token into the field at the top. It is saved in your browser's local storage (so you only
have to paste it once) unless you untick *Remember*, and the trash button removes it again. The
token is never displayed in the interface or in the command line preview — commands that need it
show `--token ***` instead.

If the `DISCORD_TOKEN` environment variable is set on the machine running the server, you can leave
the field empty and that token will be used instead. The `guide` command explains how to obtain a
token.

### 2. Pick a command

Every command from the CLI is listed in the sidebar, grouped into *Discover*, *Export* and *Help*:

| Button                   | Command       | What it does                                        |
| ------------------------ | ------------- | --------------------------------------------------- |
| List servers             | `guilds`      | Shows all servers your token can access             |
| List channels            | `channels`    | Shows all channels in a server, with their IDs      |
| List direct messages     | `dm`          | Shows all direct message channels                   |
| Export channels          | `export`      | Exports one or more channels                        |
| Export a whole server    | `exportguild` | Exports every channel in a server                   |
| Export all direct messages | `exportdm`  | Exports every direct message channel                |
| Export everything        | `exportall`   | Exports everything the token can access             |
| How to get a token       | `guide`       | Explains how to obtain the token, server or channel ID |

The list is generated from the CLI itself, so a command that is added in a future version appears
in the interface automatically.

### 3. Fill in the options

Each option of the selected command gets its own field, with the same name, description and default
value as on the command line. Rarely used options are hidden behind *Advanced options*.

The command line that will be executed is always shown above the output panel, for example:

```console
DiscordChatExporter.Cli export --channel 803194314627285022 --format Json --output ./exports/
```

Copying it is a convenient way to move a task from the interface into a script.

### 4. Run it

The main area is split into three tabs, so that nothing has to be found by scrolling:

| Tab         | What it holds                                                              |
| ----------- | -------------------------------------------------------------------------- |
| **Options** | The fields of the selected command, plus the custom command line box        |
| **Output**  | The console: live output, progress and the result of the last command       |
| **Debug**   | Diagnostics: environment, server activity and the [debug info](#debugging)  |

Press *Run* (or <kbd>Ctrl</kbd>+<kbd>Enter</kbd>); the *Output* tab opens automatically. The command
executes inside the server process and its output — including progress bars — is streamed into the
console. *Cancel* (or <kbd>Esc</kbd>) stops a running command, and the download button saves the
output as a file.

Only one command can run at a time, so that the interface can't accidentally flood the Discord API.
Use the `--parallel` option to export several channels at the same time.

### 5. Anything else

The *Run a custom command line* box at the bottom accepts any command line, including commands and
options that don't have a field, such as:

```console
export --help
channels -g 123 --include-threads all
exportall -o ./exports/ --parallel 3
```

## Debugging

If something doesn't work as expected, open the **Debug** tab. It shows:

- the version, executable, working directory, runtime and the address of the interface,
- whether the server is reachable from your network and whether a token is configured through the
  `DISCORD_TOKEN` environment variable,
- a live log of everything the interface does: HTTP requests, started and finished commands, and
  full exception details for failures.

*Save debug info* writes all of it (plus your browser and viewport) to a JSON file, which is what to
attach to a bug report. Secrets are removed from the log before it is recorded, so the file never
contains your Discord token.

The same information is available on the command line:

- <http://localhost:5000/api/debug> returns it as JSON,
- starting the server with `--verbose` prints every diagnostic event to the terminal as well,
  which is useful when the interface itself doesn't load.

## Keyboard shortcuts

| Keys                           | Action                   |
| ------------------------------ | ------------------------ |
| <kbd>Alt</kbd>+<kbd>1</kbd>…<kbd>9</kbd> | Switch between commands |
| <kbd>Ctrl</kbd>+<kbd>Enter</kbd> | Run the selected command |
| <kbd>Esc</kbd>                 | Cancel the running command |
| <kbd>Ctrl</kbd>+<kbd>L</kbd>   | Clear the output        |

## Notes

- Exports are written relative to the working directory of the server process, which is shown at the
  bottom of the sidebar.
- The interface is served from resources embedded in the executable — no internet connection is
  required to load it, and it works the same on Windows, macOS and Linux.
- The web interface is meant for local use. If you only need to export a few channels on the same
  machine and prefer a desktop window, use the [desktop GUI](Using-the-GUI.md) instead.
- The interface is covered by end-to-end tests that run in CI: `scripts/smoke-test-gui.sh` drives the
  API with `curl`, while `ui-test/` loads the page into a DOM and clicks through it (`ui-test/run.sh`).
