using System.Text;

namespace GrblCore;

public enum DisconnectReason
{
    /// <summary>The COM port disappeared, usually a pulled USB cable.</summary>
    PortRemoved,
    /// <summary>Reading from the port failed; see the detail message.</summary>
    ReadError,
}

/// <summary>
/// Connection to Grbl over a serial port or the simulator (<see cref="IGrblTransport"/>).
/// Normal lines go through a queue and are streamed with character counting (Grbl's 128-byte RX buffer).
/// Realtime commands ('?', '!', '~', Ctrl-X) bypass the queue and are written immediately,
/// so pause/resume and status work even in the middle of a job.
/// All events are raised on a background thread.
/// </summary>
public sealed class GrblConnection : IDisposable
{
    /// <summary>Size of Grbl's serial RX buffer.</summary>
    public const int RxBufferSize = 128;

    /// <summary>Moves Grbl 0.8c's planner holds (18 blocks, one always free). A line's "ok" means it is in there, not that it ran.</summary>
    public const int PlannerSize = 17;

    const byte CtrlX = 0x18;

    readonly object _sync = new();
    readonly object _writeLock = new();
    readonly Queue<string> _pending = new();
    readonly Queue<string> _inFlight = new();
    int _inFlightBytes;

    IGrblTransport? _port;
    // Grbl 1.1 reports the work offset only now and then; the reports in between are completed with it.
    Position? _workOffset;
    Thread? _readerThread;
    Timer? _statusTimer;
    Timer? _bannerTimer;
    volatile bool _closing;

    /// <summary>Firmware version; null until Grbl's startup banner has been received.</summary>
    public GrblVersion? Version { get; private set; }

    public bool IsOpen => _port?.IsOpen == true;

    /// <summary>True once Grbl has announced itself and accepts commands.</summary>
    public bool IsReady => IsOpen && Version is not null;

    /// <summary>How often status is asked for ('?'). Takes effect at once, also while connected.</summary>
    public int StatusIntervalMs
    {
        get => _statusIntervalMs;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _statusIntervalMs = value;
            try { _statusTimer?.Change(value, value); }
            catch (ObjectDisposedException) { } // connection closing
        }
    }
    int _statusIntervalMs = 500;

    /// <summary>
    /// For the latest banner: true if it answered a soft reset sent from here (Reset, or waking Grbl up on connect),
    /// false if Grbl restarted on its own (power glitch, USB reset). Set before <see cref="Connected"/> is raised.
    /// </summary>
    public bool LastStartRequested { get; private set; }
    volatile bool _softResetSent;

    /// <summary>Lines not yet acknowledged by Grbl (queued here or sitting in Grbl's buffer).</summary>
    public int PendingCount
    {
        get { lock (_sync) return _pending.Count + _inFlight.Count; }
    }

    public event Action<GrblVersion>? Connected;
    /// <summary>Connection lost without <see cref="Close"/> being called. Arguments: reason, error detail if any.</summary>
    public event Action<DisconnectReason, string?>? Disconnected;
    /// <summary>Every line from Grbl except status reports.</summary>
    public event Action<string>? LineReceived;
    public event Action<string>? LineSent;
    public event Action<GrblStatus>? StatusReceived;
    /// <summary>
    /// Grbl answered a line. Arguments: the line that was sent, Grbl's error text (null for "ok").
    /// "ok" means Grbl accepted the line into its planner, not that the move is finished.
    /// </summary>
    public event Action<string, string?>? Acknowledged;

    /// <summary>Opens a COM port. Throws IOException, UnauthorizedAccessException or ArgumentException if it can't be opened.</summary>
    public void Open(string portName, int baudRate)
    {
        if (_port is not null)
            throw new InvalidOperationException("Connection is already open.");
        Open(SerialTransport.Open(portName, baudRate));
    }

    /// <summary>Starts talking to Grbl over an already open transport, e.g. the simulator. The connection disposes it on close.</summary>
    public void Open(IGrblTransport transport)
    {
        if (_port is not null)
            throw new InvalidOperationException("Connection is already open.");

        _closing = false;
        Version = null;
        _workOffset = null;
        _softResetSent = false;
        ClearQueues();
        _port = transport;

        _readerThread = new Thread(ReadLoop) { IsBackground = true, Name = "Grbl reader" };
        _readerThread.Start();

        // Opening the port usually resets the Arduino and Grbl prints its banner.
        // If it doesn't, a soft reset makes Grbl announce itself (same as Grbl Controller does).
        _bannerTimer = new Timer(_ => { if (IsOpen && Version is null) SoftReset(); }, null, 2500, Timeout.Infinite);
    }

    public void Close()
    {
        _closing = true;
        Cleanup();
        _readerThread?.Join(1000);
        _readerThread = null;
    }

    public void Dispose() => Close();

    /// <summary>Longest line (without newline) that fits into Grbl's RX buffer.</summary>
    public const int MaxLineLength = RxBufferSize - 2;

    /// <summary>Queues a G-code line or Grbl command ($X, $$...).</summary>
    public void SendLine(string line)
    {
        line = line.Trim();
        if (line.Length == 0)
            return;
        if (line.Length > MaxLineLength)
            throw new ArgumentException($"Line is longer than {MaxLineLength} characters.", nameof(line));
        if (!IsReady)
            throw new InvalidOperationException("Grbl is not ready.");

        lock (_sync)
            _pending.Enqueue(line);
        Pump();
    }

    /// <summary>Drops lines not yet sent to Grbl. Lines already in Grbl's buffer still run.</summary>
    public void ClearPending()
    {
        lock (_sync)
            _pending.Clear();
    }

    /// <summary>Pause ('!'). Motion stops with deceleration; the spindle keeps running.</summary>
    public void FeedHold() => WriteRealtime((byte)'!');

    /// <summary>Resume after pause ('~').</summary>
    public void CycleStart() => WriteRealtime((byte)'~');

    public void RequestStatus() => WriteRealtime((byte)'?');

    /// <summary>Ctrl-X. Stops everything immediately; Grbl forgets its position and buffered lines.</summary>
    public void SoftReset()
    {
        ClearQueues();
        _softResetSent = true;
        WriteRealtime(CtrlX);
    }

    void ReadLoop()
    {
        var port = _port!;
        var reason = DisconnectReason.ReadError;
        string? detail = null;

        while (!_closing)
        {
            string line;
            try
            {
                if (port.ReadLine() is not { } received)
                {
                    // A pulled USB cable doesn't always throw; sometimes the port just disappears.
                    if (!port.IsPresent)
                    {
                        reason = DisconnectReason.PortRemoved;
                        break;
                    }
                    continue;
                }
                line = received.Trim();
            }
            // Close() closing the port cancels the read in progress (OperationCanceledException,
            // or ObjectDisposedException if the port is already gone); the loop then ends because _closing is set.
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException
                                           or OperationCanceledException or ObjectDisposedException)
            {
                detail = ex.Message;
                break;
            }

            if (line.Length > 0)
                HandleLine(line);
        }

        if (!_closing)
        {
            Cleanup();
            Disconnected?.Invoke(reason, detail);
        }
    }

    void HandleLine(string line)
    {
        if (StatusParser.TryParse(line, out var status))
        {
            if (status.WorkOffset is { } offset)
                _workOffset = offset;
            else if (_workOffset is { } known)
                status = status.WithOffset(known);
            StatusReceived?.Invoke(status);
            return;
        }

        LineReceived?.Invoke(line);

        if (GrblVersion.TryParseBanner(line, out var version))
        {
            // Grbl (re)started: whatever was in its buffer is gone. Lines still waiting here are dropped
            // on purpose - continuing a job after an unexpected reset would cut in the wrong place.
            ClearQueues();
            // G92 is gone after a reset; 1.1 sends the new offset with its first report.
            _workOffset = null;
            Version = version;
            LastStartRequested = _softResetSent;
            _softResetSent = false;
            StartStatusPolling();
            Connected?.Invoke(version);
            return;
        }

        if (line == "ok" || line.StartsWith("error", StringComparison.OrdinalIgnoreCase))
        {
            string? sent = null;
            lock (_sync)
            {
                if (_inFlight.Count > 0)
                {
                    sent = _inFlight.Dequeue();
                    _inFlightBytes -= sent.Length + 1;
                }
            }
            Acknowledged?.Invoke(sent ?? "", line == "ok" ? null : line);
            Pump();
        }
    }

    /// <summary>Sends queued lines while they fit into Grbl's RX buffer.</summary>
    void Pump()
    {
        var sent = new List<string>();
        lock (_sync)
        {
            while (Version is not null && _pending.Count > 0 && _inFlightBytes + _pending.Peek().Length + 1 < RxBufferSize)
            {
                var line = _pending.Peek();
                if (!Write(Encoding.ASCII.GetBytes(line + "\n")))
                    break;
                _pending.Dequeue();
                _inFlight.Enqueue(line);
                _inFlightBytes += line.Length + 1;
                sent.Add(line);
            }
        }
        foreach (var line in sent)
            LineSent?.Invoke(line);
    }

    void WriteRealtime(byte command) => Write([command]);

    bool Write(byte[] data)
    {
        lock (_writeLock)
        {
            var port = _port;
            if (port is null || !port.IsOpen)
                return false;
            try
            {
                port.Write(data);
                return true;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException or UnauthorizedAccessException)
            {
                // The reader thread notices the broken connection and raises Disconnected.
                return false;
            }
        }
    }

    void StartStatusPolling()
    {
        _statusTimer?.Dispose();
        _statusTimer = new Timer(_ => RequestStatus(), null, 0, _statusIntervalMs);
    }

    void ClearQueues()
    {
        lock (_sync)
        {
            _pending.Clear();
            _inFlight.Clear();
            _inFlightBytes = 0;
        }
    }

    void Cleanup()
    {
        Interlocked.Exchange(ref _statusTimer, null)?.Dispose();
        Interlocked.Exchange(ref _bannerTimer, null)?.Dispose();

        var port = Interlocked.Exchange(ref _port, null);
        if (port is not null)
        {
            lock (_writeLock)
                port.Dispose();
        }
        ClearQueues();
        Version = null;
    }
}
