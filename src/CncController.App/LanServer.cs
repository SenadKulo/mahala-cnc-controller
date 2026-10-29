using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using CncController.Remote;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CncController.App;

public enum LanState
{
    Off,
    Starting,
    Running,
    /// <summary>Couldn't start (port taken...), see <see cref="LanServer.Error"/>.</summary>
    Failed,
}

/// <summary>
/// Serves the machine page to phones and computers in the local network, without the website or an account: the same
/// page as in the desktop window, talking to the session over a WebSocket with <see cref="LocalPage"/>'s messages
/// (the page's side is ui/lan.js). Only a device that typed the 6-digit code (kept in a cookie) gets the page;
/// the code is shown on the desktop. Uploads are a normal post, like on the website.
/// Start and stop it on the session's thread; <see cref="StateChanged"/> is raised there too.
/// </summary>
public sealed class LanServer : IAsyncDisposable
{
    /// <summary>Marks what came from the local network, for the log.</summary>
    public const string Origin = "lan";
    public const int DefaultPort = 8090;
    public const string LanPage = "lan.html";

    const string CookieName = "cnc-lan";
    const int MaxFailures = 5;
    const int MaxMessageBytes = 1024 * 1024;
    static readonly TimeSpan LockOut = TimeSpan.FromMinutes(1);
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    readonly PageApi _api;
    readonly Action<Action> _post;
    readonly string _uiFolder;
    WebApplication? _app;
    CancellationTokenSource? _stopping;
    volatile string _code = "";
    readonly object _lock = new();
    int _failures;
    DateTime _lockedUntil;

    /// <param name="post">Runs an action on the session's thread.</param>
    /// <param name="uiFolder">The page's files: the website's wwwroot as copied next to the program, with lan.html and lan.js.</param>
    public LanServer(PageApi api, Action<Action> post, string uiFolder)
    {
        _api = api;
        _post = post;
        _uiFolder = uiFolder;
        // A phone connects long after the program started; it gets the log since then, like the window.
        api.Session.Logged += (time, text) =>
        {
            _recentLog.Enqueue(new LogLine(time, text));
            while (_recentLog.Count > PagePublisher.LogBufferSize)
                _recentLog.Dequeue();
        };
    }

    // On the session's thread, where the session logs.
    readonly Queue<LogLine> _recentLog = new();

    public LanState State { get; private set; }

    /// <summary>Why it couldn't start, while <see cref="State"/> is <see cref="LanState.Failed"/>.</summary>
    public string? Error { get; private set; }

    /// <summary>The port it listens on while running.</summary>
    public int Port { get; private set; }

    /// <summary>Raised on the session's thread.</summary>
    public event Action? StateChanged;

    /// <summary>The page's address to type on a phone: the first of <see cref="Urls"/>; null while not running.</summary>
    public string? Url => Urls().FirstOrDefault();

    /// <summary>Every address of this computer in its networks, the one with a gateway (the home network) first.</summary>
    public IReadOnlyList<string> Urls() =>
        State == LanState.Running ? [.. Addresses().Select(a => $"http://{a}:{Port}")] : [];

    public string StateText
    {
        get
        {
            var l = _api.Session.Language;
            return l.T("Lokalna mreža: ", "Local network: ") + State switch
            {
                LanState.Starting => l.T("pokrećem...", "starting..."),
                LanState.Running => Url ?? l.T("radi, ali računar nema adresu u mreži", "running, but the computer has no network address"),
                LanState.Failed => l.T($"ne radi ({Error})", $"not running ({Error})"),
                _ => l.T("isključena", "off"),
            };
        }
    }

    /// <summary>A new random code of 6 digits.</summary>
    public static string NewCode() => RandomNumberGenerator.GetInt32(1_000_000).ToString("D6");

    /// <summary>This computer's IPv4 addresses in its networks, those of an interface with a gateway first.</summary>
    public static IReadOnlyList<IPAddress> Addresses()
    {
        try
        {
            return [.. NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                    && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                .Select(n => n.GetIPProperties())
                .OrderByDescending(p => p.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)))
                .SelectMany(p => p.UnicastAddresses.Select(u => u.Address))
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a) && a.GetAddressBytes() is not [169, 254, ..])
                .Distinct()];
        }
        catch (NetworkInformationException)
        {
            return [];
        }
    }

    /// <summary>Starts listening (again) on <paramref name="port"/> of every network; a device needs <paramref name="code"/> once.</summary>
    /// <param name="bind">Tests: listen on this address only.</param>
    public async Task StartAsync(int port, string code, IPAddress? bind = null)
    {
        await StopAsync();
        _code = code;
        lock (_lock)
            _failures = 0;
        SetState(LanState.Starting, null);
        var stopping = _stopping = new CancellationTokenSource();
        WebApplication? app = null;
        try
        {
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ContentRootPath = _uiFolder });
            builder.Logging.ClearProviders();
            builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(2));
            builder.WebHost.ConfigureKestrel(o =>
            {
                o.Listen(bind ?? IPAddress.Any, port);
                o.Limits.MaxRequestBodySize = RemoteContract.MaxUploadBytes + 64 * 1024;
            });
            app = builder.Build();
            Configure(app, stopping.Token);
            await app.StartAsync();
            Port = new Uri(app.Urls.First()).Port;
            _app = app;
            SetState(LanState.Running, null);
        }
        catch (Exception ex) when (ex is IOException or SocketException or InvalidOperationException or UnauthorizedAccessException)
        {
            if (app is not null)
                await app.DisposeAsync();
            var l = _api.Session.Language;
            SetState(LanState.Failed, ex is AddressInUseException || ex.InnerException is AddressInUseException
                ? l.T($"port {port} je zauzet", $"port {port} is in use")
                : ex.Message);
        }
    }

    public async Task StopAsync()
    {
        _stopping?.Cancel();
        _stopping = null;
        if (_app is { } app)
        {
            _app = null;
            try
            {
                await app.StopAsync();
            }
            finally
            {
                await app.DisposeAsync();
            }
        }
        if (State != LanState.Off)
            SetState(LanState.Off, null);
    }

    void SetState(LanState state, string? error)
    {
        State = state;
        Error = error;
        _post(() => StateChanged?.Invoke());
    }

    // ----- The site -----

    void Configure(WebApplication app, CancellationToken stopping)
    {
        app.UseWebSockets();
        app.MapGet("/", (HttpContext context) =>
        {
            if (context.Request.Query.TryGetValue("code", out var typed))
            {
                switch (CheckCode(typed.ToString()))
                {
                    case CodeCheck.Right:
                        context.Response.Cookies.Append(CookieName, _code, new CookieOptions
                        {
                            HttpOnly = true,
                            SameSite = SameSiteMode.Strict,
                            MaxAge = TimeSpan.FromDays(365),
                            IsEssential = true,
                        });
                        return Results.Redirect("/");
                    case CodeCheck.Locked:
                        return CodePage(_api.Session.Language.T("Previše pogrešnih pokušaja. Probaj ponovo za minut.", "Too many wrong tries. Try again in a minute."));
                    default:
                        return CodePage(_api.Session.Language.T("Pogrešan kod.", "Wrong code."));
                }
            }
            return Authorized(context) ? Results.File(Path.Combine(_uiFolder, LanPage), "text/html; charset=utf-8") : CodePage(null);
        });
        // The page asks before connecting, so a forgotten or changed code sends it back to the code.
        app.MapGet("/ping", (HttpContext context) => Authorized(context) ? Results.NoContent() : Results.Unauthorized());
        // As a Delegate, so its result is written (a RequestDelegate would drop it).
        app.MapPost("/", (Func<HttpContext, Task<IResult>>)Upload);
        app.Map("/socket", async context =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
            if (!Authorized(context) || !SameOrigin(context))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(stopping, context.RequestAborted);
            await Serve(socket, linked.Token);
        });
        app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(_uiFolder) });
    }

    bool Authorized(HttpContext context) =>
        context.Request.Cookies.TryGetValue(CookieName, out var cookie) && cookie is not null && Same(cookie, _code);

    /// <summary>Another site the phone opens must not reach the page with its cookie.</summary>
    static bool SameOrigin(HttpContext context) =>
        context.Request.Headers.Origin.ToString() is not { Length: > 0 } origin
        || string.Equals(origin, $"{context.Request.Scheme}://{context.Request.Host}", StringComparison.OrdinalIgnoreCase);

    static bool Same(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    enum CodeCheck { Right, Wrong, Locked }

    /// <summary>A few wrong tries lock it for a minute, so the 6 digits can't be guessed.</summary>
    CodeCheck CheckCode(string typed)
    {
        lock (_lock)
        {
            if (DateTime.UtcNow < _lockedUntil)
                return CodeCheck.Locked;
            if (_code.Length > 0 && Same(typed.Trim(), _code))
            {
                _failures = 0;
                return CodeCheck.Right;
            }
            if (++_failures >= MaxFailures)
            {
                _failures = 0;
                _lockedUntil = DateTime.UtcNow + LockOut;
            }
            return CodeCheck.Wrong;
        }
    }

    IResult CodePage(string? error)
    {
        var l = _api.Session.Language;
        static string E(string text) => WebUtility.HtmlEncode(text);
        var html = $$"""
            <!doctype html>
            <html lang="{{l.Code()}}" data-bs-theme="dark">
            <head>
                <meta charset="utf-8" />
                <meta name="viewport" content="width=device-width, initial-scale=1" />
                <title>The Mahala Workshop CNC</title>
                <link rel="icon" href="favicon.ico" sizes="any" />
                <link rel="stylesheet" href="lib/bootstrap/dist/css/bootstrap.min.css" />
                <link rel="stylesheet" href="css/site.css" />
            </head>
            <body>
                <header class="statusbar">
                    <div class="brand">
                        <span class="brand-name">THE <span>MAHALA</span> WORKSHOP</span>
                        <span class="brand-tool">CNC Controller</span>
                    </div>
                </header>
                <main class="page">
                    <section class="card" style="max-width: 24rem; margin: 2rem auto">
                        <div class="card-body">
                            <h1 class="card-title mb-2">{{E(l.T("Kod za pristup", "Access code"))}}</h1>
                            <p class="small text-muted">{{E(l.T(
                                "Upiši kod od 6 cifara koji piše u desktop programu, kod „Lokalna mreža“. Na ovom uređaju se upisuje samo jednom.",
                                "Enter the 6-digit code shown in the desktop program, under \"Local network\". It is entered only once on this device."))}}</p>
                            {{(error is null ? "" : $"<div class=\"alert alert-warning py-2 small\">{E(error)}</div>")}}
                            <form method="get" action="/">
                                <input name="code" class="form-control mb-2" inputmode="numeric" autocomplete="one-time-code" maxlength="6" required autofocus
                                       aria-label="{{E(l.T("Kod", "Code"))}}" />
                                <button type="submit" class="btn btn-wood w-100">{{E(l.T("Otvori", "Open"))}}</button>
                            </form>
                        </div>
                    </section>
                </main>
            </body>
            </html>
            """;
        return Results.Content(html, "text/html; charset=utf-8", Encoding.UTF8,
            error is null ? StatusCodes.Status200OK : StatusCodes.Status401Unauthorized);
    }

    /// <summary>A G-code file from the page's form, loaded like one uploaded on the website.</summary>
    async Task<IResult> Upload(HttpContext context)
    {
        if (!Authorized(context) || !SameOrigin(context))
            return Results.Unauthorized();
        if (context.Request.Query["handler"] != "Upload" || !context.Request.HasFormContentType)
            return Results.BadRequest();
        var l = _api.Session.Language;
        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        if (form.Files.GetFile("file") is not { Length: > 0 } file)
            return Results.Json(new UploadReply(false, l.T("Izaberi fajl.", "Choose a file.")), Json);
        if (file.Length > RemoteContract.MaxUploadBytes)
            return Results.Json(new UploadReply(false, l.T("Fajl je veći od ", "The file is larger than ") + $"{RemoteContract.MaxUploadBytes / (1024 * 1024)} MB."), Json);
        using var content = new MemoryStream((int)file.Length);
        await file.CopyToAsync(content, context.RequestAborted);
        var uploaded = new UploadedFile(file.FileName, content.ToArray());
        var reply = await _api.OnSessionThread(() => _api.LoadUploaded(uploaded, Origin));
        return Results.Json(reply, Json);
    }

    // ----- One page, over its WebSocket -----

    async Task Serve(WebSocket socket, CancellationToken cancel)
    {
        var outbox = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
        LocalPage? page = null;
        // Made on the session's thread; the page's messages are posted after it, so they find it there.
        _post(() => page = new LocalPage(_api, _post, json => outbox.Writer.TryWrite(json), Origin, [.. _recentLog]));
        var sending = SendLoop(socket, outbox.Reader, cancel);
        try
        {
            var buffer = new byte[16 * 1024];
            var message = new MemoryStream();
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, cancel);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;
                message.Write(buffer, 0, result.Count);
                if (message.Length > MaxMessageBytes)
                    break;
                if (!result.EndOfMessage)
                    continue;
                var text = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                message.SetLength(0);
                _post(() => page?.Receive(text));
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or IOException)
        {
            // The phone went away (screen off, out of the WiFi) or the server is stopping.
        }
        finally
        {
            _post(() => page?.Dispose());
            outbox.Writer.TryComplete();
            await sending;
            if (socket.State == WebSocketState.Open)
            {
                try
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(1));
                }
                catch (Exception ex) when (ex is WebSocketException or TimeoutException or IOException)
                {
                }
            }
        }
    }

    static async Task SendLoop(WebSocket socket, ChannelReader<string> outbox, CancellationToken cancel)
    {
        try
        {
            await foreach (var json in outbox.ReadAllAsync(cancel))
            {
                if (socket.State != WebSocketState.Open)
                    return;
                await socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, cancel);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or IOException)
        {
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
