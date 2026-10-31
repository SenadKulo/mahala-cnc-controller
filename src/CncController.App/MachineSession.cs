using GrblCore;
using GrblCore.Simulation;

namespace CncController.App;

public enum ConnectionStatus
{
    Disconnected,
    /// <summary>Port open, Grbl's banner not seen yet.</summary>
    WaitingForGrbl,
    Connected,
    /// <summary>The connection broke (USB pulled, read error).</summary>
    Lost,
}

public enum JobEventKind
{
    /// <summary>Stopped with the laser off while the compressors switched on.</summary>
    CompressorBreak,
    /// <summary>Grbl restarted by itself during the job.</summary>
    Restart,
}

/// <summary>Something that happened to a job: when, and how many of its lines Grbl had accepted by then.</summary>
/// <param name="Until">For a compressor break, when the job was to continue.</param>
public sealed record JobEvent(DateTime Time, JobEventKind Kind, int Line, DateTime? Until);

/// <summary>Grbl restarted and lost its position: should the remembered work position be restored?</summary>
/// <remarks>Its text is <see cref="Texts.Question"/>.</remarks>
public sealed record PositionQuestion(Position Current, Position Target, DateTime? SavedAt, string? JobFile, int? JobLine);

/// <summary>
/// Everything the program does with the machine, without any window: connection, jobs, the remembered position,
/// the automatic resume and the compressor pause. The desktop window (and later remote access) shows its state
/// and calls its methods, so the logic exists once.
/// Single-threaded: use it only on the thread behind <c>post</c> (the UI thread on the desktop).
/// Its events are raised on that thread too. Texts (log, questions, alerts) are in <see cref="Language"/>.
/// </summary>
public sealed class MachineSession : IDisposable
{
    /// <summary>Entry in the port list that connects to the simulated Grbl 0.8c (the user's machine) instead of a COM port.</summary>
    public const string SimulatorPortName = "Simulator";
    /// <summary>Entry in the port list that connects to a simulated Grbl 1.1, what most other boards run.</summary>
    public const string Simulator11PortName = "Simulator 1.1";

    /// <summary>The simulator entries, after the COM ports in the port list.</summary>
    public static readonly IReadOnlyList<string> SimulatorPortNames = [SimulatorPortName, Simulator11PortName];

    public static bool IsSimulatorPort(string? port) => port is not null && SimulatorPortNames.Contains(port);
    public const int DefaultBaud = 9600;
    /// <summary>Automatic resumes per job; after that a person has to look.</summary>
    public const int MaxAutoResumes = 10;

    // Asked more often during a job: if Grbl restarts mid-move, the last report is all there is to go on.
    const int JobStatusIntervalMs = 200;
    const int IdleStatusIntervalMs = 500;
    const int MaxIssuesShown = 10;
    /// <summary>Grbl must stay idle this long with a confirmed position before a job continues by itself.</summary>
    static readonly TimeSpan AutoResumeSettle = TimeSpan.FromSeconds(5);
    static readonly TimeSpan AutoResumeGiveUp = TimeSpan.FromSeconds(60);

    readonly Action<Action> _post;
    readonly Func<DateTime> _clock;
    readonly GrblConnection _grbl = new();
    readonly PositionMemory _memory;
    readonly LogFile? _logFile;
    readonly string? _logFolder;
    readonly System.Diagnostics.Stopwatch _jobClock = new();
    // Created on the first connect to the simulator and kept, so it remembers its position and settings like a real board.
    GrblSimulator? _simulator;

    int _baud = DefaultBaud;
    GCodeJob? _job;

    // After every (re)start Grbl may report a different work position than the one remembered.
    // Until that is checked, statuses must not overwrite the remembered position.
    bool _positionTrusted;
    bool _positionCheckPending;
    // G92 line whose "ok" makes the reported position trustworthy again.
    string? _positionCommand;
    // Restarts while the position question is open are only counted, not asked about again.
    int _restartsWhileAsking;
    // False until the first banner after connecting, which always answers our own wake-up reset.
    bool _grblStarted;
    // Our own lines (not a job's) Grbl hasn't answered yet. Only a status that came after all of them is current:
    // before that, the last one may be from before a move that is already running.
    int _linesUnanswered;
    bool _statusCurrent;

    // Automatic resume after Grbl restarted on its own: waiting since then, or null.
    DateTime? _autoResumeSince;
    DateTime? _autoResumeIdleSince;
    int _autoResumes;
    // Restarts of Grbl by itself during the job started last by hand, for the notification at its end.
    int _restartsThisJob;

    // Compressor pause: schedule in use (null = off), read by the job on the reader thread.
    volatile CompressorSchedule? _activeCompressors;
    DateTime? _breakStart;
    DateTime? _breakUntil;
    bool _breakSettledLogged;
    bool _breakLateLogged;

    /// <param name="logFolder">Folder for the daily log files; null to log only through <see cref="Logged"/>.</param>
    /// <param name="post">Runs an action on the session's thread, later (the desktop: <c>BeginInvoke</c>). Grbl's events arrive through it.</param>
    /// <param name="clock">Current time; tests pass their own.</param>
    public MachineSession(AppSettings settings, PositionMemory memory, string? logFolder, Action<Action> post, Func<DateTime>? clock = null)
    {
        Settings = settings;
        _memory = memory;
        _logFolder = logFolder;
        _logFile = logFolder is null ? null : new LogFile(logFolder);
        _post = post;
        _clock = clock ?? (() => DateTime.Now);

        _grbl.Connected += v =>
        {
            // Read right away: the next banner overwrites it.
            bool requested = _grbl.LastStartRequested;
            var time = _clock();
            _post(() => OnGrblConnected(v, requested, time));
        };
        _grbl.Disconnected += (reason, detail) => _post(() => OnGrblDisconnected(reason, detail));
        _grbl.StatusReceived += s => _post(() => OnStatus(s));
        _grbl.LineReceived += line => _post(() => OnLineReceived(line));
        _grbl.LineSent += line => _post(() => { if (!JobActive) Log(">> " + line); });
        _grbl.Acknowledged += (line, error) => _post(() => OnAcknowledged(line, error));

        UpdateCompressors();
    }

    // ----- Events -----

    /// <summary>A line for the log (also written to the day's log file).</summary>
    public event Action<DateTime, string>? Logged;
    /// <summary>Something a person must read (a rejected line, a file that can't run, a port that won't open).</summary>
    public event Action<string>? Alerted;
    /// <summary>Connection, job, position or settings changed: refresh what is enabled and shown.</summary>
    public event Action? Changed;
    /// <summary>A new status from Grbl (several times a second).</summary>
    public event Action<GrblStatus>? StatusReceived;
    /// <summary>A program was loaded.</summary>
    public event Action? ProgramChanged;
    /// <summary>A job started or Grbl accepted more of its lines.</summary>
    public event Action? ProgressChanged;
    /// <summary><see cref="PendingQuestion"/> appeared or went away (answered here or elsewhere).</summary>
    public event Action? QuestionChanged;
    /// <summary>
    /// Something a person away from the machine should hear about (title, text): a job finished or stopped,
    /// an alarm, the automatic resume giving up, the connection lost. Remote access sends it to the phone.
    /// </summary>
    public event Action<string, string>? Notified;

    // ----- State -----

    public AppSettings Settings { get; }

    /// <summary>The language of the log, questions, alerts and notifications, and of every screen.</summary>
    public Language Language
    {
        get => Settings.Language;
        set
        {
            if (Settings.Language == value)
                return;
            Settings.Language = value;
            SaveSettings();
            Changed?.Invoke();
        }
    }

    string T(string bosnian, string english) => Language.T(bosnian, english);

    /// <summary>The connection, for windows that talk to Grbl directly (Grbl settings).</summary>
    public GrblConnection Grbl => _grbl;

    /// <summary>The simulated Grbl, once connected to it.</summary>
    public GrblSimulator? Simulator => _simulator;

    public ConnectionStatus Connection { get; private set; }

    /// <summary>The port connected to, or last connected to.</summary>
    public string? PortName { get; private set; }
    public GrblVersion? Version { get; private set; }
    public bool IsOpen => _grbl.IsOpen;
    public bool IsReady => _grbl.IsReady;

    /// <summary>Last status since Grbl (re)started; null until the first one.</summary>
    public GrblStatus? LastStatus { get; private set; }

    /// <summary>Text for the state display: Grbl's state, or what the connection is waiting for.</summary>
    public string StateText => Texts.StateText(this);

    /// <summary>Grbl is in an alarm and takes no commands until unlocked ($X) or reset.</summary>
    public bool InAlarm => LastStatus?.State == MachineState.Alarm;

    /// <summary>The reported work position is the real one (checked after the last start).</summary>
    public bool PositionConfirmed => _positionTrusted;

    /// <summary>Waiting for Grbl to confirm the position, while connected.</summary>
    public bool PositionWaiting => IsOpen && !_positionTrusted;

    public Position? RememberedPosition => _memory.WorkPosition;
    public DateTime? RememberedAt => _memory.SavedAt;

    /// <summary>The laser as we last switched it; Grbl 0.8c doesn't report it.</summary>
    public bool LaserOn { get; private set; }

    public PositionQuestion? PendingQuestion { get; private set; }

    public GCodeProgram? Program { get; private set; }
    public string? ProgramPath { get; private set; }
    public Toolpath? Toolpath { get; private set; }
    public JobEstimate? Estimate { get; private set; }
    public GCodeJob? Job => _job;
    public bool JobActive => _job?.IsActive == true;
    public TimeSpan JobElapsed => _jobClock.Elapsed;

    /// <summary>When the job last started by hand (an automatic resume continues the same job); null before the first.</summary>
    public DateTime? JobStartedAt { get; private set; }

    /// <summary>What happened to the job since it was started by hand: compressor breaks and restarts, for the timeline.</summary>
    public IReadOnlyList<JobEvent> JobEvents => _jobEvents;
    readonly List<JobEvent> _jobEvents = [];

    /// <summary>Baud rate for the next connect; the estimate includes serial time, which depends on it.</summary>
    public int Baud
    {
        get => _baud;
        set
        {
            if (_baud == value)
                return;
            _baud = value;
            UpdateEstimate();
            Changed?.Invoke();
        }
    }

    // What can be done now. The commands check the same, so a stale screen can't do more than the window would allow.

    /// <summary>Lines of our own (laser, unlock, commands, Grbl settings): not while a job sends its lines.</summary>
    public bool CanSendCommands => IsOpen && !JobActive;
    public bool CanSetPosition => CanSendCommands && _positionCommand is null;
    public bool CanRestorePosition => CanSetPosition && !_positionTrusted && _memory.WorkPosition is not null;
    /// <summary>Jog and going to zero: moving before the position check would make the remembered position wrong.</summary>
    public bool CanMove => CanSetPosition && _positionTrusted;
    public bool CanFrame => CanMove && Toolpath?.CutBounds is not null;
    public bool CanLoadFile => !JobActive;
    public bool CanCloseFile => Program is not null && !JobActive;
    public bool CanStart => CanSendCommands && Program?.CanRun == true;
    /// <summary>The resume moves go to work coordinates, so they need a confirmed position like going to zero.</summary>
    public bool CanResumeFrom => CanStart && CanMove;

    public bool AutoResume
    {
        get => Settings.AutoResume;
        set
        {
            if (Settings.AutoResume == value)
                return;
            Settings.AutoResume = value;
            SaveSettings();
            if (!value)
                CancelAutoResume(T("isključen", "switched off"));
            Changed?.Invoke();
        }
    }

    public bool CompressorPause
    {
        get => Settings.CompressorPause;
        set
        {
            if (Settings.CompressorPause == value)
                return;
            Settings.CompressorPause = value;
            SaveSettings();
            UpdateCompressors();
            if (!value && _job?.State == JobState.Break)
            {
                Log(T("Planirana pauza isključena, nastavljam posao.", "Scheduled break switched off, continuing the job."));
                _job.EndBreak();
            }
        }
    }

    public CompressorSchedule? Compressors => Settings.Compressors;

    // ----- Connection -----

    /// <summary>Opens the port (or a simulator, <see cref="SimulatorPortNames"/>) and waits for Grbl's banner.</summary>
    public bool Connect(string port)
    {
        if (IsOpen)
            return true;
        try
        {
            _grblStarted = false;
            if (IsSimulatorPort(port))
            {
                var firmware = port == Simulator11PortName ? SimulatedFirmware.Grbl11 : SimulatedFirmware.Grbl08c;
                if (_simulator?.Firmware != firmware)
                {
                    _simulator?.Dispose();
                    _simulator = new GrblSimulator(firmware);
                }
                _simulator.Start();
                _grbl.Open(_simulator.Connect());
            }
            else
            {
                _grbl.Open(port, _baud);
            }
            Connection = ConnectionStatus.WaitingForGrbl;
            PortName = port;
            Log(T($"Otvoren {port}, čekam pozdrav od Grbl-a...", $"Opened {port}, waiting for Grbl's greeting..."));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            Log(T($"Ne mogu otvoriti {port}: {ex.Message}", $"Can't open {port}: {ex.Message}"));
            Alert(T($"Ne mogu otvoriti {port}:", $"Can't open {port}:") + Environment.NewLine + ex.Message);
            return false;
        }
        finally
        {
            Changed?.Invoke();
        }
    }

    /// <summary>Closes the port. A running job stops sending; what Grbl already has still runs.</summary>
    public void Disconnect()
    {
        if (!IsOpen)
            return;
        _grbl.Close();
        _job?.Stop();
        CancelAutoResume(T("odspojeno", "disconnected"));
        AbortGrblSettings(T("odspojeno", "disconnected"));
        Connection = ConnectionStatus.Disconnected;
        ClearStatus();
        Log(T("Odspojeno.", "Disconnected."));
        Changed?.Invoke();
    }

    void OnGrblConnected(GrblVersion version, bool requested, DateTime time)
    {
        bool first = !_grblStarted;
        _grblStarted = true;
        if (!requested && !first)
            OnGrblRestartedByItself(time);
        if (_autoResumeSince is not null)
        {
            // Another restart while waiting to continue: start waiting again.
            _autoResumeSince = time;
            _autoResumeIdleSince = null;
        }

        // Grbl just started or was reset, so the laser is off and the work offset may be gone.
        LaserOn = false;
        Connection = ConnectionStatus.Connected;
        Version = version;
        LastStatus = null;
        _positionTrusted = false;
        _positionCheckPending = true;
        _positionCommand = null;
        _linesUnanswered = 0;
        _statusCurrent = false;
        AbortGrblSettings(T("Grbl je resetovan", "Grbl was reset"));
        Changed?.Invoke();
    }

    /// <summary>A banner nobody asked for: the board restarted (supply glitch from the compressors, or USB).</summary>
    void OnGrblRestartedByItself(DateTime time)
    {
        if (PendingQuestion is not null)
        {
            _restartsWhileAsking++;
            Log(T($"Grbl se ponovo sam restartovao dok čeka odgovor o poziciji ({_restartsWhileAsking}. put). CNC stoji, pitanje ostaje isto.",
                $"Grbl restarted by itself again while waiting for the answer about the position (time {_restartsWhileAsking}). The CNC stands still, the question stays the same."));
        }
        else
        {
            Log(T("Grbl se sam restartovao (smetnja u napajanju ili USB).", "Grbl restarted by itself (power glitch or USB)."));
        }
        _restartsThisJob++;
        if (_job is { State: not (JobState.Completed or JobState.Stopped) } restarted)
            _jobEvents.Add(new JobEvent(time, JobEventKind.Restart, restarted.CompletedCount, null));

        var learned = CompressorSchedule.Learn(Settings.Compressors, time);
        Settings.Compressors = learned;
        SaveSettings();
        UpdateCompressors();
        Log(T($"  Raspored pauze naučen iz restarta: oko {learned.KnownStart:HH:mm:ss}, svakih {learned.Period.TotalMinutes:0.#} min; sljedeća ≈ {learned.NextStart(_clock()):HH:mm:ss}.",
            $"  Break schedule learned from the restart: around {learned.KnownStart:HH:mm:ss}, every {learned.Period.TotalMinutes:0.#} min; next ≈ {learned.NextStart(_clock()):HH:mm:ss}."));
    }

    void OnGrblDisconnected(DisconnectReason reason, string? detail)
    {
        var text = reason switch
        {
            DisconnectReason.PortRemoved => T("Veza prekinuta: port je nestao (USB isključen?).", "Connection lost: the port disappeared (USB unplugged?)."),
            _ => T("Veza prekinuta: greška pri čitanju porta. ", "Connection lost: error reading the port. ") + detail,
        };
        Log(text);
        Notify(T("Veza sa CNC-om prekinuta", "Connection to the CNC lost"), text + (JobActive ? T(" Posao je prekinut.", " The job was aborted.") : ""));
        Connection = ConnectionStatus.Lost;
        CancelAutoResume(T("veza je prekinuta", "the connection was lost"));
        AbortGrblSettings(T("veza je prekinuta", "the connection was lost"));
        ClearStatus();
        Changed?.Invoke();
    }

    void ClearStatus()
    {
        LastStatus = null;
        _positionTrusted = false;
        _positionCheckPending = false;
        _positionCommand = null;
        // Without a connection the question is moot; the next start asks again.
        SetQuestion(null);
    }

    void OnLineReceived(string line)
    {
        // The settings dump goes where it was asked for, not into the log.
        if (OnGrblSettingLine(line))
            return;
        // During a job every line is answered with "ok"; logging that would flood the log.
        if (!(JobActive && line == "ok"))
            Log("<< " + Texts.GrblAlarm(line, Language));
    }

    void OnAcknowledged(string line, string? error)
    {
        if (_linesUnanswered > 0)
            _linesUnanswered--;
        if (error is not null)
            error = Texts.GrblError(error, Language);
        if (error is not null && !JobActive)
            Log($"!! {error}  ({T("linija", "line")}: {line})");
        if (OnGrblSettingsAcknowledged(line, error))
            return;

        if (line != _positionCommand)
            return;
        _positionCommand = null;
        if (error is null)
        {
            _positionTrusted = true;
        }
        else
        {
            Log(T("Pozicija nije postavljena. Probaj ponovo (ako je Alarm, prvo Otključaj).", "Position not set. Try again (in an Alarm, Unlock first)."));
            CancelAutoResume(T("Grbl nije prihvatio poziciju", "Grbl didn't accept the position"), notify: true);
        }
        Changed?.Invoke();
    }

    void OnStatus(GrblStatus status)
    {
        if (!IsOpen)
            return;
        if (status.State == MachineState.Alarm && LastStatus?.State != MachineState.Alarm)
            Notify(T("Grbl je u alarmu", "Grbl is in an alarm"),
                T("CNC stoji. Provjeri ga, pa klikni Otključaj ($X).", "The CNC stands still. Check it, then click Unlock ($X).")
                + (JobActive ? T(" Posao je u toku.", " A job is running.") : ""));
        LastStatus = status;
        if (_linesUnanswered == 0)
            _statusCurrent = true;
        TrackPosition(status);
        StatusReceived?.Invoke(status);
    }

    // ----- Position -----

    void TrackPosition(GrblStatus status)
    {
        if (status.WorkPosition is not { } work || status.MachinePosition is not { } machine)
            return;

        if (_autoResumeSince is not null && status.State == MachineState.Alarm)
            CancelAutoResume(T("Grbl je u alarmu", "Grbl is in an alarm"));
        if (_positionCheckPending && status.State == MachineState.Idle && PendingQuestion is null)
        {
            _positionCheckPending = false;
            CheckPositionAfterStart(work, machine);
            return;
        }
        if (!_positionTrusted)
            return;
        if (JobActive)
            _memory.JobPosition = work;
        try
        {
            if (_memory.Update(work, machine))
                Changed?.Invoke();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Keep running; the next status will try again.
        }
    }

    /// <summary>Called once Grbl is idle after a start or reset: brings back the remembered work position.</summary>
    void CheckPositionAfterStart(Position work, Position machine)
    {
        if (_memory.WorkPosition is not { } savedWork || _memory.MachinePosition is not { } savedMachine
            || PositionRecovery.Check(savedWork, savedMachine, work, machine) is not { } restore)
        {
            _positionTrusted = true;
            Changed?.Invoke();
            return;
        }

        var target = restore.Target;
        if (!restore.MachinePositionLost)
        {
            // Grbl still knows where the machine is; only the work offset is gone (e.g. a zero set with G92
            // before the zero was stored permanently). Nothing to assume, so no need to ask.
            SetWorkPosition(target, T($"Grbl je izgubio nulu pri resetu; radna pozicija automatski vraćena na {target}.",
                $"Grbl lost the zero in the reset; work position restored automatically to {target}."));
            return;
        }

        if (_autoResumeSince is not null)
        {
            // The job is to continue by itself: nobody is there to ask, and the machine wasn't touched.
            SetWorkPosition(target, T($"Automatski nastavak: radna pozicija vraćena na {target} (zadnja javljena prije restarta).",
                $"Automatic resume: work position restored to {target} (the last one reported before the restart)."));
            return;
        }

        // Only one question at a time: restarts while it is open are counted in OnGrblRestartedByItself.
        _restartsWhileAsking = 0;
        SetQuestion(new PositionQuestion(work, target, _memory.SavedAt, _memory.JobFile, _memory.JobLine));
        // A job waits for a person (without an interrupted job, someone is just connecting).
        if (_memory.JobLine is { } jobLine)
            Notify(T("Posao čeka odgovor", "The job waits for an answer"),
                T($"Grbl je restartovan i izgubio poziciju; posao {Path.GetFileName(_memory.JobFile)} je stao na liniji {jobLine}. Odgovori na pitanje o poziciji.",
                    $"Grbl restarted and lost its position; job {Path.GetFileName(_memory.JobFile)} stopped at line {jobLine}. Answer the question about the position."));
    }

    /// <summary>Answers <see cref="PendingQuestion"/>; ignored if it was already answered.</summary>
    public void AnswerPositionQuestion(PositionQuestion question, bool restore)
    {
        if (PendingQuestion != question)
            return;
        SetQuestion(null);
        // Restarts while asking set this again; the answer covers them (the machine stood still).
        _positionCheckPending = false;
        if (_restartsWhileAsking > 0)
            Log(T($"Dok je pitanje bilo otvoreno, Grbl se restartovao još {_restartsWhileAsking} put(a).",
                $"While the question was open, Grbl restarted {_restartsWhileAsking} more time(s)."));
        if (!IsOpen)
            return;
        if (restore)
        {
            SetWorkPosition(question.Target, T($"Radna pozicija vraćena na {question.Target}.", $"Work position restored to {question.Target}."));
        }
        else
        {
            Log(T($"Zapamćena pozicija odbačena, radna pozicija je sada {LastStatus?.WorkPosition ?? question.Current}.",
                $"Remembered position discarded, the work position is now {LastStatus?.WorkPosition ?? question.Current}."));
            _positionTrusted = true;
            Changed?.Invoke();
        }
    }

    void SetQuestion(PositionQuestion? question)
    {
        if (PendingQuestion == question)
            return;
        PendingQuestion = question;
        QuestionChanged?.Invoke();
    }

    /// <summary>
    /// Makes the current spot read as <paramref name="work"/> by storing the G54 offset (kept by Grbl across resets).
    /// The position is remembered again once Grbl accepts it. False (and logged why) if nothing was sent.
    /// </summary>
    bool SetWorkPosition(Position work, string logText)
    {
        // The offset is computed from the reported machine position, so the machine must stand still.
        if (!_statusCurrent)
        {
            Log(T("Pozicija nije postavljena: čekam svjež status od Grbl-a (CNC se možda još pomjera). Probaj ponovo za trenutak.",
                "Position not set: waiting for a fresh status from Grbl (the CNC may still be moving). Try again in a moment."));
            return false;
        }
        if (LastStatus is not { State: MachineState.Idle, MachinePosition: { } machine })
        {
            Log(T("Pozicija nije postavljena: CNC mora mirovati (Idle). Sačekaj ili Otključaj pa probaj ponovo.",
                "Position not set: the CNC must be idle. Wait, or Unlock, then try again."));
            return false;
        }
        var commands = PositionRecovery.SetWorkPositionCommands(work, machine);
        foreach (var line in commands)
            if (!Send(line))
                return false;
        _positionTrusted = false;
        _positionCheckPending = false;
        _positionCommand = commands[^1];
        Log(logText);
        Changed?.Invoke();
        return true;
    }

    /// <summary>False (and logged why) if the zero wasn't set, e.g. while the machine still moves.</summary>
    public bool SetZero() =>
        CanSetPosition && SetWorkPosition(new Position(0, 0, 0), T("Trenutna pozicija postavljena kao nula (0,0,0), Grbl je pamti i nakon reseta.",
            "The current position set as zero (0,0,0); Grbl keeps it after a reset too."));

    /// <summary>Sets the work position to the remembered one; only if the machine wasn't moved since (the caller asks).</summary>
    public bool RestoreRememberedPosition() =>
        CanRestorePosition && _memory.WorkPosition is { } saved && SetWorkPosition(saved, T($"Radna pozicija vraćena na {saved}.", $"Work position restored to {saved}."));

    public void GoToZero()
    {
        if (!CanMove || LastStatus?.WorkPosition is not { } work)
            return;
        foreach (var line in PositionRecovery.GoToZeroCommands(work))
            if (!Send(line))
                return;
        SetLaser(false);
    }

    public void Jog(Axis axis, double distance)
    {
        if (!CanMove)
            return;
        foreach (var line in GrblCore.Jog.Commands(axis, distance))
            if (!Send(line))
                return;
    }

    // ----- Machine commands -----

    public void Pause()
    {
        if (JobActive)
            _job!.Pause();
        else if (IsOpen)
            _grbl.FeedHold();
    }

    public void Resume()
    {
        if (JobActive)
            _job!.Resume();
        else if (IsOpen)
            _grbl.CycleStart();
    }

    public void ToggleLaser()
    {
        // Grbl 0.8c doesn't report the laser, so we track what we sent.
        // M3/M5 is queued behind moves already sent, like any other line.
        bool on = !LaserOn;
        if (!CanSendCommands || !Send(on ? LaserOnCommand : "M5"))
            return;
        SetLaser(on);
        if (on && GrblSettingValue(32) == 1)
            Log(T("Grbl je u laserskom načinu ($32=1): laser svijetli samo dok se kreće sa G1/G2/G3, ne dok stoji.",
                "Grbl is in laser mode ($32=1): the laser is lit only while moving with G1/G2/G3, not while standing still."));
    }

    /// <summary>The connected Grbl (0.9+) runs the laser at the S value, so <see cref="AppSettings.LaserPower"/> matters.</summary>
    public bool UsesLaserPower => IsOpen && Version?.UsesSpindleSpeed == true;

    /// <summary>Full power (Grbl's $30) once the settings were read; null while unknown.</summary>
    public int? LaserPowerMax => GrblSettingValue(30) is { } max and > 0 ? (int)max : null;

    /// <summary>M3, with the laser power on Grbl 0.9+ (whose S is 0 after a reset, so M3 alone stays dark).</summary>
    string LaserOnCommand => UsesLaserPower ? $"M3 S{Settings.LaserPower}" : "M3";

    /// <summary>Lines a job sends first: the laser power on Grbl 0.9+, so the program's M3 without S lights the laser.</summary>
    IReadOnlyList<string>? LaserPowerSetup() => UsesLaserPower ? [$"S{Settings.LaserPower}"] : null;

    /// <summary>Highest laser power accepted; Grbl 1.1's $30 is usually 1000, some boards use 255 or 10000.</summary>
    public const int MaxLaserPower = 100000;

    /// <summary>Sets the laser power S used on Grbl 0.9+. Returns false (and logs why) for an invalid value.</summary>
    public bool SetLaserPower(int power)
    {
        if (power < 1 || power > MaxLaserPower)
        {
            Log(T($"Snaga lasera mora biti između 1 i {MaxLaserPower}.", $"The laser power must be between 1 and {MaxLaserPower}."));
            return false;
        }
        if (power == Settings.LaserPower)
            return true;
        Settings.LaserPower = power;
        SaveSettings();
        var max = GrblSettingValue(30) is { } top ? T(" od ", " of ") + $"{top} ($30)" : "";
        Log(T($"Snaga lasera: S{power}{max}. Važi od sljedećeg paljenja lasera ili posla.",
            $"Laser power: S{power}{max}. Applies from the next time the laser is switched on, or the next job."));
        Changed?.Invoke();
        return true;
    }

    /// <summary>A Grbl setting's number from the last read, or null if not read (or not a number).</summary>
    double? GrblSettingValue(int number) =>
        GrblSettings?.FirstOrDefault(s => s.Number == number) is { } s
        && double.TryParse(s.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)
            ? v : null;

    public void Unlock()
    {
        if (CanSendCommands)
            Send("$X");
    }

    public void Reset()
    {
        if (!IsOpen)
            return;
        CancelAutoResume(T("klikom na Reset", "by clicking Reset"));
        _grbl.SoftReset();
        Log(T("Reset poslan (Ctrl-X).", "Reset sent (Ctrl-X)."));
    }

    /// <summary>Sends a line typed by the user. Returns false (and logs why) if it wasn't sent.</summary>
    public bool SendCommand(string line) => CanSendCommands && Send(line);

    bool Send(string line)
    {
        line = line.Trim();
        if (line.Length == 0)
            return false;
        if (JobActive)
        {
            Log(T("Nije poslano: posao je u toku.", "Not sent: a job is running."));
            return false;
        }
        if (line.Length > GrblConnection.MaxLineLength)
        {
            Log(T($"Nije poslano: linija je duža od {GrblConnection.MaxLineLength} znakova.", $"Not sent: the line is longer than {GrblConnection.MaxLineLength} characters."));
            return false;
        }
        if (!_grbl.IsReady)
        {
            Log(T("Nije poslano: Grbl još nije spreman.", "Not sent: Grbl isn't ready yet."));
            return false;
        }
        _linesUnanswered++;
        _statusCurrent = false;
        _grbl.SendLine(line);
        return true;
    }

    void SetLaser(bool on)
    {
        if (on == LaserOn)
            return;
        LaserOn = on;
        Changed?.Invoke();
    }

    /// <summary>
    /// Keeps the speeds and acceleration used for time estimates in step with the machine.
    /// 0.8 numbers them $5, $4, $8, $9; 0.9+ per axis ($110/$111, $120/$121, the slower of X and Y) and $11, without a default feed.
    /// </summary>
    public void RememberLimits(IReadOnlyList<GrblSetting> settings)
    {
        double? Get(int number) =>
            settings.FirstOrDefault(s => s.Number == number) is { } s
            && double.TryParse(s.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0
                ? v : null;
        double? Slower(int x, int y) => Get(x) is { } a && Get(y) is { } b ? Math.Min(a, b) : Get(x) ?? Get(y);

        bool perAxis = Version?.UsesSpindleSpeed == true;
        var limits = new MachineLimits(
            (perAxis ? Slower(110, 111) : Get(5)) ?? Settings.SeekRate,
            (perAxis ? null : Get(4)) ?? Settings.DefaultFeed,
            (perAxis ? Slower(120, 121) : Get(8)) ?? Settings.Acceleration,
            Get(perAxis ? 11 : 9) ?? Settings.JunctionDeviation);
        if (limits == Settings.Limits)
            return;
        (Settings.SeekRate, Settings.DefaultFeed, Settings.Acceleration, Settings.JunctionDeviation) =
            (limits.SeekRate, limits.DefaultFeed, limits.Acceleration, limits.JunctionDeviation);
        SaveSettings();
        Log(T($"Procjena trajanja sada računa sa: prazni hod {limits.SeekRate} mm/min, ubrzanje {limits.Acceleration} mm/s², ćoškovi {limits.JunctionDeviation} mm.",
            $"The duration estimate now uses: rapids {limits.SeekRate} mm/min, acceleration {limits.Acceleration} mm/s², corners {limits.JunctionDeviation} mm."));
        UpdateEstimate();
        Changed?.Invoke();
    }

    // Reading: the dump's lines so far, until Grbl answers "$$". Writing: the line waiting for its "ok", and the rest;
    // Grbl stores each in EEPROM and may miss characters meanwhile, so one at a time.
    List<GrblSetting>? _settingsReading;
    string? _settingWriting;
    readonly Queue<string> _settingsToWrite = new();

    /// <summary>Grbl's settings as last read with <see cref="ReadGrblSettings"/>; null until then.</summary>
    public IReadOnlyList<GrblSetting>? GrblSettings { get; private set; }

    /// <summary>
    /// A <see cref="ReadGrblSettings"/> or <see cref="WriteGrblSettings"/> finished: the settings read
    /// (after writing, as read back), or null and why not.
    /// </summary>
    public event Action<IReadOnlyList<GrblSetting>?, string?>? GrblSettingsDone;

    public bool GrblSettingsBusy => _settingsReading is not null || _settingWriting is not null;

    /// <summary>Settings can be read and written: connected, no job, standing still, nothing else being read or written.</summary>
    public bool CanGrblSettings =>
        _grbl.IsReady && CanSendCommands && !GrblSettingsBusy && LastStatus?.State is MachineState.Idle or MachineState.Alarm;

    /// <summary>Asks Grbl for its settings ('$$'); <see cref="GrblSettingsDone"/> follows. Returns why not, or null.</summary>
    public string? ReadGrblSettings()
    {
        if (!CanGrblSettings)
            return T("Postavke se mogu čitati samo kad je Grbl spojen i CNC miruje.", "The settings can be read only while Grbl is connected and the CNC is idle.");
        return Send(GrblSetting.DumpCommand) ? StartReading() : T("Grbl nije spreman.", "Grbl isn't ready.");
    }

    string? StartReading()
    {
        _settingsReading = [];
        Changed?.Invoke();
        return null;
    }

    /// <summary>
    /// Stores new values (by setting number) one at a time, then reads all back; <see cref="GrblSettingsDone"/> follows.
    /// Values are checked like in the desktop window. Returns why not, or null.
    /// </summary>
    public string? WriteGrblSettings(IReadOnlyDictionary<int, string> values)
    {
        if (!CanGrblSettings)
            return T("Postavke se mogu upisati samo kad je Grbl spojen i CNC miruje.", "The settings can be written only while Grbl is connected and the CNC is idle.");
        if (GrblSettings is not { } known)
            return T("Prvo učitaj postavke.", "Read the settings first.");
        var commands = new List<string>();
        foreach (var (number, value) in values)
        {
            if (known.FirstOrDefault(s => s.Number == number) is not { } setting || setting.Normalize(value) is not { } normalized)
                return T($"Neispravna vrijednost za ${number}: {value}", $"Invalid value for ${number}: {value}");
            if (normalized != setting.Normalize(setting.Value))
                commands.Add(setting.SetCommand(normalized));
        }
        if (commands.Count == 0)
            return T("Nema izmjena.", "No changes.");
        Log(T("Upisujem u Grbl: ", "Writing to Grbl: ") + string.Join(", ", commands));
        foreach (var command in commands)
            _settingsToWrite.Enqueue(command);
        WriteNextSetting();
        return null;
    }

    void WriteNextSetting()
    {
        if (!_settingsToWrite.TryDequeue(out var command))
        {
            // Read back what Grbl actually stored.
            _settingWriting = null;
            if (!(Send(GrblSetting.DumpCommand) && StartReading() is null))
                FinishGrblSettings(null, T("Postavke su upisane, ali ih ne mogu ponovo pročitati.", "The settings were written, but can't be read back."));
            return;
        }
        if (!Send(command))
        {
            _settingsToWrite.Clear();
            _settingWriting = null;
            FinishGrblSettings(null, T($"{command} nije poslano, razlog je u logu.", $"{command} was not sent, the reason is in the log."));
            return;
        }
        _settingWriting = command;
        Changed?.Invoke();
    }

    /// <summary>The dump's lines while reading; true if the line was one of them.</summary>
    bool OnGrblSettingLine(string line)
    {
        if (_settingsReading is null || !GrblSetting.TryParse(line, out var setting))
            return false;
        _settingsReading.Add(setting);
        return true;
    }

    /// <summary>Grbl answered a line of reading or writing settings; true if it was one.</summary>
    bool OnGrblSettingsAcknowledged(string line, string? error)
    {
        if (_settingsReading is { } read && line == GrblSetting.DumpCommand)
        {
            _settingsReading = null;
            if (error is not null)
            {
                FinishGrblSettings(null, T($"Grbl je odbio čitanje postavki: {error}", $"Grbl refused to read the settings: {error}"));
                return true;
            }
            GrblSettings = read;
            Log(T($"Grbl postavke pročitane ({read.Count}).", $"Grbl settings read ({read.Count})."));
            // The estimate uses the speeds and acceleration.
            RememberLimits(read);
            FinishGrblSettings(read, null);
            return true;
        }
        if (_settingWriting is null || line != _settingWriting)
            return false;
        _settingWriting = null;
        if (error is not null)
        {
            _settingsToWrite.Clear();
            FinishGrblSettings(null, T($"Grbl je odbio {line}: {error}", $"Grbl refused {line}: {error}"));
            return true;
        }
        WriteNextSetting();
        return true;
    }

    void AbortGrblSettings(string reason)
    {
        if (!GrblSettingsBusy)
            return;
        _settingsReading = null;
        _settingWriting = null;
        _settingsToWrite.Clear();
        FinishGrblSettings(null, T($"Čitanje/upis postavki prekinut: {reason}.", $"Reading/writing the settings aborted: {reason}."));
    }

    void FinishGrblSettings(IReadOnlyList<GrblSetting>? settings, string? error)
    {
        if (error is not null)
            Log(error);
        Changed?.Invoke();
        GrblSettingsDone?.Invoke(settings, error);
    }

    // ----- Program -----

    /// <summary>Loads a G-code file that is ready for Grbl (an original ArtCAM file is converted for the laser first).</summary>
    public bool LoadFile(string path)
    {
        if (!CanLoadFile)
            return false;
        GCodeProgram program;
        try
        {
            program = GCodeProgram.Load(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Alert(T("Greška s fajlom:", "File error:") + Environment.NewLine + ex.Message);
            return false;
        }

        _job?.Dispose();
        _job = null;
        Program = program;
        ProgramPath = path;
        Toolpath = Toolpath.Build(program);
        UpdateEstimate();
        ProgramChanged?.Invoke();
        Log(T($"Učitan fajl {Path.GetFileName(path)}: {program.Lines.Count} linija za slanje, trajanje ≈ {Texts.Duration(Estimate!.Total)}.",
            $"Loaded file {Path.GetFileName(path)}: {program.Lines.Count} lines to send, duration ≈ {Texts.Duration(Estimate!.Total)}."));
        ReportIssues(program);
        Changed?.Invoke();
        return true;
    }

    /// <summary>After a power cut or closing the program mid-job, loads that job so it is ready to continue.</summary>
    public void CloseFile()
    {
        if (!CanCloseFile)
            return;
        var name = Path.GetFileName(ProgramPath);
        _job?.Dispose();
        _job = null;
        Program = null;
        ProgramPath = null;
        Toolpath = null;
        Estimate = null;
        ProgramChanged?.Invoke();
        // An interrupted job stays remembered: opening the file again offers "Nastavi od..." as before.
        Log(T($"Fajl {name} zatvoren.", $"File {name} closed."));
        Changed?.Invoke();
    }

    public void LoadInterruptedJob()
    {
        if (Program is null && _memory.JobLine is { } line && File.Exists(_memory.JobFile) && LoadFile(_memory.JobFile))
            Log(T($"Prethodni posao je prekinut kod linije {line}. Spoji se, potvrdi poziciju pa klikni \"Nastavi od...\".",
                $"The previous job was interrupted at line {line}. Connect, confirm the position, then click \"Resume from...\"."));
    }

    void UpdateEstimate()
    {
        if (Program is null || Toolpath is null)
            return;
        Estimate = JobEstimate.Calculate(Program, Toolpath, Settings.Limits, _baud);
    }

    void ReportIssues(GCodeProgram program)
    {
        int shortened = program.Issues.Count(i => i.Kind == GCodeIssueKind.Shortened);
        if (shortened > 0)
            Log(T($"  {shortened} linija skraćeno (decimale zaokružene na 3 mjesta) da stanu u Grbl bafer.",
                $"  {shortened} lines shortened (decimals rounded to 3 places) to fit Grbl's buffer."));

        int programEnds = program.Issues.Count(i => i.Kind == GCodeIssueKind.ProgramEndRemoved);
        if (programEnds > 0)
            Log(T($"  Uklonjeno M2/M30 ({programEnds}x) - Grbl 0.8 se zna zablokirati na njima.", $"  Removed M2/M30 ({programEnds}x) - Grbl 0.8 can lock up on them."));

        var unsupported = program.Issues.Where(i => i.Kind == GCodeIssueKind.UnsupportedRemoved).ToList();
        if (unsupported.Count > 0)
        {
            var words = string.Join(", ", unsupported.SelectMany(i => i.Detail!.Split(' ')).Distinct().Order());
            Log(T($"  Uklonjene komande koje Grbl 0.8 ne poznaje ({unsupported.Count} linija): {words}",
                $"  Removed commands Grbl 0.8 doesn't know ({unsupported.Count} lines): {words}"));
        }

        var tooLong = program.Issues.Where(i => i.Kind == GCodeIssueKind.TooLong).ToList();
        if (tooLong.Count == 0)
            return;

        var lines = string.Join(Environment.NewLine, tooLong.Take(MaxIssuesShown).Select(i => $"  {i.SourceLineNumber}: {i.OriginalText.Trim()}"));
        var more = tooLong.Count > MaxIssuesShown ? $"{Environment.NewLine}  ... " + T("i još", "and") + $" {tooLong.Count - MaxIssuesShown}" + T("", " more") : "";
        Alert(T($"{tooLong.Count} linija je predugo za Grbl ({GCodeProgram.DefaultMaxLineLength} znakova) i ovaj fajl se ne može pokrenuti:",
            $"{tooLong.Count} lines are too long for Grbl ({GCodeProgram.DefaultMaxLineLength} characters) and this file can't run:") + $"{Environment.NewLine}{lines}{more}");
    }

    public void Frame()
    {
        if (!CanFrame || Toolpath?.CutBounds is not { } bounds || LastStatus?.WorkPosition is not { } here)
            return;
        foreach (var line in GrblCore.Frame.Commands(bounds, here))
            if (!Send(line))
                return;
        SetLaser(false);
        Log(T(FormattableString.Invariant($"Okvir: X {bounds.MinX:0.##} do {bounds.MaxX:0.##}, Y {bounds.MinY:0.##} do {bounds.MaxY:0.##} (laser ugašen), pa nazad."),
            FormattableString.Invariant($"Frame: X {bounds.MinX:0.##} to {bounds.MaxX:0.##}, Y {bounds.MinY:0.##} to {bounds.MaxY:0.##} (laser off), then back.")));
    }

    // ----- Job -----

    /// <summary>Where "Nastavi od..." should start: the line the laser stopped at, and an explanation; (0, null) without an interruption for this file.</summary>
    public (int SuggestedIndex, string? StoppedText) SuggestResume()
    {
        if (Toolpath is null || _memory.JobLine is not { } jobLine || !IsRememberedJob(ProgramPath))
            return (0, null);
        int suggested = JobResume.SuggestStart(Toolpath, IndexOfSourceLine(jobLine), _memory.JobPosition);
        var text = T($"Posao je stao: Grbl je zadnju prihvatio liniju {jobLine}", $"The job stopped: the last line Grbl accepted was {jobLine}") +
            (_memory.JobPosition is { } p ? T($", laser je zadnji put viđen na {p}.", $", the laser was last seen at {p}.") : ".") +
            T(" Predložena je linija na kojoj je laser stao (Grbl unaprijed prihvata pokrete koji se možda nisu izvršili).",
                " The line where the laser stopped is suggested (Grbl accepts moves ahead, which may not have run).");
        return (suggested, text);
    }

    /// <summary>
    /// What "Nastavi od..." would send to continue from the line with this number in the file, from where the machine is now:
    /// the plan and its description, or null and why it can't.
    /// </summary>
    public (ResumePlan? Plan, string Text) PlanResume(int sourceLine)
    {
        if (Program is null || LastStatus?.WorkPosition is not { } here)
            return (null, T("Nema učitanog fajla ili pozicije CNC-a.", "No file loaded, or no position of the CNC."));
        return JobResume.TryPlan(Program, IndexOfSourceLine(sourceLine), here, out var plan, out var problem)
            ? (plan, Texts.ResumePlan(Program, plan, Estimate, Language))
            : (null, Texts.ResumeProblem(problem, Language));
    }

    bool IsRememberedJob(string? path) =>
        path is not null && _memory.JobFile is { } jobFile
        && string.Equals(Path.GetFullPath(jobFile), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase);

    /// <summary>Index into the loaded program of the line with this number in the file (or the next one sent).</summary>
    int IndexOfSourceLine(int sourceLine)
    {
        int index = 0;
        while (index < Program!.Lines.Count - 1 && Program.Lines[index].SourceLineNumber < sourceLine)
            index++;
        return index;
    }

    /// <summary>Starts the loaded program from the beginning, or continues it by <paramref name="resume"/> ("Nastavi od...").</summary>
    public void StartJob(ResumePlan? resume = null)
    {
        if (resume is null ? CanStart : CanResumeFrom)
            StartJob(resume, automatic: false);
    }

    /// <param name="automatic">Started by the automatic resume after a restart; counts towards <see cref="MaxAutoResumes"/>.</param>
    void StartJob(ResumePlan? resume, bool automatic)
    {
        if (Program is null)
            return;
        if (!_grbl.IsReady)
        {
            Log(T("Nije pokrenuto: Grbl još nije spreman.", "Not started: Grbl isn't ready yet."));
            return;
        }
        if (!automatic)
        {
            CancelAutoResume(null);
            _autoResumes = 0;
            _restartsThisJob = 0;
            _jobEvents.Clear();
            JobStartedAt = _clock();
        }

        _job?.Dispose();
        var job = _job = new GCodeJob(_grbl, Program, resume, LaserPowerSetup());
        // A replaced job's late events are ignored.
        job.StateChanged += state => _post(() => { if (job == _job) OnJobStateChanged(job, state); });
        job.ProgressChanged += completed => _post(() => { if (job == _job) OnProgress(job); });
        job.LineFailed += (line, error) => _post(() => { if (job == _job) OnJobLineFailed(line, error); });
        var estimate = Estimate!;
        job.BreakBeforeLine = index => BreakForCompressors(job, estimate, index);
        _breakStart = _breakUntil = null;
        _grbl.StatusIntervalMs = JobStatusIntervalMs;

        _memory.JobFile = ProgramPath;
        // Until Grbl accepts a line, the job still stands where it stopped before.
        _memory.JobLine = job.StartIndex > 0 ? Program.Lines[job.StartIndex - 1].SourceLineNumber : null;
        _memory.JobPosition = null;
        SavePositionMemory();
        _jobClock.Restart();
        ProgressChanged?.Invoke();
        job.Start();
    }

    /// <summary>Stops sending. Lines already in Grbl's buffer still run; the caller asks first.</summary>
    public void Stop()
    {
        if (JobActive)
            _job!.Stop();
    }

    void OnProgress(GCodeJob job)
    {
        // Written to disk together with the next position update.
        if (job.LastCompletedLine is { } last)
            _memory.JobLine = last.SourceLineNumber;
        ProgressChanged?.Invoke();
    }

    void OnJobStateChanged(GCodeJob job, JobState state)
    {
        var last = job.LastCompletedLine;
        var where = last is null ? "" : T(" Zadnja prihvaćena linija: ", " Last accepted line: ") + $"{last.SourceLineNumber} ({last.Text}).";
        switch (state)
        {
            case JobState.Running:
                if (_breakStart is not null)
                {
                    // Back from a compressor break; CompressorTick logged it.
                    _breakStart = _breakUntil = null;
                    break;
                }
                Log(job.CompletedCount != job.StartIndex ? T("Posao nastavljen.", "Job resumed.")
                    : job.StartIndex == 0 ? T("Posao pokrenut.", "Job started.")
                    : T("Posao nastavljen od linije ", "Job resumed from line ") + $"{job.Program.Lines[job.StartIndex].SourceLineNumber}.");
                break;
            case JobState.Break:
                OnCompressorBreak();
                break;
            case JobState.Paused:
                Log(T("Posao pauziran.", "Job paused.") + where);
                SavePositionMemory();
                break;
            case JobState.Completed:
                _jobClock.Stop();
                var took = Texts.Duration(_jobClock.Elapsed);
                Log(T($"Posao završen za {took} (procjena {Texts.Duration(Estimate!.Remaining(job.StartIndex))}, pauze uračunate).",
                    $"Job finished in {took} (estimate {Texts.Duration(Estimate!.Remaining(job.StartIndex))}, breaks included)."));
                Notify(T("Posao završen", "Job finished"), T($"{JobName} za {took}.", $"{JobName} in {took}.") +
                    (_restartsThisJob > 0
                        ? T($" Grbl se restartovao {_restartsThisJob} put(a), automatskih nastavaka: {_autoResumes}.",
                            $" Grbl restarted {_restartsThisJob} time(s), automatic resumes: {_autoResumes}.")
                        : ""));
                ForgetJob();
                break;
            case JobState.Stopped:
                Log(T("Posao zaustavljen.", "Job stopped.") + where);
                Notify(T("Posao zaustavljen", "Job stopped"), JobName + "." + where);
                // A resume that stopped before its first line keeps the earlier interruption to try again.
                if (job.StartIndex == 0 || last is not null)
                    ForgetJob();
                break;
            case JobState.Aborted:
                _breakStart = _breakUntil = null;
                if (job.AbortReason == JobAbortReason.GrblRestarted && Settings.AutoResume)
                {
                    Log(T("Posao prekinut jer se Grbl restartovao.", "Job aborted because Grbl restarted.") + where);
                    SavePositionMemory();
                    BeginAutoResume();
                    break;
                }
                var aborted = job.AbortReason switch
                {
                    JobAbortReason.GrblRestarted => T("Posao prekinut jer se Grbl restartovao.", "Job aborted because Grbl restarted."),
                    JobAbortReason.Reset => T("Posao prekinut resetom.", "Job aborted by a reset."),
                    _ => T("Posao prekinut: veza je prekinuta.", "Job aborted: the connection was lost."),
                } + where;
                Log(aborted + T(" Kad pozicija bude potvrđena, klikni \"Nastavi od...\".", " Once the position is confirmed, click \"Resume from...\"."));
                Notify(T("Posao prekinut", "Job aborted"), $"{JobName}: {aborted}");
                SavePositionMemory();
                break;
        }
        if (!JobActive)
        {
            _grbl.StatusIntervalMs = IdleStatusIntervalMs;
            // The job's last lines may still be on their way; wait for a status after them.
            _linesUnanswered = 0;
            _statusCurrent = false;
        }
        Changed?.Invoke();
    }

    void OnJobLineFailed(GCodeLine line, string error)
    {
        var nl = Environment.NewLine;
        error = Texts.GrblError(error, Language);
        if (line.SourceLineNumber == 0)
        {
            Log($"!! {error}  " + T("pri pripremi posla", "while preparing the job") + $": {line.Text}");
            Alert(T("Grbl je odbio pripremnu komandu:", "Grbl refused a preparation command:") + $"{nl}{line.Text}{nl}{error}{nl}{nl}"
                + T("Posao je zaustavljen prije prve linije fajla.", "The job was stopped before the file's first line."));
            return;
        }
        Log($"!! {error}  " + T("na liniji", "at line") + $" {line.SourceLineNumber}: {line.Text}");
        Notify(T("Posao pauziran: greška", "Job paused: error"),
            T($"Grbl je odbio liniju {line.SourceLineNumber} ({line.Text}): {error}. Nastavi ili zaustavi.",
                $"Grbl refused line {line.SourceLineNumber} ({line.Text}): {error}. Resume or stop."));
        Alert(T($"Grbl je odbio liniju {line.SourceLineNumber}:", $"Grbl refused line {line.SourceLineNumber}:") + $"{nl}{line.Text}{nl}{error}{nl}{nl}"
            + T("Posao je pauziran. Nastavi ili zaustavi.", "The job is paused. Resume or stop."));
    }

    void ForgetJob()
    {
        _memory.JobFile = null;
        _memory.JobLine = null;
        _memory.JobPosition = null;
        SavePositionMemory();
    }

    // ----- Automatic resume after a restart -----

    void BeginAutoResume()
    {
        if (_autoResumes >= MaxAutoResumes)
        {
            Log(T($"Automatski nastavak: već {MaxAutoResumes} puta u ovom poslu, više ne nastavljam sam. Provjeri CNC pa klikni \"Nastavi od...\".",
                $"Automatic resume: already {MaxAutoResumes} times in this job, not resuming by myself any more. Check the CNC, then click \"Resume from...\"."));
            Notify(T("Automatski nastavak odustao", "Automatic resume gave up"),
                T($"{JobName}: Grbl se restartovao već {MaxAutoResumes} puta u ovom poslu. Provjeri CNC pa nastavi ručno.",
                    $"{JobName}: Grbl restarted {MaxAutoResumes} times in this job already. Check the CNC, then resume by hand."));
            return;
        }
        _autoResumeSince = _clock();
        _autoResumeIdleSince = null;
        Log(T("Automatski nastavak: čekam da Grbl bude spreman, vraćam poziciju pa nastavljam...",
            "Automatic resume: waiting for Grbl to be ready, restoring the position, then resuming..."));
    }

    /// <param name="reason">Why, for the log; null to cancel without a message.</param>
    /// <param name="notify">Gave up by itself and nobody is told otherwise (not a click, not an alarm or lost connection, which notify themselves).</param>
    void CancelAutoResume(string? reason, bool notify = false)
    {
        if (_autoResumeSince is null)
            return;
        _autoResumeSince = null;
        _autoResumeIdleSince = null;
        if (reason is not null)
            Log(T($"Automatski nastavak otkazan: {reason}. Klikni \"Nastavi od...\" kad budeš spreman.",
                $"Automatic resume cancelled: {reason}. Click \"Resume from...\" when you are ready."));
        if (notify)
            Notify(T("Automatski nastavak odustao", "Automatic resume gave up"), $"{JobName}: {reason}. " + T("Nastavi ručno.", "Resume by hand."));
    }

    void GiveUpAutoResume(string why)
    {
        Log(T("Automatski nastavak nije moguć: ", "Automatic resume not possible: ") + why);
        Notify(T("Automatski nastavak nije moguć", "Automatic resume not possible"), $"{JobName}: {why} " + T("Nastavi ručno.", "Resume by hand."));
    }

    string JobName => Path.GetFileName(ProgramPath) ?? T("Posao", "Job");

    void Notify(string title, string text) => Notified?.Invoke(title, text);

    /// <summary>Waiting for the automatic resume after a restart.</summary>
    public bool AutoResumePending => _autoResumeSince is not null;

    void AutoResumeTick(DateTime now)
    {
        if (_autoResumeSince is not { } since)
            return;
        bool ready = _grbl.IsReady && _positionTrusted && _positionCommand is null && LastStatus?.State == MachineState.Idle;
        if (!ready)
        {
            _autoResumeIdleSince = null;
            if (now - since > AutoResumeGiveUp)
                CancelAutoResume(T($"pozicija nije potvrđena ni nakon {AutoResumeGiveUp.TotalSeconds:0} s",
                    $"the position wasn't confirmed even after {AutoResumeGiveUp.TotalSeconds:0} s"), notify: true);
            return;
        }
        // Compressors can restart the board several times in a row; wait until it stays up.
        _autoResumeIdleSince ??= now;
        if (now - _autoResumeIdleSince < AutoResumeSettle)
            return;

        _autoResumeSince = null;
        _autoResumeIdleSince = null;
        if (Program is null || Toolpath is null || LastStatus?.WorkPosition is not { } here
            || _memory.JobLine is not { } jobLine || !IsRememberedJob(ProgramPath))
        {
            GiveUpAutoResume(T("nema zapamćenog prekinutog posla za učitani fajl.", "no interrupted job remembered for the loaded file."));
            return;
        }
        int start = JobResume.SuggestStart(Toolpath, IndexOfSourceLine(jobLine), _memory.JobPosition);
        if (!JobResume.TryPlan(Program, start, here, out var plan, out var problem))
        {
            GiveUpAutoResume(problem.Kind switch
            {
                ResumeProblemKind.NothingLeft => T("do kraja fajla nema više pokreta.", "there are no more moves to the end of the file."),
                ResumeProblemKind.UnsupportedCommand => T($"linija {problem.Line!.SourceLineNumber} mijenja koordinatni sistem.",
                    $"line {problem.Line!.SourceLineNumber} changes the coordinate system."),
                _ => T($"linija {problem.Line!.SourceLineNumber} je predugačka kad joj se doda način kretanja.",
                    $"line {problem.Line!.SourceLineNumber} is too long once its motion mode is added."),
            });
            return;
        }
        _autoResumes++;
        Log(T($"Automatski nastavak {_autoResumes}/{MaxAutoResumes}: od linije {Program.Lines[plan.StartIndex].SourceLineNumber} (stao na liniji {jobLine}).",
            $"Automatic resume {_autoResumes}/{MaxAutoResumes}: from line {Program.Lines[plan.StartIndex].SourceLineNumber} (stopped at line {jobLine})."));
        StartJob(plan, automatic: true);
    }

    // ----- Pause while the compressors start -----

    /// <summary>Sets or clears (null) the compressor rhythm typed in by the user.</summary>
    public void SetCompressors(CompressorSchedule? schedule)
    {
        Settings.Compressors = schedule;
        SaveSettings();
        UpdateCompressors();
        Log(schedule is { } s
            ? T($"Raspored pauze upisan: u {s.KnownStart:HH:mm:ss}, svakih {s.Period.TotalMinutes:0.#} min.",
                $"Break schedule set: at {s.KnownStart:HH:mm:ss}, every {s.Period.TotalMinutes:0.#} min.")
            : T("Raspored pauze obrisan.", "Break schedule cleared."));
    }

    /// <summary>
    /// Called by the job before each line, on the reader thread: break if the lines up to this one
    /// would still be running when the compressors switch on.
    /// </summary>
    bool BreakForCompressors(GCodeJob job, JobEstimate estimate, int index)
    {
        if (_activeCompressors is not { } schedule)
            return false;
        var now = _clock();
        // Accepted lines may still be waiting in Grbl's planner; count them as not run, to be safe.
        int from = Math.Max(job.StartIndex, job.CompletedCount - GrblConnection.PlannerSize);
        var busy = estimate.Remaining(from) - estimate.Remaining(index + 1);
        return schedule.ShouldBreak(now, now + busy);
    }

    void OnCompressorBreak()
    {
        var now = _clock();
        _breakSettledLogged = _breakLateLogged = false;
        if (_activeCompressors is not { } schedule)
        {
            _job?.EndBreak();
            return;
        }
        _breakStart = schedule.UpcomingStart(now);
        _breakUntil = schedule.BreakEnd(now);
        if (_job is { } breaking)
            _jobEvents.Add(new JobEvent(now, JobEventKind.CompressorBreak, breaking.CompletedCount, _breakUntil));
        Log(T($"Planirana pauza (≈ {_breakStart:HH:mm:ss}): laser se gasi nakon već poslanih linija, nastavak u {_breakUntil:HH:mm:ss}.",
            $"Scheduled break (≈ {_breakStart:HH:mm:ss}): the laser goes off after the lines already sent, resuming at {_breakUntil:HH:mm:ss}."));
    }

    void CompressorTick(DateTime now)
    {
        if (_job is not { State: JobState.Break } job || _breakStart is not { } start || _breakUntil is not { } until)
            return;
        if (!_breakSettledLogged && job.IsBreakSettled)
        {
            _breakSettledLogged = true;
            Log(T("  CNC stoji, laser ugašen.", "  The CNC stands still, laser off."));
        }
        if (!_breakLateLogged && now >= start && !job.IsBreakSettled)
        {
            _breakLateLogged = true;
            Log(T("  Upozorenje: planirana pauza je počela, a CNC još nije stao (posao sporiji od procjene).",
                "  Warning: the scheduled break has started and the CNC hasn't stopped yet (the job is slower than estimated)."));
        }
        if (now < until)
            return;
        Log(_breakSettledLogged ? T("Planirana pauza je gotova, nastavljam posao.", "The scheduled break is over, continuing the job.")
            : T("Nastavljam posao.", "Continuing the job."));
        job.EndBreak();
    }

    /// <summary>Puts the compressor settings into effect.</summary>
    void UpdateCompressors()
    {
        _activeCompressors = Settings.CompressorPause ? Settings.Compressors : null;
        Changed?.Invoke();
    }

    // ----- Housekeeping -----

    /// <summary>Drives the automatic resume and the compressor pause; call it a few times a second.</summary>
    public void Tick()
    {
        var now = _clock();
        AutoResumeTick(now);
        CompressorTick(now);
    }

    public void SaveSettings()
    {
        try
        {
            Settings.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log(T("Postavke nisu spremljene: ", "Settings not saved: ") + ex.Message);
        }
    }

    void SavePositionMemory()
    {
        try
        {
            _memory.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log(T("Pozicija nije spremljena: ", "Position not saved: ") + ex.Message);
        }
    }

    /// <summary>Writes to the log (every screen showing it) and to today's log file.</summary>
    public void Log(string text)
    {
        var now = _clock();
        Logged?.Invoke(now, text);
        if (_logFile?.Write(now, text) is { } error)
            Logged?.Invoke(now, T($"Log se ne može upisati u fajl ({_logFolder}): {error}", $"The log can't be written to a file ({_logFolder}): {error}"));
    }

    void Alert(string text) => Alerted?.Invoke(text);

    public void Dispose()
    {
        _job?.Dispose();
        _grbl.Dispose();
        _simulator?.Dispose();
    }
}
