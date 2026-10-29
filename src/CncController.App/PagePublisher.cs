using CncController.Remote;

namespace CncController.App;

/// <summary>
/// Keeps one page up to date (the website's browsers through <see cref="RemoteLink"/>, or the desktop's own page):
/// remembers what changed since the last send and sends only that, a few times a second.
/// Use it on the session's thread, like <see cref="MachineSession"/>.
/// </summary>
public sealed class PagePublisher : IDisposable
{
    /// <summary>Lines of the log a page gets when it connects.</summary>
    public const int LogBufferSize = 300;

    readonly MachineSession _session;
    readonly PageApi _api;
    bool _snapshotDirty;
    bool _toolpathDirty;
    readonly List<LogLine> _newLog = [];
    readonly Queue<LogLine> _recentLog = new();

    /// <param name="recentLog">The log so far, for a page made after the program started (the local network's).</param>
    public PagePublisher(PageApi api, IEnumerable<LogLine>? recentLog = null)
    {
        _api = api;
        _session = api.Session;
        foreach (var line in recentLog ?? [])
            _recentLog.Enqueue(line);
        _session.Changed += MarkDirty;
        _session.StatusReceived += OnStatus;
        _session.ProgressChanged += MarkDirty;
        _session.ProgramChanged += OnProgramChanged;
        _session.Logged += OnLogged;
        _api.Changed += MarkDirty;
    }

    void MarkDirty() => _snapshotDirty = true;
    void OnStatus(GrblCore.GrblStatus _) => _snapshotDirty = true;
    void OnProgramChanged() => _toolpathDirty = _snapshotDirty = true;

    void OnLogged(DateTime time, string text)
    {
        var line = new LogLine(time, text);
        _newLog.Add(line);
        _recentLog.Enqueue(line);
        while (_recentLog.Count > LogBufferSize)
            _recentLog.Dequeue();
    }

    /// <summary>Everything, for a page that just connected (or reconnected): drawing, recent log, state.</summary>
    public void SendAll(Action<string, object?[]> send)
    {
        _newLog.Clear();
        _toolpathDirty = _snapshotDirty = false;
        send(RemoteContract.SendToolpath, [_api.BuildToolpath()]);
        send(RemoteContract.SendLog, [_recentLog.ToArray(), true]);
        send(RemoteContract.SendSnapshot, [_api.BuildSnapshot()]);
    }

    /// <summary>What changed since the last send.</summary>
    public void SendChanges(Action<string, object?[]> send)
    {
        // Notices a plugged-in USB cable even while nothing else changes.
        _api.Ports();
        if (_toolpathDirty)
        {
            _toolpathDirty = false;
            send(RemoteContract.SendToolpath, [_api.BuildToolpath()]);
        }
        if (_newLog.Count > 0)
        {
            send(RemoteContract.SendLog, [_newLog.ToArray(), false]);
            _newLog.Clear();
        }
        if (_snapshotDirty)
        {
            _snapshotDirty = false;
            send(RemoteContract.SendSnapshot, [_api.BuildSnapshot()]);
        }
    }

    /// <summary>While nothing can be sent: new lines are dropped, the recent log goes out in full with the next <see cref="SendAll"/>.</summary>
    public void Skip() => _newLog.Clear();

    public void Dispose()
    {
        _session.Changed -= MarkDirty;
        _session.StatusReceived -= OnStatus;
        _session.ProgressChanged -= MarkDirty;
        _session.ProgramChanged -= OnProgramChanged;
        _session.Logged -= OnLogged;
        _api.Changed -= MarkDirty;
    }
}
