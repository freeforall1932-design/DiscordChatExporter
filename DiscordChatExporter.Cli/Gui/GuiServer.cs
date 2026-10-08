using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DiscordChatExporter.Cli.Gui;

internal sealed class GuiServerOptions
{
    public required string DisplayUrl { get; init; }

    public required IReadOnlyList<string> Prefixes { get; init; }

    public required bool IsNetworkExposed { get; init; }

    public required string ExecutableName { get; init; }

    public required string VersionText { get; init; }
}

/// <summary>
/// Minimal HTTP server that hosts the graphical interface and exposes the CLI commands to it.
/// </summary>
internal sealed class GuiServer : IDisposable
{
    private const string ProductName = "DiscordChatExporter";

    private static readonly TimeSpan MaxPollWait = TimeSpan.FromSeconds(30);

    private readonly HttpListener _listener = new();
    private readonly GuiRunManager _runManager;
    private readonly GuiServerOptions _options;
    private readonly Dictionary<string, byte[]> _assetCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _startedAt = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture);

    public GuiServer(GuiServerOptions options, GuiRunManager runManager)
    {
        _options = options;
        _runManager = runManager;
    }

    public GuiServerOptions Options => _options;

    public void Start()
    {
        foreach (var prefix in _options.Prefixes)
            _listener.Prefixes.Add(prefix);

        _listener.Start();
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        // Stopping the listener is the only way to unblock the pending GetContextAsync() call
        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                _listener.Stop();
            }
            catch (Exception)
            {
                // Ignore
            }
        });

        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;

            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception)
            {
                // The listener was stopped as part of the shutdown
                break;
            }

            _ = HandleRequestSafeAsync(context);
        }
    }

    private async Task HandleRequestSafeAsync(HttpListenerContext context)
    {
        try
        {
            await HandleRequestAsync(context);
        }
        catch (Exception ex)
        {
            try
            {
                await WriteJsonAsync(
                    context.Response,
                    (int)HttpStatusCode.InternalServerError,
                    new GuiErrorDto(ex.Message)
                );
            }
            catch (Exception)
            {
                // The connection is already gone
            }
        }
        finally
        {
            try
            {
                context.Response.Close();
            }
            catch (Exception)
            {
                // Ignore
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        var path = request.Url?.AbsolutePath ?? "/";
        var method = request.HttpMethod.ToUpperInvariant();

        response.Headers["Cache-Control"] = "no-store";

        // Serve the interface
        if (method is "GET" or "HEAD")
        {
            var assetName = path switch
            {
                "/" or "/index.html" => "index.html",
                "/app.css" => "app.css",
                "/app.js" => "app.js",
                _ => null,
            };

            if (assetName is not null)
            {
                await WriteAssetAsync(response, assetName);
                return;
            }
        }

        if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            await WriteTextAsync(response, (int)HttpStatusCode.NotFound, "Not found.");
            return;
        }

        // Cross-origin requests are rejected, so that a malicious website can't drive the
        // tool (and the user's token) through the browser
        if (!IsSameOrigin(request))
        {
            await WriteJsonAsync(
                response,
                (int)HttpStatusCode.Forbidden,
                new GuiErrorDto("Cross-origin requests are not allowed.")
            );
            return;
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // /api/info
        if (segments.Length == 2 && string.Equals(segments[1], "info", StringComparison.OrdinalIgnoreCase))
        {
            if (method != "GET")
            {
                await WriteMethodNotAllowedAsync(response, "GET");
                return;
            }

            await WriteJsonAsync(
                response,
                (int)HttpStatusCode.OK,
                new GuiInfoDto(
                    ProductName,
                    _options.VersionText,
                    _options.ExecutableName,
                    Directory.GetCurrentDirectory(),
                    _startedAt,
                    _options.IsNetworkExposed,
                    !string.IsNullOrWhiteSpace(
                        Environment.GetEnvironmentVariable("DISCORD_TOKEN")
                    ),
                    GuiCommandCatalog.GetCommands()
                )
            );

            return;
        }

        // /api/runs
        if (segments.Length == 2 && string.Equals(segments[1], "runs", StringComparison.OrdinalIgnoreCase))
        {
            if (method == "POST")
            {
                await HandleCreateRunAsync(context);
                return;
            }

            if (method == "GET")
            {
                await WriteJsonAsync(
                    response,
                    (int)HttpStatusCode.OK,
                    _runManager.All.Select(r => r.Snapshot(0) with { Output = string.Empty }).ToArray()
                );
                return;
            }

            await WriteMethodNotAllowedAsync(response, "GET, POST");
            return;
        }

        // /api/runs/*
        if (segments.Length >= 3 && string.Equals(segments[1], "runs", StringComparison.OrdinalIgnoreCase))
        {
            // /api/runs/current
            if (string.Equals(segments[2], "current", StringComparison.OrdinalIgnoreCase))
            {
                var current = _runManager.Current;
                var currentSnapshot = current?.Snapshot(0);

                await WriteJsonAsync(
                    response,
                    (int)HttpStatusCode.OK,
                    new GuiCurrentRunDto(
                        currentSnapshot is not null
                            ? currentSnapshot with { Output = string.Empty }
                            : null
                    )
                );
                return;
            }

            var run = _runManager.TryGet(segments[2]);
            if (run is null)
            {
                await WriteJsonAsync(
                    response,
                    (int)HttpStatusCode.NotFound,
                    new GuiErrorDto($"Run '{segments[2]}' was not found.")
                );
                return;
            }

            // /api/runs/{id}
            if (segments.Length == 3 && method == "GET")
            {
                var cursor = ParseInt(request.QueryString["cursor"]) ?? 0;
                var wait = Math.Clamp(
                    ParseInt(request.QueryString["wait"]) ?? 0,
                    0,
                    (int)MaxPollWait.TotalMilliseconds
                );

                var snapshot = await run.PollAsync(cursor, wait, CancellationToken.None);
                await WriteJsonAsync(response, (int)HttpStatusCode.OK, snapshot);
                return;
            }

            // /api/runs/{id}/cancel
            if (
                segments.Length == 4
                && string.Equals(segments[3], "cancel", StringComparison.OrdinalIgnoreCase)
                && method == "POST"
            )
            {
                try
                {
                    await run.Cancellation.CancelAsync();
                    run.Append(
                        Environment.NewLine + "[cancellation requested, waiting for the command to stop]" + Environment.NewLine
                    );
                }
                catch (ObjectDisposedException) { }

                await WriteJsonAsync(
                    response,
                    (int)HttpStatusCode.OK,
                    run.Snapshot(0) with { Output = string.Empty }
                );
                return;
            }

            // /api/runs/{id}/log
            if (
                segments.Length == 4
                && string.Equals(segments[3], "log", StringComparison.OrdinalIgnoreCase)
                && method == "GET"
            )
            {
                var snapshot = run.Snapshot(0);
                response.Headers["Content-Disposition"] =
                    $"attachment; filename=\"dce-{run.Command}-{run.Id}.log\"";
                await WriteTextAsync(response, (int)HttpStatusCode.OK, snapshot.Output);
                return;
            }
        }

        await WriteJsonAsync(
            response,
            (int)HttpStatusCode.NotFound,
            new GuiErrorDto($"Unknown endpoint '{path}'.")
        );
    }

    private async Task HandleCreateRunAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        var body = await ReadBodyAsync(request);

        GuiRunRequest? runRequest;
        try
        {
            runRequest = JsonSerializer.Deserialize(body, GuiJsonContext.Default.GuiRunRequest);
        }
        catch (JsonException ex)
        {
            await WriteJsonAsync(
                response,
                (int)HttpStatusCode.BadRequest,
                new GuiErrorDto($"Failed to parse the request: {ex.Message}")
            );
            return;
        }

        if (runRequest is null)
        {
            await WriteJsonAsync(
                response,
                (int)HttpStatusCode.BadRequest,
                new GuiErrorDto("The request body is empty.")
            );
            return;
        }

        var token = runRequest.Token?.Trim('"', ' ');

        IReadOnlyList<string> arguments;
        string commandName;

        // Raw command line, as typed by the user
        if (runRequest.RawCommandLine is not null)
        {
            arguments = GuiCommandCatalog.SplitRawCommandLine(runRequest.RawCommandLine);
            if (arguments.Count <= 0)
            {
                await WriteJsonAsync(
                    response,
                    (int)HttpStatusCode.BadRequest,
                    new GuiErrorDto("Enter a command to run, for example: export --help")
                );
                return;
            }

            commandName = arguments[0];

            // Inject the token if the command supports it and it wasn't provided explicitly
            if (
                !string.IsNullOrWhiteSpace(token)
                && !arguments.Contains("--token", StringComparer.OrdinalIgnoreCase)
                && GuiCommandCatalog.TryGetCommand(commandName) is { RequiresToken: true }
            )
            {
                arguments =
                [
                    arguments[0],
                    "--token",
                    token,
                    .. arguments.Skip(1),
                ];
            }
        }
        // Command with values collected in the interface
        else
        {
            var command = GuiCommandCatalog.TryGetCommand(runRequest.Command);
            if (command is null)
            {
                await WriteJsonAsync(
                    response,
                    (int)HttpStatusCode.BadRequest,
                    new GuiErrorDto($"Command '{runRequest.Command}' was not found.")
                );
                return;
            }

            var values = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, optionValues) in runRequest.Options ?? [])
                values[name] = optionValues;

            var errors = GuiCommandCatalog.Validate(
                command,
                token,
                values,
                !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISCORD_TOKEN"))
            );

            if (errors.Count > 0)
            {
                await WriteJsonAsync(
                    response,
                    (int)HttpStatusCode.BadRequest,
                    new GuiErrorDto("The command can't be run yet.", errors)
                );
                return;
            }

            arguments = GuiCommandCatalog.BuildArguments(command, token, values);
            commandName = command.Name;
        }

        GuiRun run;
        try
        {
            run = _runManager.Start(commandName, arguments, token);
        }
        catch (GuiRunBusyException ex)
        {
            await WriteJsonAsync(
                response,
                (int)HttpStatusCode.Conflict,
                new GuiErrorDto(
                    ex.Message + " Wait for it to finish, or cancel it before starting another one."
                )
            );
            return;
        }

        await WriteJsonAsync(response, (int)HttpStatusCode.Created, run.Snapshot(0));
    }

    private bool IsSameOrigin(HttpListenerRequest request)
    {
        var origin = request.Headers["Origin"];
        if (string.IsNullOrWhiteSpace(origin))
            return true;

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri))
            return false;

        var host = request.Headers["Host"] ?? request.Url?.Authority;
        if (string.IsNullOrWhiteSpace(host))
            return false;

        return string.Equals(
            originUri.Host,
            host.Split(':')[0],
            StringComparison.OrdinalIgnoreCase
        );
    }

    private async Task WriteAssetAsync(HttpListenerResponse response, string name)
    {
        if (!_assetCache.TryGetValue(name, out var bytes))
        {
            var resourceName = typeof(GuiServer)
                .Assembly.GetManifestResourceNames()
                .FirstOrDefault(n =>
                    n.EndsWith("Assets." + name, StringComparison.OrdinalIgnoreCase)
                );

            if (resourceName is null)
            {
                await WriteTextAsync(response, (int)HttpStatusCode.NotFound, "Asset not found.");
                return;
            }

            using var stream = typeof(GuiServer).Assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                await WriteTextAsync(response, (int)HttpStatusCode.NotFound, "Asset not found.");
                return;
            }

            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            bytes = buffer.ToArray();

            _assetCache[name] = bytes;
        }

        response.StatusCode = (int)HttpStatusCode.OK;
        response.ContentType = Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".html" => "text/html; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".js" => "text/javascript; charset=utf-8",
            _ => "application/octet-stream",
        };
        response.ContentLength64 = bytes.Length;

        await response.OutputStream.WriteAsync(bytes);
    }

    private static async Task WriteJsonAsync<T>(
        HttpListenerResponse response,
        int statusCode,
        T payload
    )
        where T : class
    {
        var json = JsonSerializer.Serialize(payload, typeof(T), GuiJsonContext.Default);

        await WriteTextAsync(response, statusCode, json, "application/json; charset=utf-8");
    }

    private static async Task WriteTextAsync(
        HttpListenerResponse response,
        int statusCode,
        string content,
        string contentType = "text/plain; charset=utf-8"
    )
    {
        var bytes = Encoding.UTF8.GetBytes(content);

        response.StatusCode = statusCode;
        response.ContentType = contentType;
        response.ContentLength64 = bytes.Length;

        await response.OutputStream.WriteAsync(bytes);
    }

    private static Task WriteMethodNotAllowedAsync(HttpListenerResponse response, string allow) =>
        WriteTextAsync(response, (int)HttpStatusCode.MethodNotAllowed, $"Allowed: {allow}");

    private static async Task<string> ReadBodyAsync(HttpListenerRequest request)
    {
        using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private static int? ParseInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;

    public void Dispose()
    {
        try
        {
            _listener.Stop();
            _listener.Close();
        }
        catch (Exception)
        {
            // Ignore
        }
    }
}
