namespace GrblCore;

public enum JobState
{
    Ready,
    Running,
    /// <summary>Feed hold - by the user, or automatically after Grbl rejected a line.</summary>
    Paused,
    /// <summary>
    /// Sending stopped at a line boundary and the laser was turned off (M5 after the lines already sent),
    /// e.g. while the compressors start. <see cref="GCodeJob.EndBreak"/> turns the laser back on and continues.
    /// </summary>
    Break,
    /// <summary>All lines accepted and the machine went back to Idle.</summary>
    Completed,
    /// <summary>Stopped by the user. Lines already in Grbl's buffer still ran.</summary>
    Stopped,
    /// <summary>Connection lost or Grbl was reset during the job; see <see cref="GCodeJob.AbortReason"/>.</summary>
    Aborted,
}

public enum JobAbortReason
{
    ConnectionLost,
    /// <summary>Grbl was reset from the program (Ctrl-X).</summary>
    Reset,
    /// <summary>Grbl restarted on its own: power glitch (compressors) or USB reset. The machine position is usually lost.</summary>
    GrblRestarted,
}

/// <summary>
/// Streams a <see cref="GCodeProgram"/> to Grbl. While a job is active nothing else may send lines
/// through the connection, because progress is counted from Grbl's answers in order.
/// Realtime commands (status, reset) are fine. Events are raised on a background thread.
/// </summary>
public sealed class GCodeJob : IDisposable
{
    /// <summary>Lines kept queued ahead of Grbl - plenty to keep its 128-byte buffer full.</summary>
    const int FeedAhead = 20;

    const string LaserOff = "M5";

    enum SentKind { Preamble, Program, Extra }

    readonly GrblConnection _grbl;
    readonly object _sync = new();
    readonly ResumePlan? _resume;
    readonly IReadOnlyList<string> _preamble;
    int _preambleSent;
    int _nextToSend;
    int _completed;
    // Everything sent and not yet answered, in order. Grbl answers in the order lines were sent.
    readonly Queue<(SentKind Kind, string Text)> _sent = new();
    // Lines of our own (break M5, laser back on) waiting to be sent before the next program line.
    readonly Queue<string> _extra = new();
    // "M3"/"M4" if the laser is on after the lines sent so far, as the program left it.
    string? _laser;
    bool _breakSettled;

    public GCodeProgram Program { get; }
    public JobState State { get; private set; } = JobState.Ready;

    public bool IsActive => State is JobState.Running or JobState.Paused or JobState.Break;

    /// <summary>Why the job was aborted; meaningful once <see cref="State"/> is <see cref="JobState.Aborted"/>.</summary>
    public JobAbortReason AbortReason { get; private set; }

    /// <summary>Index of the first line sent; 0 unless the job continues an earlier one.</summary>
    public int StartIndex { get; }

    /// <summary>Lines Grbl has accepted (answered "ok" or "error"), counting the skipped lines before <see cref="StartIndex"/>.</summary>
    public int CompletedCount
    {
        get { lock (_sync) return _completed; }
    }

    /// <summary>Program lines handed to the connection so far (accepted or not), counting the skipped lines before <see cref="StartIndex"/>.</summary>
    public int SentCount
    {
        get { lock (_sync) return _nextToSend; }
    }

    public int TotalCount => Program.Lines.Count;

    /// <summary>Last line Grbl accepted in this job, or null before the first one.</summary>
    public GCodeLine? LastCompletedLine
    {
        get { lock (_sync) return _completed == StartIndex ? null : Program.Lines[_completed - 1]; }
    }

    /// <summary>
    /// During a <see cref="JobState.Break"/>: true once Grbl answered the break's M5. Grbl 0.8c answers M5 only
    /// after every move before it has run, so the machine is standing still with the laser off.
    /// </summary>
    public bool IsBreakSettled
    {
        get { lock (_sync) return State == JobState.Break && _breakSettled; }
    }

    /// <summary>
    /// Asked before each program line is sent, with its index (on a background thread, keep it quick).
    /// Returning true starts a <see cref="JobState.Break"/> instead of sending the line.
    /// </summary>
    public Func<int, bool>? BreakBeforeLine { get; set; }

    public event Action<JobState>? StateChanged;
    /// <summary>Argument: <see cref="CompletedCount"/>.</summary>
    public event Action<int>? ProgressChanged;
    /// <summary>
    /// Grbl rejected a line; the job pauses. Arguments: the line, Grbl's error text.
    /// For a preamble line of a resumed job the line number is 0 and the job stops instead,
    /// because the laser may not be where the program expects it.
    /// </summary>
    public event Action<GCodeLine, string>? LineFailed;

    /// <param name="resume">Continue from <see cref="ResumePlan.StartIndex"/> after sending the plan's preamble; null to run the whole program.</param>
    /// <param name="setup">
    /// Lines sent before anything else, e.g. the laser power ("S1000") for Grbl 0.9+, where S is 0 after a reset.
    /// A rejected one stops the job like a rejected preamble line.
    /// </param>
    public GCodeJob(GrblConnection grbl, GCodeProgram program, ResumePlan? resume = null, IReadOnlyList<string>? setup = null)
    {
        if (!program.CanRun)
            throw new ArgumentException("Program contains lines that are too long for Grbl.", nameof(program));

        _grbl = grbl;
        Program = program;
        _resume = resume;
        _preamble = [.. setup ?? [], .. resume?.Preamble ?? []];
        StartIndex = _nextToSend = _completed = resume?.StartIndex ?? 0;
        _grbl.Acknowledged += OnAcknowledged;
        _grbl.StatusReceived += OnStatus;
        _grbl.Disconnected += OnDisconnected;
        _grbl.Connected += OnGrblRestarted;
    }

    public void Start()
    {
        if (!_grbl.IsReady)
            throw new InvalidOperationException("Grbl is not ready.");
        if (!TryChangeState(JobState.Running, JobState.Ready))
            throw new InvalidOperationException("Job was already started.");
        Feed();
    }

    public void Pause()
    {
        if (TryChangeState(JobState.Paused, JobState.Running))
            _grbl.FeedHold();
    }

    public void Resume()
    {
        if (!TryChangeState(JobState.Running, JobState.Paused))
            return;
        _grbl.CycleStart();
        Feed();
    }

    /// <summary>Ends a <see cref="JobState.Break"/>: the laser goes back on if the program had it on, and sending continues.</summary>
    public void EndBreak()
    {
        lock (_sync)
        {
            if (State != JobState.Break)
                return;
            State = JobState.Running;
            _breakSettled = false;
            if (_laser is { } on)
                _extra.Enqueue(on);
        }
        StateChanged?.Invoke(JobState.Running);
        Feed();
    }

    /// <summary>Stops sending. Lines already in Grbl's buffer (a few short moves) still run; use soft reset to stop immediately.</summary>
    public void Stop()
    {
        if (TryChangeState(JobState.Stopped, JobState.Running, JobState.Paused, JobState.Break))
            _grbl.ClearPending();
    }

    public void Dispose()
    {
        _grbl.Acknowledged -= OnAcknowledged;
        _grbl.StatusReceived -= OnStatus;
        _grbl.Disconnected -= OnDisconnected;
        _grbl.Connected -= OnGrblRestarted;
    }

    void Feed()
    {
        bool breakStarted = false;
        // Sending happens under the lock so lines from the UI thread and the reader thread can't swap places.
        lock (_sync)
        {
            try
            {
                while (IsActive && _extra.Count > 0 && _grbl.PendingCount < FeedAhead)
                    Send(SentKind.Extra, _extra.Dequeue());
                while (State == JobState.Running && _preambleSent < _preamble.Count && _grbl.PendingCount < FeedAhead)
                {
                    Send(SentKind.Preamble, _preamble[_preambleSent]);
                    _preambleSent++;
                }
                while (State == JobState.Running && _preambleSent == _preamble.Count && _nextToSend < Program.Lines.Count && _grbl.PendingCount < FeedAhead)
                {
                    if (BreakBeforeLine?.Invoke(_nextToSend) == true)
                    {
                        State = JobState.Break;
                        _breakSettled = false;
                        breakStarted = true;
                        Send(SentKind.Extra, LaserOff);
                        break;
                    }
                    Send(SentKind.Program, _resume is not null && _nextToSend == _resume.StartIndex ? _resume.FirstLine : Program.Lines[_nextToSend].Text);
                    _nextToSend++;
                }
            }
            catch (InvalidOperationException)
            {
                // Connection dropped; OnDisconnected aborts the job.
            }
        }
        if (breakStarted)
            StateChanged?.Invoke(JobState.Break);
    }

    void Send(SentKind kind, string text)
    {
        _grbl.SendLine(text);
        _sent.Enqueue((kind, text));
        if (kind != SentKind.Extra)
            _laser = LaserAfter(text, _laser);
    }

    /// <summary>Laser state after a line, from its M3/M4/M5 words.</summary>
    static string? LaserAfter(string line, string? laser)
    {
        foreach (var w in GCodeWord.Parse(line))
        {
            if (w.Is('M', 3) || w.Is('M', 4))
                laser = w.Is('M', 3) ? "M3" : "M4";
            else if (w.Is('M', 5))
                laser = null;
        }
        return laser;
    }

    void OnAcknowledged(string sentText, string? error)
    {
        GCodeLine line;
        int completed;
        SentKind kind;
        lock (_sync)
        {
            if (!IsActive || !_sent.TryDequeue(out var sent))
                return;
            kind = sent.Kind;
            if (kind == SentKind.Program)
            {
                line = Program.Lines[_completed];
                completed = ++_completed;
            }
            else
            {
                line = new GCodeLine(0, sent.Text);
                completed = _completed;
                // The break's M5 is the last line sent; its answer means everything before it has run.
                if (kind == SentKind.Extra && State == JobState.Break && _sent.Count == 0 && _extra.Count == 0)
                    _breakSettled = true;
            }
        }

        if (kind != SentKind.Program)
        {
            if (error is null)
            {
                Feed();
            }
            else
            {
                Stop();
                LineFailed?.Invoke(line, error);
            }
            return;
        }

        ProgressChanged?.Invoke(completed);
        if (error is not null)
        {
            LineFailed?.Invoke(line, error);
            Pause();
        }
        else
        {
            Feed();
        }
    }

    void OnStatus(GrblStatus status)
    {
        bool done;
        lock (_sync)
            done = State == JobState.Running && _completed == Program.Lines.Count && _sent.Count == 0 && status.State == MachineState.Idle;
        if (done)
            TryChangeState(JobState.Completed, JobState.Running);
    }

    void OnDisconnected(DisconnectReason reason, string? detail) => Abort(JobAbortReason.ConnectionLost);

    void OnGrblRestarted(GrblVersion version) =>
        Abort(_grbl.LastStartRequested ? JobAbortReason.Reset : JobAbortReason.GrblRestarted);

    void Abort(JobAbortReason reason)
    {
        lock (_sync)
        {
            if (!IsActive)
                return;
            AbortReason = reason;
            State = JobState.Aborted;
        }
        StateChanged?.Invoke(JobState.Aborted);
    }

    bool TryChangeState(JobState to, params JobState[] from)
    {
        lock (_sync)
        {
            if (!from.Contains(State))
                return false;
            State = to;
        }
        StateChanged?.Invoke(to);
        return true;
    }
}
