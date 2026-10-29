using System.Collections.Concurrent;
using GrblCore;
using GrblCore.Simulation;

namespace CncController.App.Tests;

/// <summary>Runs posted actions one after another on its own thread, like the UI thread on the desktop.</summary>
sealed class Dispatcher : IDisposable
{
    readonly BlockingCollection<Action> _queue = new();
    readonly Thread _thread;

    public Dispatcher()
    {
        _thread = new Thread(() =>
        {
            foreach (var action in _queue.GetConsumingEnumerable())
                action();
        }) { IsBackground = true, Name = "Session" };
        _thread.Start();
    }

    public void Post(Action action)
    {
        if (!_queue.IsAddingCompleted)
            _queue.Add(action);
    }

    public T Invoke<T>(Func<T> func)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            try { done.SetResult(func()); }
            catch (Exception ex) { done.SetException(ex); }
        });
        return done.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
    }

    public void Invoke(Action action) => Invoke(() => { action(); return true; });

    public void Dispose() => _queue.CompleteAdding();
}

/// <summary><see cref="MachineSession"/> against the simulator, with real threads, like the desktop runs it.</summary>
public sealed class MachineSessionTests : IDisposable
{
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    readonly Dispatcher _ui = new();
    readonly PositionMemory _memory = new();
    readonly AppSettings _settings = new();
    readonly MachineSession _session;
    readonly ConcurrentQueue<string> _log = new();
    readonly ConcurrentQueue<(string Title, string Text)> _notifications = new();
    readonly string _file = Path.Combine(Path.GetTempPath(), $"session-test-{Guid.NewGuid():N}.tap");
    // Added to the real time, so a test can skip the session's waits (5 s settle, compressor window).
    TimeSpan _shift;

    public MachineSessionTests()
    {
        _session = new MachineSession(_settings, _memory, logFolder: null, _ui.Post, () => DateTime.Now + _shift);
        _session.Logged += (_, text) => _log.Enqueue(text);
        _session.Notified += (title, text) => _notifications.Enqueue((title, text));
    }

    public void Dispose()
    {
        _ui.Invoke(_session.Dispose);
        _ui.Dispose();
        File.Delete(_file);
    }

    GrblSimulator Simulator => _ui.Invoke(() => _session.Simulator!);

    void ConnectSimulator(string port = MachineSession.SimulatorPortName)
    {
        Assert.True(_ui.Invoke(() => _session.Connect(port)));
        Simulator.TimeScale = 20;
        WaitUntil(() => _session.IsReady && _session.LastStatus is not null);
    }

    void LoadSquares(int repeats)
    {
        File.WriteAllLines(_file,
            ["G21 G90", "M3", .. Enumerable.Repeat<string[]>(["G1 X5 Y0 F3000", "G1 X5 Y5", "G1 X0 Y5", "G1 X0 Y0"], repeats).SelectMany(l => l), "M5", "G0 X1 Y2"]);
        Assert.True(_ui.Invoke(() => _session.LoadFile(_file)));
    }

    /// <summary>Polls on the session's thread, calling <see cref="MachineSession.Tick"/> like the desktop timer.</summary>
    void WaitUntil(Func<bool> condition, bool tick = false)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!_ui.Invoke(() => { if (tick) _session.Tick(); return condition(); }))
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Log:" + Environment.NewLine + string.Join(Environment.NewLine, _log));
            Thread.Sleep(20);
        }
    }

    bool Logged(string part) => _log.Any(l => l.Contains(part));

    [Fact]
    public void Connect_NothingRemembered_PositionConfirmedWithoutAsking()
    {
        ConnectSimulator();

        WaitUntil(() => _session.PositionConfirmed);
        Assert.Null(_ui.Invoke(() => _session.PendingQuestion));
        Assert.True(_ui.Invoke(() => _session.CanMove));
    }

    [Fact]
    public void Jog_BeforePositionCheck_SendsNothing()
    {
        _memory.WorkPosition = new Position(10, 20, 0);
        _memory.MachinePosition = new Position(40, 50, 0);
        ConnectSimulator();
        WaitUntil(() => _session.PendingQuestion is not null);

        _ui.Invoke(() => _session.Jog(Axis.X, 10));

        Assert.False(_ui.Invoke(() => _session.CanMove));
        Thread.Sleep(200);
        Assert.Equal(new Position(0, 0, 0), Simulator.MachinePosition);
    }

    [Fact]
    public void SetZero_RightAfterAMove_WaitsForAStatusAfterIt()
    {
        ConnectSimulator();
        WaitUntil(() => _session.CanMove);

        // Grbl answers the jog before it moves; the last status still says Idle at the old spot.
        Assert.False(_ui.Invoke(() => { _session.Jog(Axis.X, 10); return _session.SetZero(); }));
        Assert.True(Logged("čekam svjež status"));

        WaitUntil(() => _session.LastStatus is { State: MachineState.Idle, WorkPosition.X: > 9.99 });
        Assert.True(_ui.Invoke(_session.SetZero));
        WaitUntil(() => _session.PositionConfirmed);
        Assert.True(PositionRecovery.Same(new Position(0, 0, 0), Simulator.WorkPosition));
    }

    [Fact]
    public void PositionQuestion_RestartWhileOpen_AskedOnce_AnswerRestores()
    {
        _memory.WorkPosition = new Position(10, 20, 0);
        _memory.MachinePosition = new Position(40, 50, 0);
        ConnectSimulator();
        WaitUntil(() => _session.PendingQuestion is not null);
        var question = _ui.Invoke(() => _session.PendingQuestion!);
        Assert.Equal(new Position(10, 20, 0), question.Target);
        int changes = 0;
        _session.QuestionChanged += () => changes++;

        Simulator.Restart();
        WaitUntil(() => Logged("dok čeka odgovor o poziciji (1. put)"));
        WaitUntil(() => _session.LastStatus?.State == MachineState.Idle);
        Assert.Same(question, _ui.Invoke(() => _session.PendingQuestion));

        _ui.Invoke(() => _session.AnswerPositionQuestion(question, restore: true));
        // A second answer (another screen, a moment later) is ignored.
        _ui.Invoke(() => _session.AnswerPositionQuestion(question, restore: false));

        WaitUntil(() => _session.PositionConfirmed);
        Assert.Equal(1, changes);
        Assert.True(Logged("Grbl se restartovao još 1 put(a)"));
        WaitUntil(() => _session.LastStatus?.WorkPosition == new Position(10, 20, 0));
    }

    [Fact]
    public void Disconnect_ClearsTheQuestion()
    {
        _memory.WorkPosition = new Position(10, 20, 0);
        _memory.MachinePosition = new Position(40, 50, 0);
        ConnectSimulator();
        WaitUntil(() => _session.PendingQuestion is not null);

        _ui.Invoke(_session.Disconnect);

        Assert.Null(_ui.Invoke(() => _session.PendingQuestion));
        Assert.Equal(ConnectionStatus.Disconnected, _ui.Invoke(() => _session.Connection));
    }

    [Fact]
    public void RestartDuringJob_AutoResume_ContinuesAndCompletes()
    {
        _settings.AutoResume = true;
        ConnectSimulator();
        WaitUntil(() => _session.PositionConfirmed);
        LoadSquares(100);
        _ui.Invoke(() => _session.StartJob());
        WaitUntil(() => _session.Job!.CompletedCount > 60);

        Simulator.Restart();

        WaitUntil(() => Logged("Automatski nastavak: radna pozicija vraćena"));
        // Grbl has to stay idle for 5 s before the job continues; skip that wait.
        WaitUntil(() => _session.PositionConfirmed && _session.LastStatus?.State == MachineState.Idle, tick: true);
        _shift += TimeSpan.FromSeconds(6);
        WaitUntil(() => _session.Job?.State == JobState.Completed, tick: true);

        Assert.True(Logged("Automatski nastavak 1/10"));
        Assert.True(Logged("Raspored pauze naučen iz restarta: oko"));
        Assert.True(PositionRecovery.Same(new Position(1, 2, 0), Simulator.WorkPosition), Simulator.WorkPosition.ToString());
        Assert.False(Simulator.LaserOn);
        Assert.False(_ui.Invoke(() => _session.AutoResumePending));
        // Nobody watched: the phone hears it went through, with the restart.
        var (title, text) = Assert.Single(_notifications);
        Assert.Equal("Posao završen", title);
        Assert.Contains("Grbl se restartovao 1 put(a), automatskih nastavaka: 1.", text);
        Assert.Equal(JobEventKind.Restart, Assert.Single(_ui.Invoke(() => _session.JobEvents.ToList())).Kind);
    }

    [Fact]
    public void RestartDuringJob_AutoResumeOff_WaitsForAPerson()
    {
        ConnectSimulator();
        WaitUntil(() => _session.PositionConfirmed);
        LoadSquares(100);
        _ui.Invoke(() => _session.StartJob());
        WaitUntil(() => _session.Job!.CompletedCount > 60);

        Simulator.Restart();

        WaitUntil(() => _session.Job!.State == JobState.Aborted);
        WaitUntil(() => _session.PendingQuestion is not null);
        Assert.True(Logged("Posao prekinut jer se Grbl restartovao."));
        Assert.False(_ui.Invoke(() => _session.AutoResumePending));
    }

    [Fact]
    public void CompressorPause_StopsWithLaserOff_ThenContinues()
    {
        ConnectSimulator();
        WaitUntil(() => _session.PositionConfirmed);
        LoadSquares(150);
        _ui.Invoke(() => _session.StartJob());
        WaitUntil(() => _session.Job!.CompletedCount > 20);

        // The compressors switch on in 5 s: already inside the quiet window.
        _ui.Invoke(() =>
        {
            _session.SetCompressors(new CompressorSchedule(DateTime.Now + _shift + TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(15)));
            _session.CompressorPause = true;
        });

        WaitUntil(() => _session.Job!.IsBreakSettled, tick: true);
        WaitUntil(() => Logged("CNC stoji, laser ugašen."), tick: true);
        Assert.False(Simulator.LaserOn);
        Assert.True(Logged("Planirana pauza (≈"));

        _shift += TimeSpan.FromSeconds(16);
        WaitUntil(() => _session.Job!.State == JobState.Completed, tick: true);

        Assert.True(Logged("Planirana pauza je gotova, nastavljam posao."));
        Assert.True(PositionRecovery.Same(new Position(1, 2, 0), Simulator.WorkPosition), Simulator.WorkPosition.ToString());
        // On the job's timeline: the break, with the lines done by then and when it was to continue.
        var pause = Assert.Single(_ui.Invoke(() => _session.JobEvents.ToList()));
        Assert.Equal(JobEventKind.CompressorBreak, pause.Kind);
        Assert.True(pause.Line > 0);
        Assert.NotNull(pause.Until);
    }

    [Fact]
    public void Grbl11_LaserButtonAndJob_UseTheLaserPower()
    {
        _settings.LaserPower = 400;
        ConnectSimulator(MachineSession.Simulator11PortName);
        WaitUntil(() => _session.CanMove);
        Assert.True(_ui.Invoke(() => _session.UsesLaserPower));

        // M3 alone would leave 1.1's laser dark (S0 after a reset).
        _ui.Invoke(_session.ToggleLaser);
        WaitUntil(() => _session.Simulator!.LaserOn);
        Assert.Equal(400, Simulator.LaserPower);
        _ui.Invoke(_session.ToggleLaser);
        WaitUntil(() => !_session.Simulator!.LaserOn);

        // The squares' "M3" has no S: the job sends the power first.
        Assert.True(_ui.Invoke(() => _session.SetLaserPower(250)));
        LoadSquares(2);
        _ui.Invoke(() => _session.StartJob());
        WaitUntil(() => _session.Job?.State == JobState.Completed);
        Assert.Equal(250, Simulator.LaserPower);
    }

    [Fact]
    public void Grbl11_NumberedErrorsAreExplained()
    {
        ConnectSimulator(MachineSession.Simulator11PortName);
        WaitUntil(() => _session.CanSendCommands);

        _ui.Invoke(() => _session.SendCommand("G1 X10"));

        WaitUntil(() => Logged("error:22 (nedostaje brzina (F))"));
    }

    [Fact]
    public void Grbl11_Settings_UpdateTheEstimateAndFullPower()
    {
        ConnectSimulator(MachineSession.Simulator11PortName);
        WaitUntil(() => _session.CanGrblSettings);

        Assert.Null(_ui.Invoke(_session.ReadGrblSettings));
        WaitUntil(() => _session.GrblSettings is not null);

        Assert.Equal(0.01, _settings.JunctionDeviation);
        Assert.Equal(500, _settings.SeekRate);
        Assert.Equal(1000, _ui.Invoke(() => _session.LaserPowerMax));
        Assert.Equal("Najveća brzina X (mm/min) - i za prazni hod G0", Texts.GrblSettingMeaning(_ui.Invoke(() => _session.Version), 110, Language.Bosnian));
    }
}
