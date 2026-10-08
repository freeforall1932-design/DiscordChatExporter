# Using the web interface

The CLI ships with an optional web interface that exposes **every command as a button**, so you
don't have to remember command-line options.

It is a small web server that runs on your machine, inside the same executable as the CLI, and it
executes the exact same command classes — the interface is a front-end for the CLI, not a
re-implementation.

## Starting it

```console
./DiscordChatExporter.Cli gui
```

The interface is then available at <http://localhost:5000/> and your default browser opens
automatically. Press <kbd>Ctrl</kbd>+<kbd>C</kbd> in the terminal to stop the server.

| Option         | Description                                                                   |
| -------------- | ----------------------------------------------------------------------------- |
| `-p, --port`   | Port to listen on (default: `5000`)                                           |
| `--host`       | `localhost` (default) only allows this machine, `any` also allows your LAN    |
| `--no-browser` | Don't open the browser automatically                                          |

For example, to use the interface from another device on your network:

```console
./DiscordChatExporter.Cli gui --host any --port 8080
```

> [!WARNING]
> Use `--host any` only on a network you trust.
> While the server is running, anybody who can reach it can use your token and export data with it.

## What you can do

1. **Paste your token** in the field at the top.
   It is stored in your browser's local storage (click the trash icon next to the field to forget
   it), and it is sent to the local server with every command that needs it.
   If `DISCORD_TOKEN` is set in the environment, DCE uses that instead.

2. **Pick a command** from the sidebar. Every command available in the CLI is listed, grouped into
   *Discover*, *Export* and *Help*:

   | Button                  | Command       |
   | ----------------------- | ------------- |
   | List servers            | `guilds`      |
   | List channels           | `channels`    |
   | List direct messages    | `dm`          |
   | Export channels         | `export`      |
   | Export a whole server   | `exportguild` |
   | Export all direct messages | `exportdm` |
   | Export everything       | `exportall`   |
   | How to get a token      | `guide`       |

   The list is generated from the CLI itself, so a command that is added (or removed) in a future
   version shows up (or disappears) automatically.

3. **Fill in the options.** Fields are generated from the command's own options, with the same
   names, descriptions and defaults as the command line. Options that are only relevant in rare
   cases are hidden behind *Advanced options*.

   The command line that will be executed is always shown below the form, for example:

   ```console
   DiscordChatExporter.Cli export --channel 803194314627285022 --format Json --output ./exports/
   ```

   Copying it is a convenient way to move a task from the interface into a script.

4. **Press `Run`.** The command executes in the server process, and its output is streamed into the
   console panel below, including progress. `Cancel` (or <kbd>Esc</kbd>) stops a running command,
   and the *download* button saves the log.

5. **Anything the interface doesn't cover** can be run from *Run a custom command line* at the
   bottom, which accepts any CLI command, including `--help`:

   ```console
   export --help
   exportall -o ./exports/ --parallel 3 --media
   ```

## Keyboard shortcuts

| Keys                       | Action                  |
| -------------------------- | ----------------------- |
| <kbd>Alt</kbd>+<kbd>1..9</kbd> | Switch between commands |
| <kbd>Ctrl</kbd>+<kbd>Enter</kbd> | Run the current command |
| <kbd>Esc</kbd>             | Cancel the running command |
| <kbd>Ctrl</kbd>+<kbd>L</kbd> | Clear the output        |

## Notes

- Only one command can run at a time, so that the interface can't accidentally flood the Discord
  API (use the `--parallel` option to export several channels at the same time).
- Exports are written relative to the working directory of the server process, which is shown at
  the bottom of the sidebar.
- The interface is served from embedded resources — no internet connection is needed to load it,
  and it works the same on Windows, macOS and Linux.
