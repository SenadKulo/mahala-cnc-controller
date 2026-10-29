using System.Text.Json;
using CncController.Remote;

namespace CncController.App;

/// <summary>
/// The machine page inside the desktop window (WebView2), or on a phone in the local network (<see cref="LanServer"/>):
/// the same page as on the website, talking to the session
/// directly instead of through the website. This side of it knows nothing of WebView2: it gets the page's messages
/// (<see cref="Receive"/>) and sends JSON through a function, so it can be tested without a window.
/// <para>
/// From the page: <c>{"type":"ready"}</c> once loaded, and <c>{"type":"invoke","id":1,"method":"Command","args":[…]}</c>.
/// To the page: <c>{"type":"event","name":"Snapshot","args":[…]}</c> (the same names as from the website) and
/// <c>{"type":"reply","id":1,"ok":true,"result":…}</c> or <c>{"type":"reply","id":1,"ok":false,"error":"…"}</c>.
/// </para>
/// Use it on the session's thread, like <see cref="MachineSession"/>.
/// </summary>
public sealed class LocalPage : IDisposable
{
    /// <summary>Sent once the page has everything; tells it which window it runs in and the window's own state.</summary>
    public const string HostEvent = "Host";

    static readonly TimeSpan SendInterval = TimeSpan.FromMilliseconds(200);
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    readonly PageApi _api;
    readonly string? _origin;
    readonly PagePublisher _publisher;
    readonly Action<Action> _post;
    readonly Action<string> _send;
    readonly Timer _timer;
    readonly Dictionary<string, Func<JsonElement[], object?>> _hostMethods = [];
    object? _host;
    bool _ready;

    /// <param name="post">Runs an action on the session's thread (also the thread that may send to the page).</param>
    /// <param name="send">Hands a JSON message to the page.</param>
    /// <param name="origin">For the log, like the website's commands (<see cref="LanServer.Origin"/>); null for the desktop's own window.</param>
    /// <param name="recentLog">The log so far (<see cref="PagePublisher"/>).</param>
    public LocalPage(PageApi api, Action<Action> post, Action<string> send, string? origin = null, IEnumerable<LogLine>? recentLog = null)
    {
        _api = api;
        _origin = origin;
        _publisher = new PagePublisher(api, recentLog);
        _post = post;
        _send = send;
        _timer = new Timer(_ => _post(Flush), null, SendInterval, SendInterval);
    }

    /// <summary>A method only the desktop window has (open a file with its dialog, its settings...).</summary>
    public void AddHostMethod(string name, Func<JsonElement[], object?> method) => _hostMethods[name] = method;

    /// <summary>The window's own state for the page (remote access, keyboard...); sent now and after every reload.</summary>
    public void SetHost(object host)
    {
        _host = host;
        if (_ready)
            SendEvent(HostEvent, [host]);
    }

    /// <summary>A message from the page.</summary>
    public void Receive(string json)
    {
        JsonElement message;
        try
        {
            message = JsonDocument.Parse(json).RootElement;
        }
        catch (JsonException)
        {
            return;
        }
        switch (message.TryGetProperty("type", out var type) ? type.GetString() : null)
        {
            case "ready":
                // Loaded or reloaded: everything again. The desktop is always "online" here: the page talks to it directly.
                _ready = true;
                SendEvent(RemoteContract.DesktopStatus, [new DesktopStatus(true, null)]);
                _publisher.SendAll(SendEvent);
                if (_host is not null)
                    SendEvent(HostEvent, [_host]);
                break;
            case "invoke" when message.TryGetProperty("id", out var id) && message.TryGetProperty("method", out var method):
                var args = message.TryGetProperty("args", out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray().ToArray() : [];
                _ = Invoke(id.GetInt32(), method.GetString() ?? "", args);
                break;
        }
    }

    async Task Invoke(int id, string method, JsonElement[] args)
    {
        object? result;
        try
        {
            result = method switch
            {
                RemoteContract.Command => _api.Execute(Arg<RemoteCommand>(args, 0) ?? new RemoteCommand(), _origin),
                RemoteContract.PreviewResume => _api.PreviewResume(Arg<int?>(args, 0)),
                RemoteContract.ReadGrblSettings => await _api.ReadGrblSettings(_origin),
                RemoteContract.WriteGrblSettings => await _api.WriteGrblSettings(Arg<GrblSettingValue[]>(args, 0) ?? [], _origin),
                _ when _hostMethods.TryGetValue(method, out var host) => host(args),
                _ => throw new InvalidOperationException($"Unknown method {method}."),
            };
        }
        catch (Exception ex)
        {
            // Whatever went wrong, the page must not wait forever for its answer.
            Reply(new { type = "reply", id, ok = false, error = ex.Message });
            return;
        }
        Reply(new { type = "reply", id, ok = true, result });
    }

    /// <summary>A reply may finish on another thread (Grbl settings); the page is only ever written to from the session's.</summary>
    void Reply(object reply)
    {
        var json = JsonSerializer.Serialize(reply, Json);
        _post(() => _send(json));
    }

    static T? Arg<T>(JsonElement[] args, int index) =>
        index < args.Length && args[index].ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            ? args[index].Deserialize<T>(Json)
            : default;

    void SendEvent(string name, object?[] args) => _send(JsonSerializer.Serialize(new { type = "event", name, args }, Json));

    void Flush()
    {
        if (_ready)
            _publisher.SendChanges(SendEvent);
        else
            _publisher.Skip();
    }

    public void Dispose()
    {
        _timer.Dispose();
        _publisher.Dispose();
    }
}
