using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace CncController.App.Tests;

/// <summary><see cref="LanServer"/>: the page in the local network, over real HTTP and a WebSocket on this computer.</summary>
public sealed class LanServerTests : IAsyncLifetime
{
    const string Code = "482913";
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    readonly Dispatcher _ui = new();
    readonly MachineSession _session;
    readonly LanServer _server;
    readonly string _uiFolder = Path.Combine(Path.GetTempPath(), $"lan-ui-{Guid.NewGuid():N}");
    readonly ConcurrentQueue<string> _log = new();
    readonly CookieContainer _cookies = new();
    readonly HttpClient _http;

    public LanServerTests()
    {
        Directory.CreateDirectory(Path.Combine(_uiFolder, "css"));
        File.WriteAllText(Path.Combine(_uiFolder, LanServer.LanPage), "<!doctype html><title>lan page</title>");
        File.WriteAllText(Path.Combine(_uiFolder, "css", "site.css"), "body { }");
        _session = new MachineSession(new AppSettings(), new PositionMemory(), logFolder: null, _ui.Post);
        _session.Logged += (_, text) => _log.Enqueue(text);
        var api = new PageApi(_session, _ui.Post, Path.Combine(_uiFolder, "uploads"));
        _server = new LanServer(api, _ui.Post, _uiFolder);
        _http = new HttpClient(new HttpClientHandler { CookieContainer = _cookies, AllowAutoRedirect = false });
    }

    public Task InitializeAsync() => _server.StartAsync(0, Code, IPAddress.Loopback);

    public async Task DisposeAsync()
    {
        await _server.StopAsync();
        _http.Dispose();
        _ui.Invoke(_session.Dispose);
        _ui.Dispose();
        Directory.Delete(_uiFolder, recursive: true);
    }

    Uri At(string path) => new($"http://127.0.0.1:{_server.Port}{path}");

    async Task EnterCode(string code = Code)
    {
        var response = await _http.GetAsync(At($"/?code={code}"));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    async Task<ClientWebSocket> OpenSocket(string? origin = null)
    {
        var socket = new ClientWebSocket();
        socket.Options.Cookies = _cookies;
        if (origin is not null)
            socket.Options.SetRequestHeader("Origin", origin);
        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{_server.Port}/socket"), CancellationToken.None);
        return socket;
    }

    static Task Send(ClientWebSocket socket, object message) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message)), WebSocketMessageType.Text, true, CancellationToken.None);

    /// <summary>Reads messages until one matches.</summary>
    static async Task<JsonElement> Receive(ClientWebSocket socket, Func<JsonElement, bool> match)
    {
        using var cancel = new CancellationTokenSource(Timeout);
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var message = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, cancel.Token);
                message.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);
            var json = JsonDocument.Parse(message.ToArray()).RootElement;
            if (match(json))
                return json;
        }
    }

    static bool IsEvent(JsonElement m, string name) => m.GetProperty("type").GetString() == "event" && m.GetProperty("name").GetString() == name;

    [Fact]
    public async Task WithoutTheCode_OnlyTheCodePage()
    {
        var page = await _http.GetAsync(At("/"));
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Kod za pristup", await page.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await _http.GetAsync(At("/ping"))).StatusCode);
        await Assert.ThrowsAsync<WebSocketException>(() => OpenSocket());
        // The page's files themselves are no secret.
        Assert.Equal(HttpStatusCode.OK, (await _http.GetAsync(At("/css/site.css"))).StatusCode);
    }

    [Fact]
    public async Task RightCode_RememberedByTheDevice()
    {
        await EnterCode();

        var page = await _http.GetAsync(At("/"));
        Assert.Contains("lan page", await page.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NoContent, (await _http.GetAsync(At("/ping"))).StatusCode);
    }

    [Fact]
    public async Task WrongCodes_LockForAMinute()
    {
        var wrong = await _http.GetAsync(At("/?code=111111"));
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Contains("Pogrešan kod.", await wrong.Content.ReadAsStringAsync());
        for (int i = 0; i < 4; i++)
            await _http.GetAsync(At("/?code=111111"));

        var right = await _http.GetAsync(At($"/?code={Code}"));

        Assert.Equal(HttpStatusCode.Unauthorized, right.StatusCode);
        Assert.Contains("Previše pogrešnih pokušaja", await right.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task NewCode_DevicesMustEnterItAgain()
    {
        await EnterCode();

        await _server.StartAsync(_server.Port, "123456", IPAddress.Loopback);

        Assert.Equal(HttpStatusCode.Unauthorized, (await _http.GetAsync(At("/ping"))).StatusCode);
    }

    [Fact]
    public async Task Socket_GetsTheStateAndRunsCommands_LoggedAsFromTheLocalNetwork()
    {
        await EnterCode();
        using var socket = await OpenSocket();

        await Send(socket, new { type = "ready" });

        var desktop = await Receive(socket, m => IsEvent(m, "Desktop"));
        Assert.True(desktop.GetProperty("args")[0].GetProperty("online").GetBoolean());
        var snapshot = await Receive(socket, m => IsEvent(m, "Snapshot"));
        Assert.Equal("NIJE SPOJENO", snapshot.GetProperty("args")[0].GetProperty("stateText").GetString());

        await Send(socket, new { type = "invoke", id = 7, method = "Command", args = new object[] { new { name = "CompressorPause", value = true } } });

        var reply = await Receive(socket, m => m.GetProperty("type").GetString() == "reply");
        Assert.Equal(7, reply.GetProperty("id").GetInt32());
        Assert.True(reply.GetProperty("result").GetProperty("done").GetBoolean());
        Assert.True(_ui.Invoke(() => _session.CompressorPause));
        Assert.Contains(_log, l => l == "Iz lokalne mreže: Planirana pauza uključena.");
    }

    [Fact]
    public async Task Socket_GetsTheLogFromBeforeItConnected()
    {
        _ui.Invoke(() => _session.Log("before the phone"));
        await EnterCode();
        using var socket = await OpenSocket();

        await Send(socket, new { type = "ready" });

        var log = await Receive(socket, m => IsEvent(m, "Log"));
        Assert.Contains(log.GetProperty("args")[0].EnumerateArray(), l => l.GetProperty("text").GetString() == "before the phone");
    }

    [Fact]
    public async Task Socket_FromAnotherSite_IsRefused()
    {
        await EnterCode();

        await Assert.ThrowsAsync<WebSocketException>(() => OpenSocket(origin: "http://evil.example"));
    }

    [Fact]
    public async Task Upload_LoadsTheFile()
    {
        await EnterCode();
        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes("G21 G90\nG1 X10 Y0 F500\nG1 X10 Y10\n")), "file", "square.tap" },
        };

        var response = await _http.PostAsync(At("/?handler=Upload"), form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("done").GetBoolean());
        Assert.Equal("square.tap", Path.GetFileName(_ui.Invoke(() => _session.ProgramPath)));
        Assert.Contains(_log, l => l.StartsWith("Iz lokalne mreže: učitavam fajl square.tap"));
    }

    [Fact]
    public async Task Upload_WithoutTheCode_IsRefused()
    {
        using var form = new MultipartFormDataContent { { new ByteArrayContent([1, 2, 3]), "file", "x.tap" } };

        var response = await _http.PostAsync(At("/?handler=Upload"), form);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(_ui.Invoke(() => _session.ProgramPath));
    }

    [Fact]
    public async Task PortInUse_SaysSo()
    {
        using var taken = new TcpListener(IPAddress.Loopback, 0);
        taken.Start();
        int port = ((IPEndPoint)taken.LocalEndpoint).Port;

        await _server.StartAsync(port, Code, IPAddress.Loopback);

        Assert.Equal(LanState.Failed, _server.State);
        Assert.Equal($"Lokalna mreža: ne radi (port {port} je zauzet)", _server.StateText);
    }

    [Fact]
    public void NewCode_IsSixDigits() =>
        Assert.Matches("^[0-9]{6}$", LanServer.NewCode());
}
