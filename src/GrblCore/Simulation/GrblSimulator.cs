using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace GrblCore.Simulation;

/// <summary>Which Grbl the simulator plays.</summary>
public enum SimulatedFirmware
{
    /// <summary>The user's machine: text errors, "Cycle", MPos and WPos in every report, settings $0-$22.</summary>
    Grbl08c,
    /// <summary>
    /// What most boards run today: "error:20"/"ALARM:3", "Run"/"Hold:0", MPos (or WPos, $10) with the offset (WCO)
    /// only now and then, settings $0-$32 and $100-$132, laser power S (0 after a reset).
    /// </summary>
    Grbl11,
}

/// <summary>
/// An Arduino running Grbl 0.8c or 1.1, in software, so the program can be developed and tested without the machine.
/// </summary>
/// <remarks>
/// <para>What it does like the real board: the startup banner, "ok"/error per line, a planner of
/// <see cref="PlannerSize"/> blocks (a motion line is answered only once it fits), M3/M5/G4 waiting for the
/// moves before them, realtime '?', '!', '~' and Ctrl-X, the version's status format, '$' commands, settings and
/// coordinate offsets kept "in EEPROM" across resets, G92 lost on reset, alarm after a reset during motion.</para>
/// <para>Simplified: moves run at constant speed (no acceleration, except braking on feed hold),
/// the serial line has no baud-rate delay, and Z/laser power are just numbers.</para>
/// <para>Faults for testing: <see cref="Restart"/> (supply glitch, machine position lost),
/// <see cref="RestartInterval"/> (the compressors every 15 minutes), <see cref="CorruptNextStatus"/>,
/// <see cref="Unplug"/>.</para>
/// <para>Time only moves in <see cref="Advance"/>: call it directly in tests, or <see cref="Start"/> a background
/// clock that calls it in real time, sped up by <see cref="TimeScale"/>.</para>
/// </remarks>
public sealed class GrblSimulator : IDisposable
{
    public const string Banner = "Grbl 0.8c ['$' for help]";
    public const string Banner11 = "Grbl 1.1h ['$' for help]";

    public const int PlannerSize = GrblConnection.PlannerSize;

    /// <summary>Bytes the serial RX buffer holds; more are lost, as on the real board.</summary>
    const int RxBufferSize = 127;

    /// <summary>Same limit the program shortens lines to (<see cref="GCodeProgram.DefaultMaxLineLength"/>).</summary>
    const int LineBufferSize = GCodeProgram.DefaultMaxLineLength;

    /// <summary>Time from power-up until the banner (Arduino bootloader).</summary>
    const double BootSeconds = 1.0;

    const int TickMs = 10;
    const int Axes = 3;
    const int StartupLines = 2;

    // Grbl 0.8c settings and their '$$' descriptions. $4, $5, $8 and $9 are the machine's values from October 2026.
    static readonly (int Number, double Default, bool Integer, string Description)[] SettingTable =
    [
        (0, 250, false, "x, step/mm"),
        (1, 250, false, "y, step/mm"),
        (2, 250, false, "z, step/mm"),
        (3, 10, true, "step pulse, usec"),
        (4, 250, false, "default feed, mm/min"),
        (5, 500, false, "default seek, mm/min"),
        (6, 192, true, "step port invert mask, int:{0}"),
        (7, 25, true, "step idle delay, msec"),
        (8, 20, false, "acceleration, mm/sec^2"),
        (9, 0.05, false, "junction deviation, mm"),
        (10, 0.1, false, "arc, mm/segment"),
        (11, 25, true, "n-arc correction, int"),
        (12, 3, true, "n-decimals, int"),
        (13, 0, true, "report inches, bool"),
        (14, 1, true, "auto start, bool"),
        (15, 0, true, "invert step enable, bool"),
        (16, 0, true, "hard limits, bool"),
        (17, 0, true, "homing cycle, bool"),
        (18, 0, true, "homing dir invert mask, int:{0}"),
        (19, 25, false, "homing feed, mm/min"),
        (20, 250, false, "homing seek, mm/min"),
        (21, 100, true, "homing debounce, msec"),
        (22, 1, false, "homing pull-off, mm"),
    ];

    // Grbl 1.1 settings, as a typical laser board has them (laser mode on). 1.1 prints no descriptions.
    static readonly (int Number, double Default, bool Integer, string Description)[] SettingTable11 =
    [
        (0, 10, true, ""), (1, 25, true, ""), (2, 0, true, ""), (3, 0, true, ""), (4, 0, true, ""), (5, 0, true, ""),
        (6, 0, true, ""), (10, 1, true, ""), (11, 0.01, false, ""), (12, 0.002, false, ""), (13, 0, true, ""),
        (20, 0, true, ""), (21, 0, true, ""), (22, 0, true, ""), (23, 0, true, ""), (24, 25, false, ""),
        (25, 500, false, ""), (26, 250, true, ""), (27, 1, false, ""), (30, 1000, true, ""), (31, 0, true, ""),
        (32, 1, true, ""),
        (100, 250, false, ""), (101, 250, false, ""), (102, 250, false, ""),
        (110, 500, false, ""), (111, 500, false, ""), (112, 500, false, ""),
        (120, 20, false, ""), (121, 20, false, ""), (122, 20, false, ""),
        (130, 300, false, ""), (131, 300, false, ""), (132, 50, false, ""),
    ];

    // 1.1 answers with a number; the simulator decides with 0.8's texts and translates.
    static readonly Dictionary<string, int> ErrorCodes11 = new()
    {
        ["Expected command letter"] = 1,
        ["Bad number format"] = 2,
        ["Invalid statement"] = 3,
        ["Value < 0.0"] = 4,
        ["Setting disabled"] = 5,
        ["Value < 3 usec"] = 6,
        ["Busy or queue not empty"] = 8,
        ["Alarm lock"] = 9,
        ["Line overflow"] = 11,
        ["Unsupported statement"] = 20,
        ["Modal group violation"] = 21,
        ["Undefined feed rate"] = 22,
        ["Invalid radius"] = 33,
    };

    /// <summary>Reports between two that carry the work offset (Grbl 1.1's REPORT_WCO_REFRESH_IDLE/BUSY_COUNT).</summary>
    const int WcoRefreshIdle = 10, WcoRefreshBusy = 30;

    static readonly string[] HelpText =
    [
        "$$ (view Grbl settings)",
        "$# (view # parameters)",
        "$G (view parser state)",
        "$N (view startup blocks)",
        "$x=value (save Grbl setting)",
        "$Nx=line (save startup block)",
        "$C (check gcode mode)",
        "$X (kill alarm lock)",
        "$H (run homing cycle)",
        "~ (cycle start)",
        "! (feed hold)",
        "? (current status)",
        "ctrl-x (reset Grbl)",
    ];

    enum MachineMode { Idle, Queue, Cycle, Hold, Alarm, CheckMode }

    /// <summary>A move waiting in the planner. Speed in mm/s.</summary>
    readonly record struct Block(double[] Target, double Speed);

    /// <summary>
    /// Part of the line being executed: a move that needs a planner slot (<paramref name="Target"/> set),
    /// or something that waits until all moves are done (M3/M5, G4).
    /// </summary>
    readonly record struct Step(double[]? Target, double Speed, Action? Apply = null, double Dwell = 0);

    readonly object _sync = new();

    readonly bool _v11;
    readonly (int Number, double Default, bool Integer, string Description)[] _settingTable;

    // "EEPROM": survives resets and power cuts.
    readonly Dictionary<int, double> _settings;
    readonly double[][] _coordinates = [new double[Axes], new double[Axes], new double[Axes], new double[Axes], new double[Axes], new double[Axes]];
    readonly double[] _g28 = new double[Axes];
    readonly double[] _g30 = new double[Axes];
    readonly string[] _startupLines = ["", ""];

    // RAM.
    double[] _machine = new double[Axes];
    readonly double[] _g92 = new double[Axes];
    MachineMode _mode = MachineMode.Idle;
    bool _laser, _coolant;
    double _spindle;       // S
    int _wcoCountdown;     // 1.1: reports until the next one with WCO
    readonly StringBuilder _rx = new();
    readonly Queue<Block> _planner = new();
    double _holdSpeed;
    readonly Queue<Step> _steps = new();
    bool _lineActive;
    double _dwellRemaining;

    // G-code parser state.
    int _motion;          // 0-3, or -1 after G80
    bool _absolute = true;
    bool _inches;
    int _plane = 17;
    int _coordinateSystem; // 0 = G54
    double _feed;          // mm/min
    double[] _parserPosition = new double[Axes];

    SimulatorPort? _port;
    bool _unplugged;
    double _bootRemaining;
    int _corruptStatuses;
    double _time;
    TimeSpan? _restartInterval;
    double _nextRestartAt;
    int _restarts;

    Thread? _clock;
    volatile bool _running;
    double _timeScale = 1;

    public GrblSimulator(SimulatedFirmware firmware = SimulatedFirmware.Grbl08c)
    {
        Firmware = firmware;
        _v11 = firmware == SimulatedFirmware.Grbl11;
        _settingTable = _v11 ? SettingTable11 : SettingTable;
        _settings = _settingTable.ToDictionary(s => s.Number, s => s.Default);
        _feed = DefaultFeed;
    }

    public SimulatedFirmware Firmware { get; }

    /// <summary>The banner this firmware prints.</summary>
    public string BannerText => _v11 ? Banner11 : Banner;

    // Where each version keeps the values the simulation needs.
    double StepsPerMm(int axis) => _settings[(_v11 ? 100 : 0) + axis];
    double SeekRate => _v11 ? Math.Min(_settings[110], _settings[111]) : _settings[5];
    double Acceleration => _v11 ? _settings[120] : _settings[8];
    /// <summary>1.1 has no default feed: G1 without an F is an error.</summary>
    double DefaultFeed => _v11 ? 0 : _settings[4];
    /// <summary>1.1 sets the arc tolerance ($12), not the segment length; 0.1 mm is about what that gives.</summary>
    double ArcSegment => _v11 ? 0.1 : _settings[10];
    bool HomingEnabled => _settings[_v11 ? 22 : 17] != 0;
    /// <summary>1.1 always starts moving at once; 0.8 only with $14 (auto start).</summary>
    bool AutoStart => _v11 || _settings[14] != 0;

    /// <summary>How much faster than real time the background clock runs. 1 = real time.</summary>
    public double TimeScale
    {
        get => Volatile.Read(ref _timeScale);
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            Volatile.Write(ref _timeScale, value);
        }
    }

    /// <summary>Simulated time since the simulator was created.</summary>
    public TimeSpan Time { get { lock (_sync) return TimeSpan.FromSeconds(_time); } }

    public Position MachinePosition { get { lock (_sync) return ToPosition(ReportedMachine()); } }

    public Position WorkPosition { get { lock (_sync) return ToPosition(WorkOf(ReportedMachine())); } }

    /// <summary>State as Grbl would report it ("Idle", "Cycle", ...).</summary>
    public string State { get { lock (_sync) return StateName; } }

    /// <summary>M3/M4 in effect, and on 1.1 with a power above 0.</summary>
    public bool LaserOn { get { lock (_sync) return _laser && (!_v11 || _spindle > 0); } }

    /// <summary>The S value Grbl has; 0 after a reset.</summary>
    public double LaserPower { get { lock (_sync) return _spindle; } }

    /// <summary>True between a restart and the banner.</summary>
    public bool IsBooting { get { lock (_sync) return _bootRemaining > 0; } }

    public bool IsConnected { get { lock (_sync) return _port is not null; } }

    /// <summary>Restarts by <see cref="Restart"/> or <see cref="RestartInterval"/>.</summary>
    public int Restarts { get { lock (_sync) return _restarts; } }

    /// <summary>
    /// Restart the board every this much simulated time, counted from when it is set; null = never.
    /// Like the compressors that switch on every 15 minutes.
    /// </summary>
    public TimeSpan? RestartInterval
    {
        get { lock (_sync) return _restartInterval; }
        set
        {
            if (value is { } interval && interval <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(value), value, "Restart interval must be positive.");
            lock (_sync)
            {
                _restartInterval = value;
                _nextRestartAt = _time + (value?.TotalSeconds ?? 0);
            }
        }
    }

    /// <summary>Simulated time until the next scheduled restart, or null if none is scheduled.</summary>
    public TimeSpan? NextRestartIn
    {
        get { lock (_sync) return _restartInterval is null ? null : TimeSpan.FromSeconds(Math.Max(0, _nextRestartAt - _time)); }
    }

    /// <summary>
    /// Opens the simulated serial port. Like a COM port opened without DTR, the board isn't restarted and
    /// says nothing until it is reset; <paramref name="resetOnOpen"/> restarts it as DTR would (banner after a second).
    /// </summary>
    public SimulatorPort Connect(bool resetOnOpen = false)
    {
        lock (_sync)
        {
            if (_port is not null)
                throw new InvalidOperationException("The simulator is already connected.");
            if (_unplugged)
            {
                // Plugged back in: the board powered up while no one was listening.
                _unplugged = false;
                PowerUp();
                _bootRemaining = 0;
            }
            _port = new SimulatorPort(this);
            if (resetOnOpen)
            {
                PowerUp();
                _bootRemaining = BootSeconds;
            }
            return _port;
        }
    }

    /// <summary>Power glitch: the board restarts, machine position becomes 0, laser off; EEPROM stays.</summary>
    public void Restart()
    {
        lock (_sync)
        {
            if (_unplugged)
                return;
            _restarts++;
            PowerUp();
            _bootRemaining = BootSeconds;
        }
    }

    /// <summary>
    /// Pulls the USB cable: the port disappears and the board loses power. <see cref="Connect"/> plugs it back in.
    /// </summary>
    public void Unplug()
    {
        lock (_sync)
        {
            _port?.Unplug();
            _unplugged = true;
            PowerUp();
            _bootRemaining = 0;
        }
    }

    /// <summary>
    /// The next status report loses a few characters in the middle, as when a compressor starts.
    /// Depending on where, it can't be parsed or (worse) reads as a different position.
    /// </summary>
    public void CorruptNextStatus()
    {
        lock (_sync)
            _corruptStatuses++;
    }

    /// <summary>Runs the clock in the background, in real time multiplied by <see cref="TimeScale"/>.</summary>
    public void Start()
    {
        lock (_sync)
        {
            if (_clock is not null)
                return;
            _running = true;
            _clock = new Thread(ClockLoop) { IsBackground = true, Name = "Grbl simulator" };
            _clock.Start();
        }
    }

    public void Dispose()
    {
        _running = false;
        _clock?.Join(1000);
        _clock = null;
        SimulatorPort? port;
        lock (_sync)
            port = _port;
        port?.Dispose();
    }

    void ClockLoop()
    {
        var clock = Stopwatch.StartNew();
        var last = clock.Elapsed;
        while (_running)
        {
            Thread.Sleep(TickMs);
            var now = clock.Elapsed;
            Advance((now - last) * TimeScale);
            last = now;
        }
    }

    /// <summary>Lets <paramref name="elapsed"/> of simulated time pass: moves run, dwells and restarts happen.</summary>
    public void Advance(TimeSpan elapsed)
    {
        double seconds = elapsed.TotalSeconds;
        if (seconds <= 0)
            return;
        lock (_sync)
        {
            if (_unplugged)
                return;
            _time += seconds;

            if (_bootRemaining > 0)
            {
                _bootRemaining -= seconds;
                if (_bootRemaining <= 0)
                {
                    _bootRemaining = 0;
                    Announce();
                }
                return;
            }
            if (_restartInterval is { } interval && _time >= _nextRestartAt)
            {
                _nextRestartAt += interval.TotalSeconds;
                _restarts++;
                PowerUp();
                _bootRemaining = BootSeconds;
                return;
            }

            if (_dwellRemaining > 0)
                _dwellRemaining = Math.Max(0, _dwellRemaining - seconds);
            Move(seconds);
            ProcessInput();
        }
    }

    internal void Receive(SimulatorPort port, byte[] data)
    {
        lock (_sync)
        {
            if (port != _port || _unplugged || _bootRemaining > 0)
                return; // a booting Arduino doesn't hear anything
            foreach (var b in data)
            {
                switch (b)
                {
                    case (byte)'?': ReportStatus(); break;
                    case (byte)'!': FeedHold(); break;
                    case (byte)'~': CycleStart(); break;
                    case 0x18: SoftReset(); break;
                    default:
                        if (_rx.Length < RxBufferSize)
                            _rx.Append((char)b);
                        break;
                }
            }
            ProcessInput();
        }
    }

    internal void Detach(SimulatorPort port)
    {
        lock (_sync)
        {
            if (_port == port)
                _port = null;
        }
    }

    void Output(string line)
    {
        if (_bootRemaining <= 0)
            _port?.Deliver(line);
    }

    void Announce()
    {
        Output("");
        Output(BannerText);
        if (_mode == MachineMode.Alarm)
            Output(_v11 ? "[MSG:'$H'|'$X' to unlock]" : "['$H'|'$X' to unlock]");
    }

    /// <summary>Power-up or watchdog reset: everything in RAM is gone, including the machine position.</summary>
    void PowerUp()
    {
        _machine = new double[Axes];
        ClearRam();
        // With homing enabled Grbl starts locked until $H or $X.
        _mode = HomingEnabled ? MachineMode.Alarm : MachineMode.Idle;
    }

    /// <summary>Ctrl-X. Machine position is kept; a reset during motion leaves Grbl in alarm.</summary>
    void SoftReset()
    {
        bool moving = _mode is MachineMode.Cycle or MachineMode.Hold;
        ClearRam();
        if (moving)
        {
            Output(_v11 ? "ALARM:3" : "ALARM: Abort during cycle");
            _mode = MachineMode.Alarm;
        }
        else if (_mode != MachineMode.Alarm)
        {
            _mode = MachineMode.Idle;
        }
        Announce();
    }

    void ClearRam()
    {
        _rx.Clear();
        _planner.Clear();
        _steps.Clear();
        _lineActive = false;
        _dwellRemaining = 0;
        _holdSpeed = 0;
        _laser = _coolant = false;
        _spindle = 0;
        _wcoCountdown = 0;
        Array.Clear(_g92);
        _motion = 0;
        _absolute = true;
        _inches = false;
        _plane = 17;
        _coordinateSystem = 0;
        _feed = DefaultFeed;
        _parserPosition = (double[])_machine.Clone();
    }

    void FeedHold()
    {
        if (_mode != MachineMode.Cycle)
            return;
        _mode = MachineMode.Hold;
        _holdSpeed = _planner.Count > 0 ? _planner.Peek().Speed : 0;
    }

    void CycleStart()
    {
        if (_mode is MachineMode.Hold or MachineMode.Queue or MachineMode.Idle && _planner.Count > 0)
            _mode = MachineMode.Cycle;
    }

    void ReportStatus()
    {
        var machine = ReportedMachine();
        var work = WorkOf(machine);
        var status = _v11 ? Status11(machine, work) : $"<{StateName},MPos:{N(machine[0])},{N(machine[1])},{N(machine[2])},WPos:{N(work[0])},{N(work[1])},{N(work[2])}>";
        if (_corruptStatuses > 0)
        {
            // Lost bytes: a few characters from the middle are missing.
            _corruptStatuses--;
            int cut = status.Length / 3;
            status = status[..cut] + status[(cut + 6)..];
        }
        Output(status);
    }

    /// <summary>
    /// "&lt;Run|MPos:...|FS:600,1000|WCO:...&gt;": MPos or WPos as $10 says, the offset every 10 (idle) or
    /// 30 (moving) reports and right after it changed.
    /// </summary>
    string Status11(double[] machine, double[] work)
    {
        bool reportMachine = ((int)_settings[10] & 1) != 0;
        var position = reportMachine ? $"MPos:{Triple(machine)}" : $"WPos:{Triple(work)}";
        bool moving = _mode is MachineMode.Cycle or MachineMode.Hold;
        double feed = _mode == MachineMode.Cycle && _planner.Count > 0 ? _planner.Peek().Speed * 60 : 0;
        var status = $"<{StateName}|{position}|FS:{feed:0},{(_laser ? _spindle : 0):0}";
        if (_wcoCountdown <= 0)
        {
            var offset = new double[Axes];
            for (int a = 0; a < Axes; a++)
                offset[a] = machine[a] - work[a];
            status += $"|WCO:{Triple(offset)}";
            _wcoCountdown = moving ? WcoRefreshBusy : WcoRefreshIdle;
        }
        _wcoCountdown--;
        return status + ">";
    }

    string StateName => _mode switch
    {
        MachineMode.Idle => "Idle",
        MachineMode.Queue => "Queue",
        MachineMode.Cycle => _v11 ? "Run" : "Cycle",
        // 1.1: Hold:1 while braking, Hold:0 once standing.
        MachineMode.Hold => _v11 ? (_holdSpeed > 0 ? "Hold:1" : "Hold:0") : "Hold",
        MachineMode.Alarm => "Alarm",
        _ => _v11 ? "Check" : "Check Mode",
    };

    /// <summary>Machine position as the stepper motors have it: whole steps.</summary>
    double[] ReportedMachine()
    {
        var steps = new double[Axes];
        for (int a = 0; a < Axes; a++)
        {
            double perMm = StepsPerMm(a);
            steps[a] = perMm > 0 ? Math.Round(_machine[a] * perMm) / perMm : _machine[a];
        }
        return steps;
    }

    double[] WorkOf(double[] machine)
    {
        var work = new double[Axes];
        for (int a = 0; a < Axes; a++)
            work[a] = machine[a] - _coordinates[_coordinateSystem][a] - _g92[a];
        return work;
    }

    void Move(double seconds)
    {
        double remaining = seconds;
        while (remaining > 1e-12 && _mode is MachineMode.Cycle or MachineMode.Hold && _planner.Count > 0)
        {
            var block = _planner.Peek();
            double distance = Distance(_machine, block.Target);
            if (distance < 1e-9 || block.Speed <= 0)
            {
                FinishBlock(block);
                continue;
            }

            if (_mode == MachineMode.Cycle)
            {
                double travel = block.Speed * remaining;
                if (travel >= distance)
                {
                    remaining -= distance / block.Speed;
                    FinishBlock(block);
                }
                else
                {
                    MoveToward(block.Target, travel / distance);
                    remaining = 0;
                }
                continue;
            }

            // 1.1 stands in "Hold:0" until '~'; 0.8 turns that into Queue below.
            if (_v11 && _holdSpeed <= 0)
                break;

            // Feed hold: brake at the configured acceleration, possibly over several blocks.
            double accel = Math.Max(Acceleration, 1e-3);
            double v = _holdSpeed;
            double t = Math.Min(remaining, v / accel);
            double stopTravel = v * t - accel * t * t / 2;
            if (stopTravel >= distance)
            {
                double reach = (v - Math.Sqrt(Math.Max(0, v * v - 2 * accel * distance))) / accel;
                _holdSpeed = v - accel * reach;
                remaining -= reach;
                FinishBlock(block);
            }
            else
            {
                MoveToward(block.Target, stopTravel / distance);
                _holdSpeed = v - accel * t;
                remaining -= t;
            }
            if (_holdSpeed <= 1e-9)
            {
                _holdSpeed = 0;
                if (_v11 && _planner.Count > 0)
                    break;
                _mode = _planner.Count > 0 ? MachineMode.Queue : MachineMode.Idle;
            }
        }
        if (_mode is MachineMode.Cycle or MachineMode.Hold && _planner.Count == 0)
            _mode = MachineMode.Idle;
    }

    void FinishBlock(Block block)
    {
        _machine = (double[])block.Target.Clone();
        _planner.Dequeue();
        // A slot is free: a waiting motion line can go in now.
        ProcessInput();
    }

    void MoveToward(double[] target, double fraction)
    {
        for (int a = 0; a < Axes; a++)
            _machine[a] += (target[a] - _machine[a]) * fraction;
    }

    static double Distance(double[] a, double[] b)
    {
        double sum = 0;
        for (int i = 0; i < Axes; i++)
            sum += (b[i] - a[i]) * (b[i] - a[i]);
        return Math.Sqrt(sum);
    }

    // ----- Lines -----

    void ProcessInput()
    {
        while (true)
        {
            if (_lineActive)
            {
                if (!RunSteps())
                    return;
                _lineActive = false;
                Output("ok");
                continue;
            }

            int end = -1;
            for (int i = 0; i < _rx.Length; i++)
            {
                if (_rx[i] is '\n' or '\r')
                {
                    end = i;
                    break;
                }
            }
            if (end < 0)
                return;
            var raw = _rx.ToString(0, end);
            _rx.Remove(0, end + 1);
            ExecuteLine(raw);
        }
    }

    /// <summary>Feeds the current line's moves into the planner. True once the whole line is done.</summary>
    bool RunSteps()
    {
        while (_steps.Count > 0)
        {
            if (_dwellRemaining > 0)
                return false;
            var step = _steps.Peek();
            if (step.Target is { } target)
            {
                if (_planner.Count >= PlannerSize)
                    return false;
                _planner.Enqueue(new Block(target, step.Speed));
                if (_mode == MachineMode.Idle)
                    _mode = AutoStart ? MachineMode.Cycle : MachineMode.Queue;
            }
            else
            {
                // Like plan_synchronize(): wait for every move before it, also through a feed hold.
                if (_planner.Count > 0)
                    return false;
                step.Apply?.Invoke();
                _dwellRemaining = step.Dwell;
            }
            _steps.Dequeue();
        }
        return _dwellRemaining <= 0;
    }

    void ExecuteLine(string raw)
    {
        var line = new StringBuilder();
        bool comment = false;
        foreach (char c in raw)
        {
            if (comment)
            {
                if (c == ')')
                    comment = false;
            }
            else if (c == '(')
            {
                comment = true;
            }
            else if (c == ';')
            {
                break;
            }
            else if (c > ' ' && c != '/')
            {
                line.Append(char.ToUpperInvariant(c));
            }
        }

        string? error;
        if (line.Length > LineBufferSize)
            error = "Line overflow";
        else if (line.Length == 0)
            error = null;
        else if (line[0] == '$')
            error = SystemCommand(line.ToString());
        else if (_mode == MachineMode.Alarm)
            error = "Alarm lock";
        else
            error = GCode(line.ToString());

        if (error is not null)
        {
            _steps.Clear();
            if (error.Length > 0)
                Output(_v11 ? $"error:{ErrorCodes11.GetValueOrDefault(error, 20)}" : "error: " + error);
            return;
        }
        // ProcessInput answers once the line's moves are in the planner.
        if (_steps.Count > 0)
            _lineActive = true;
        else
        {
            Output("ok");
        }
    }

    /// <summary>Marks a command that has already printed its answer.</summary>
    const string Answered = "";

    string? SystemCommand(string line)
    {
        switch (line)
        {
            case "$":
                if (_v11)
                    Output("[HLP:$$ $# $G $I $N $x=val $Nx=line $J=line $SLP $C $X $H ~ ! ? ctrl-x]");
                else
                    foreach (var help in HelpText)
                        Output(help);
                return null;
            case "$$":
                foreach (var s in _settingTable)
                {
                    double value = _settings[s.Number];
                    var text = s.Integer ? ((int)value).ToString(CultureInfo.InvariantCulture) : N(value);
                    if (_v11)
                    {
                        Output($"${s.Number}={text}");
                        continue;
                    }
                    var description = string.Format(CultureInfo.InvariantCulture, s.Description, Convert.ToString((int)value, 2).PadLeft(8, '0'));
                    Output($"${s.Number}={text} ({description})");
                }
                return null;
            case "$#":
                for (int i = 0; i < _coordinates.Length; i++)
                    Output($"[G{54 + i}:{Triple(_coordinates[i])}]");
                Output($"[G28:{Triple(_g28)}]");
                Output($"[G30:{Triple(_g30)}]");
                Output($"[G92:{Triple(_g92)}]");
                return null;
            case "$G":
                var motion = _motion < 0 ? "G80" : $"G{_motion}";
                var units = _inches ? 25.4 : 1;
                var state = $"{motion} G{54 + _coordinateSystem} G{_plane} G{(_inches ? 20 : 21)} G{(_absolute ? 90 : 91)} G94 M0 M{(_laser ? 3 : 5)} M{(_coolant ? 8 : 9)} T0 F{N(_feed / units)}";
                Output(_v11 ? $"[GC:{state} S{_spindle:0}]" : $"[{state}]");
                return null;
            case "$N":
                for (int i = 0; i < StartupLines; i++)
                    Output($"$N{i}={_startupLines[i]}");
                return null;
            case "$C":
                if (_mode == MachineMode.CheckMode)
                {
                    Output("[Disabled]");
                    Output("ok");
                    SoftReset();
                    return Answered;
                }
                if (_mode != MachineMode.Idle)
                    return "Busy or queue not empty";
                _mode = MachineMode.CheckMode;
                Output("[Enabled]");
                return null;
            case "$X":
                if (_mode == MachineMode.Alarm)
                {
                    Output(_v11 ? "[MSG:Caution: Unlocked]" : "[Caution: Unlocked]");
                    _mode = MachineMode.Idle;
                }
                return null;
            case "$H":
                if (!HomingEnabled)
                    return "Setting disabled";
                if (_mode is not (MachineMode.Idle or MachineMode.Alarm))
                    return "Busy or queue not empty";
                // Pretend the switches sit at machine zero.
                _machine = new double[Axes];
                _parserPosition = new double[Axes];
                _mode = MachineMode.Idle;
                return null;
        }

        // $x=value or $Nx=line
        int pos = 1;
        bool startup = line.Length > 1 && line[1] == 'N';
        if (startup)
            pos++;
        int equals = line.IndexOf('=');
        if (equals < 0)
            return int.TryParse(line[pos..], NumberStyles.Integer, CultureInfo.InvariantCulture, out _) ? "Unsupported statement" : "Bad number format";
        if (!int.TryParse(line[pos..equals], NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            return "Bad number format";
        var valueText = line[(equals + 1)..];

        if (startup)
        {
            if (number < 0 || number >= StartupLines)
                return "Invalid statement";
            _startupLines[number] = valueText;
            return null;
        }
        if (!double.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out var setting))
            return "Bad number format";
        if (!_settings.ContainsKey(number))
            return "Invalid statement";
        if (setting < 0)
            return "Value < 0.0";
        if (number == (_v11 ? 0 : 3) && setting < 3)
            return "Value < 3 usec";
        _settings[number] = setting;
        return null;
    }

    string? GCode(string line)
    {
        var words = new List<(char Letter, decimal Number)>();
        for (int i = 0; i < line.Length;)
        {
            char letter = line[i++];
            if (letter is < 'A' or > 'Z')
                return "Expected command letter";
            int start = i;
            while (i < line.Length && (char.IsAsciiDigit(line[i]) || line[i] is '.' or '-' or '+'))
                i++;
            if (!decimal.TryParse(line[start..i], NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                return "Bad number format";
            words.Add((letter, number));
        }

        int? motion = null;
        decimal? nonModal = null;
        bool machineCoordinates = false;
        bool? laser = null, coolant = null;
        bool programEnd = false;
        var values = new Dictionary<char, double>();

        foreach (var (letter, number) in words)
        {
            switch (letter)
            {
                case 'G':
                    switch (number)
                    {
                        case 0 or 1 or 2 or 3: motion = (int)number; break;
                        case 80: motion = -1; break;
                        case 4 or 10 or 28 or 30 or 92 or 28.1m or 30.1m or 92.1m:
                            if (nonModal is not null)
                                return "Modal group violation";
                            nonModal = number;
                            break;
                        case 53: machineCoordinates = true; break;
                        case 17 or 18 or 19: _plane = (int)number; break;
                        case 20: _inches = true; break;
                        case 21: _inches = false; break;
                        case >= 54 and <= 59 when number == decimal.Truncate(number): _coordinateSystem = (int)number - 54; _wcoCountdown = 0; break;
                        case 90: _absolute = true; break;
                        case 91: _absolute = false; break;
                        case 93 or 94: break;
                        default: return "Unsupported statement";
                    }
                    break;
                case 'M':
                    switch (number)
                    {
                        case 0 or 1: break;
                        case 2 or 30: programEnd = true; break;
                        case 3 or 4: laser = true; break;
                        case 5: laser = false; break;
                        case 8: coolant = true; break;
                        case 9: coolant = false; break;
                        default: return "Unsupported statement";
                    }
                    break;
                case 'N':
                    break;
                case 'F' or 'I' or 'J' or 'K' or 'L' or 'P' or 'R' or 'S' or 'T' or 'X' or 'Y' or 'Z':
                    values[letter] = (double)number;
                    break;
                default:
                    return "Unsupported statement";
            }
        }

        double scale = _inches ? 25.4 : 1;
        if (values.TryGetValue('F', out var feed))
        {
            if (feed <= 0)
                return "Invalid statement";
            _feed = feed * scale;
        }
        if (values.TryGetValue('S', out var power))
        {
            if (power < 0)
                return "Value < 0.0";
            Sync(() => _spindle = power);
        }
        if (laser is { } on)
            Sync(() => _laser = on);
        if (coolant is { } c)
            Sync(() => _coolant = c);

        bool hasAxes = values.ContainsKey('X') || values.ContainsKey('Y') || values.ContainsKey('Z');
        bool axesUsed = false;
        switch (nonModal)
        {
            case 4:
                _steps.Enqueue(new Step(null, 0, Dwell: values.GetValueOrDefault('P')));
                break;
            case 10:
                if (values.GetValueOrDefault('L') != 2)
                    return "Unsupported statement";
                int p = (int)values.GetValueOrDefault('P');
                if (p < 0 || p > _coordinates.Length)
                    return "Invalid statement";
                var offsets = _coordinates[p == 0 ? _coordinateSystem : p - 1];
                ForEachAxis(values, (a, v) => offsets[a] = v * scale);
                axesUsed = true;
                _wcoCountdown = 0;
                break;
            case 28 or 30:
                if (hasAxes)
                    AddMove(Target(values, scale, false), SeekRate);
                AddMove((double[])(nonModal == 28 ? _g28 : _g30).Clone(), SeekRate);
                axesUsed = true;
                break;
            case 28.1m:
                Array.Copy(_parserPosition, _g28, Axes);
                break;
            case 30.1m:
                Array.Copy(_parserPosition, _g30, Axes);
                break;
            case 92:
                ForEachAxis(values, (a, v) => _g92[a] = _parserPosition[a] - _coordinates[_coordinateSystem][a] - v * scale);
                axesUsed = true;
                _wcoCountdown = 0;
                break;
            case 92.1m:
                Array.Clear(_g92);
                _wcoCountdown = 0;
                break;
        }

        if (motion is { } m)
            _motion = m;
        if (hasAxes && !axesUsed)
        {
            if (_motion < 0)
                return "Invalid statement";
            if (_motion is 1 or 2 or 3 && _feed <= 0)
                return "Undefined feed rate";
            var target = Target(values, scale, machineCoordinates);
            if (_motion is 2 or 3)
            {
                if (AddArc(target, values, scale) is { } arcError)
                    return arcError;
            }
            else
            {
                AddMove(target, _motion == 0 ? SeekRate : _feed);
            }
        }

        if (programEnd)
        {
            _absolute = true;
            _plane = 17;
            _coordinateSystem = 0;
            Sync(() => _laser = _coolant = false);
        }
        return null;
    }

    void Sync(Action apply) => _steps.Enqueue(new Step(null, 0, apply));

    /// <summary>Machine position the axis words point to.</summary>
    double[] Target(Dictionary<char, double> values, double scale, bool machineCoordinates)
    {
        var target = (double[])_parserPosition.Clone();
        ForEachAxis(values, (a, v) =>
        {
            v *= scale;
            if (machineCoordinates)
                target[a] = v;
            else if (_absolute)
                target[a] = v + _coordinates[_coordinateSystem][a] + _g92[a];
            else
                target[a] += v;
        });
        return target;
    }

    static void ForEachAxis(Dictionary<char, double> values, Action<int, double> action)
    {
        if (values.TryGetValue('X', out var x)) action(0, x);
        if (values.TryGetValue('Y', out var y)) action(1, y);
        if (values.TryGetValue('Z', out var z)) action(2, z);
    }

    void AddMove(double[] target, double mmPerMinute)
    {
        if (_mode != MachineMode.CheckMode)
            _steps.Enqueue(new Step(target, mmPerMinute / 60));
        _parserPosition = target;
    }

    /// <summary>G2/G3 as short straight moves of $10 mm, like Grbl's mc_arc.</summary>
    string? AddArc(double[] target, Dictionary<char, double> values, double scale)
    {
        var (a0, a1, a2, o0, o1) = _plane switch
        {
            18 => (2, 0, 1, 'K', 'I'),
            19 => (1, 2, 0, 'J', 'K'),
            _ => (0, 1, 2, 'I', 'J'),
        };
        var start = _parserPosition;
        bool clockwise = _motion == 2;
        double i, j;
        if (values.TryGetValue('R', out var r))
        {
            r *= scale;
            double dx = target[a0] - start[a0], dy = target[a1] - start[a1];
            double d = Math.Sqrt(dx * dx + dy * dy);
            double h2 = 4 * r * r - dx * dx - dy * dy;
            if (d == 0 || h2 < 0)
                return "Invalid radius";
            double h = -Math.Sqrt(h2) / d;
            if (!clockwise) h = -h;
            if (r < 0) h = -h;
            i = 0.5 * (dx - dy * h);
            j = 0.5 * (dy + dx * h);
        }
        else
        {
            i = values.GetValueOrDefault(o0) * scale;
            j = values.GetValueOrDefault(o1) * scale;
        }

        double c0 = start[a0] + i, c1 = start[a1] + j;
        double radius = Math.Sqrt(i * i + j * j);
        double startAngle = Math.Atan2(start[a1] - c1, start[a0] - c0);
        double sweep = Math.Atan2(target[a1] - c1, target[a0] - c0) - startAngle;
        bool fullCircle = Math.Abs(target[a0] - start[a0]) < 1e-9 && Math.Abs(target[a1] - start[a1]) < 1e-9;
        if (clockwise) { if (sweep >= 0 || fullCircle) sweep -= 2 * Math.PI; }
        else { if (sweep <= 0 || fullCircle) sweep += 2 * Math.PI; }

        double linear = target[a2] - start[a2];
        double length = Math.Sqrt(Math.Pow(Math.Abs(sweep) * radius, 2) + linear * linear);
        int segments = Math.Max(1, (int)Math.Floor(length / Math.Max(ArcSegment, 1e-3)));
        var from = (double[])start.Clone();
        for (int k = 1; k < segments; k++)
        {
            double angle = startAngle + sweep * k / segments;
            var point = (double[])from.Clone();
            point[a0] = c0 + radius * Math.Cos(angle);
            point[a1] = c1 + radius * Math.Sin(angle);
            point[a2] = start[a2] + linear * k / segments;
            AddMove(point, _feed);
        }
        AddMove(target, _feed);
        return null;
    }

    static Position ToPosition(double[] p) => new(p[0], p[1], p[2]);

    static string Triple(double[] p) => $"{N(p[0])},{N(p[1])},{N(p[2])}";

    // Rounded first so -0.0001 shows as 0.000, not -0.000.
    static string N(double value) => (Math.Round(value, 3) + 0.0).ToString("0.000", CultureInfo.InvariantCulture);
}
