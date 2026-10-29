using System.Threading.Channels;
using CncController.Remote;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace CncController.App;

public enum RemoteState
{
    Off,
    Connecting,
    Connected,
    /// <summary>The last attempt failed; it is tried again by itself.</summary>
    Failed,
}

/// <summary>
/// Remote access: the desktop connects to the website and keeps that connection open, sends the session's state
/// there and carries out commands from browsers (<see cref="PageApi"/>). Nothing in the session depends on it: if the
/// website or the internet goes away, the session runs on exactly as before, and the link reconnects by itself.
/// Use it on the session's thread, like <see cref="MachineSession"/>.
/// </summary>
public sealed class RemoteLink : IDisposable
{
    /// <summary>Marks what came from the website, for the log ("Sa weba: ...").</summary>
    public const string Origin = "web";

    static readonly TimeSpan SendInterval = TimeSpan.FromMilliseconds(200);
    static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(10);
    const int MaxPendingNotifications = 20;
    static readonly TimeSpan NotificationMaxAge = TimeSpan.FromHours(2);

    readonly MachineSession _session;
    readonly PageApi _api;
    readonly PagePublisher _publisher;
    readonly Action<Action> _post;
    readonly Func<HttpMessageHandler>? _handlerFactory;
    readonly Timer _timer;

    // Changed on the session's thread only.
    HubConnection? _connection;
    CancellationTokenSource? _stop;
    Channel<(string Method, object?[] Args)>? _outbox;
    bool _sendAll;
    readonly Queue<MachineNotification> _notifications = new();

    /// <param name="post">Runs an action on the session's thread (like the session's own).</param>
    /// <param name="handlerFactory">Tests: talk to an in-process test server instead of the network.</param>
    public RemoteLink(PageApi api, Action<Action> post, Func<HttpMessageHandler>? handlerFactory = null)
    {
        _api = api;
        _session = api.Session;
        _publisher = new PagePublisher(api);
        _post = post;
        _handlerFactory = handlerFactory;
        _session.Notified += OnNotified;
        _timer = new Timer(_ => _post(Flush), null, SendInterval, SendInterval);
    }

    public RemoteState State { get; private set; }

    /// <summary>Why the last attempt failed, while <see cref="State"/> is <see cref="RemoteState.Failed"/>.</summary>
    public string? Error { get; private set; }

    /// <summary>Raised on the session's thread.</summary>
    public event Action? StateChanged;

    public string StateText => _session.Language.T("Daljinski pristup: ", "Remote access: ") + State switch
    {
        RemoteState.Connecting => _session.Language.T("spajam se...", "connecting..."),
        RemoteState.Connected => _session.Language.T("spojen", "connected"),
        RemoteState.Failed => _session.Language.T($"nije spojen ({Error}), pokušavam ponovo...", $"not connected ({Error}), trying again..."),
        _ => _session.Language.T("isključen", "off"),
    };

    /// <summary>Connects to the website at <paramref name="url"/> and keeps reconnecting until <see cref="Stop"/>.</summary>
    public void Start(string url, string key)
    {
        Stop();
        var stop = _stop = new CancellationTokenSource();
        var outbox = _outbox = Channel.CreateUnbounded<(string, object?[])>(new UnboundedChannelOptions { SingleReader = true });

        var connection = _connection = new HubConnectionBuilder()
            .WithUrl(new Uri(new Uri(url), RemoteContract.DesktopHub), options =>
            {
                options.Headers[RemoteContract.DesktopKeyHeader] = key;
                if (_handlerFactory is { } factory)
                {
                    options.HttpMessageHandlerFactory = _ => factory();
                    options.Transports = HttpTransportType.LongPolling;
                }
            })
            .WithAutomaticReconnect(new ForeverRetry())
            .Build();

        // Each is answered once the session's thread has done it; the website waits for the answer.
        connection.On<RemoteCommand, CommandReply>(RemoteContract.Command, command => _api.OnSessionThread(() => _api.Execute(command, Origin)));
        connection.On<int?, ResumePreview?>(RemoteContract.PreviewResume, line => _api.OnSessionThread(() => _api.PreviewResume(line)));
        connection.On<UploadedFile, UploadReply>(RemoteContract.Upload, file => _api.OnSessionThread(() => _api.LoadUploaded(file, Origin)));
        connection.On<GrblSettingsReply>(RemoteContract.ReadGrblSettings, () => _api.ReadGrblSettings(Origin));
        connection.On<GrblSettingValue[], GrblSettingsReply>(RemoteContract.WriteGrblSettings, values => _api.WriteGrblSettings(values, Origin));
        connection.Reconnecting += error =>
        {
            _post(() => { if (_connection == connection) SetState(RemoteState.Connecting, null); });
            return Task.CompletedTask;
        };
        connection.Reconnected += _ =>
        {
            _post(() => { if (_connection == connection) OnConnected(); });
            return Task.CompletedTask;
        };
        connection.Closed += error =>
        {
            // Only when reconnecting gave up or the website refused us; start over unless stopped.
            if (!stop.IsCancellationRequested)
                _ = ConnectAsync(connection, stop.Token);
            return Task.CompletedTask;
        };

        SetState(RemoteState.Connecting, null);
        _ = ConnectAsync(connection, stop.Token);
        _ = SendLoopAsync(connection, outbox.Reader, stop.Token);
    }

    public void Stop()
    {
        _stop?.Cancel();
        _stop = null;
        _outbox?.Writer.TryComplete();
        _outbox = null;
        if (_connection is { } connection)
        {
            _connection = null;
            _ = connection.DisposeAsync().AsTask().ContinueWith(_ => { }, TaskScheduler.Default);
        }
        SetState(RemoteState.Off, null);
    }

    async Task ConnectAsync(HubConnection connection, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await connection.StartAsync(stop);
                _post(() => { if (_connection == connection) OnConnected(); });
                return;
            }
            catch (Exception ex) when (!stop.IsCancellationRequested)
            {
                var message = ex is HttpRequestException { StatusCode: System.Net.HttpStatusCode.Unauthorized }
                    ? _session.Language.T("sajt ne prihvata ključ", "the website doesn't accept the key")
                    : ex.Message;
                _post(() => { if (_connection == connection) SetState(RemoteState.Failed, message); });
            }
            catch (OperationCanceledException)
            {
                return;
            }
            try
            {
                await Task.Delay(RetryDelay, stop);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Sends in order, one message at a time. A failed send is dropped: after reconnecting everything is sent again.</summary>
    static async Task SendLoopAsync(HubConnection connection, ChannelReader<(string Method, object?[] Args)> outbox, CancellationToken stop)
    {
        try
        {
            await foreach (var (method, args) in outbox.ReadAllAsync(stop))
            {
                try
                {
                    await connection.SendCoreAsync(method, args, stop);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Connection dropped; it reconnects and resends the whole state.
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    void OnConnected()
    {
        SetState(RemoteState.Connected, null);
        // The website may have restarted (hosting recycles it): give it everything again.
        _sendAll = true;
        Flush();
    }

    void SetState(RemoteState state, string? error)
    {
        if (State == state && Error == error)
            return;
        State = state;
        Error = error;
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Kept until the site can be reached: a phone should still hear that the job finished at 3:00 when the internet
    /// came back at 3:20. Old ones are dropped, they no longer help.
    /// </summary>
    void OnNotified(string title, string text)
    {
        _notifications.Enqueue(new MachineNotification(title, text, DateTime.Now));
        while (_notifications.Count > MaxPendingNotifications)
            _notifications.Dequeue();
    }

    /// <summary>Sends what changed since the last time; called every <see cref="SendInterval"/> on the session's thread.</summary>
    void Flush()
    {
        if (State != RemoteState.Connected || _outbox is not { } outbox)
        {
            _publisher.Skip();
            return;
        }
        void Send(string method, object?[] args) => outbox.Writer.TryWrite((method, args));
        while (_notifications.TryDequeue(out var notification))
            if (DateTime.Now - notification.Time < NotificationMaxAge)
                Send(RemoteContract.SendNotification, [notification]);
        if (_sendAll)
        {
            _sendAll = false;
            _publisher.SendAll(Send);
        }
        else
        {
            _publisher.SendChanges(Send);
        }
    }

    public void Dispose()
    {
        _timer.Dispose();
        Stop();
        _session.Notified -= OnNotified;
        _publisher.Dispose();
    }

    /// <summary>Keeps trying: the desktop must find the website again however long it was away.</summary>
    sealed class ForeverRetry : IRetryPolicy
    {
        public TimeSpan? NextRetryDelay(RetryContext context) => context.PreviousRetryCount switch
        {
            0 => TimeSpan.Zero,
            1 => TimeSpan.FromSeconds(2),
            2 => TimeSpan.FromSeconds(5),
            _ => RetryDelay,
        };
    }
}
