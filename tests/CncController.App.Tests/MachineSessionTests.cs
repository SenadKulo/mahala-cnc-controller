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
        // One job in the history, the automatic resume included.
        var record = Assert.Single(_ui.Invoke(() => _session.History.Records.ToList()));
        Assert.Equal(JobOutcome.Finished, record.Outcome);
        Assert.Equal((1, 1), (record.Restarts, record.AutoResumes));
        Assert.Equal(record.Lines, record.LinesDone);
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
        var aborted = Assert.Single(_ui.Invoke(() => _session.History.Records.ToList()));
        Assert.Equal(JobOutcome.GrblRestarted, aborted.Outcome);
        Assert.Equal(1, aborted.Restarts);
        Assert.NotNull(aborted.Ended);
    }

    [Fact]
    public void History_StopAndEmergencyStop_EndTheirJobs()
    {
        ConnectSimulator();
        WaitUntil(() => _session.PositionConfirmed);
        LoadSquares(100);

        _ui.Invoke(() => _session.StartJob());
        WaitUntil(() => _session.Job!.CompletedCount > 20);
        // Running: in the history from the start, but not on the page yet.
        Assert.Equal(JobOutcome.Running, Assert.Single(_ui.Invoke(() => _session.History.Records.ToList())).Outcome);
        Assert.Empty(_ui.Invoke(() => new PageApi(_session, _ui.Post, Path.GetTempPath()).BuildHistory()));
        _ui.Invoke(_session.Stop);
        WaitUntil(() => _session.History.Records[^1].Outcome != JobOutcome.Running);

        WaitUntil(() => _session.CanStart && _session.LastStatus?.State == MachineState.Idle);
        _ui.Invoke(() => _session.StartJob());
        WaitUntil(() => _session.Job!.CompletedCount > 20);
        _ui.Invoke(_session.Reset);
        WaitUntil(() => _session.History.Records.Count == 2 && _session.History.Records[^1].Outcome != JobOutcome.Running);

        var records = _ui.Invoke(() => _session.History.Records.ToList());
        Assert.Equal([JobOutcome.Stopped, JobOutcome.EmergencyStop], records.Select(r => r.Outcome));
        Assert.All(records, r => Assert.InRange(r.LinesDone, 1, r.Lines - 1));
        Assert.All(records, r => Assert.True(r.EstimateDoneSeconds > 0 && r.EstimateDoneSeconds < r.EstimateSeconds));
        // On the page: newest first, in the program's language.
        var shown = _ui.Invoke(() => new PageApi(_session, _ui.Post, Path.GetTempPath()).BuildHistory());
        Assert.Equal(["Hitni stop", "Zaustavljen"], shown.Select(j => j.Outcome));
    }

    [Fact]
    public void PauseLaserOff_StopsWithLaserOff_ResumeContinues()
    {
        ConnectSimulator();
        WaitUntil(() => _session.PositionConfirmed);
        LoadSquares(100);
        _ui.Invoke(() => _session.StartJob());
        WaitUntil(() => _session.Job!.CompletedCount > 20);

        Assert.True(_ui.Invoke(() => _session.CanPauseLaserOff));
        _ui.Invoke(_session.PauseLaserOff);
        WaitUntil(() => Logged("CNC stoji, laser ugašen. \"Nastavi\""), tick: true);
        Assert.True(Logged("Pauza sa ugašenim laserom: CNC staje"));
        Assert.False(Simulator.LaserOn);
        Assert.Equal("Idle", Simulator.State);
        Assert.Equal("Pauza, laser ugašen", _ui.Invoke(() => Texts.JobState(_session)));
        Assert.False(_ui.Invoke(() => _session.CanPauseLaserOff));
        // Switching the compressor pause off doesn't end a pause the user asked for.
        _ui.Invoke(() => { _session.CompressorPause = true; _session.CompressorPause = false; });
        Assert.True(_ui.Invoke(() => _session.LaserOffPause));

        _ui.Invoke(_session.Resume);
        WaitUntil(() => _session.Job!.State == JobState.Completed, tick: true);

        Assert.True(Logged("Nastavljam posao nakon pauze."));
        Assert.False(_ui.Invoke(() => _session.LaserOffPause));
        Assert.True(PositionRecovery.Same(new Position(1, 2, 0), Simulator.WorkPosition), Simulator.WorkPosition.ToString());
        Assert.Empty(_ui.Invoke(() => _session.JobEvents.ToList()));
        var record = Assert.Single(_ui.Invoke(() => _session.History.Records.ToList()));
        Assert.Equal(JobOutcome.Finished, record.Outcome);
        Assert.Equal(1, record.Pauses);
        Assert.Equal(record.Lines, record.LinesDone);
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
        var breakRecord = Assert.Single(_ui.Invoke(() => _session.History.Records.ToList()));
        Assert.Equal(JobOutcome.Finished, breakRecord.Outcome);
        Assert.Equal(1, breakRecord.CompressorBreaks);
        // The break isn't running time: from start to end took longer than the CNC ran.
        Assert.True((breakRecord.Ended!.Value - breakRecord.Started).TotalSeconds > breakRecord.RunSeconds + 10);
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

    [Fact]
    public void EngraveText_MakesAFileThatRunsOnTheSimulator()
    {
        var uploads = Path.Combine(Path.GetTempPath(), $"engrave-{Guid.NewGuid():N}");
        var api = new PageApi(_session, _ui.Post, uploads);
        ConnectSimulator();
        WaitUntil(() => _session.PositionConfirmed);

        CncController.Remote.CommandReply Engrave(string text, double height = 5, int feed = 3000, int passes = 1) =>
            _ui.Invoke(() => api.Execute(new CncController.Remote.RemoteCommand
            {
                Name = CncController.Remote.RemoteCommands.EngraveText, Text = text, Number = height, Feeds = [feed], Passes = passes,
            }, RemoteLink.Origin));

        // Refused before anything is made: characters the font doesn't have, a bad height.
        var refused = Engrave("Ćevapi €5");
        Assert.False(refused.Done);
        Assert.Contains("€", refused.Text);
        Assert.False(Engrave("Mahala", height: 0).Done);
        Assert.False(Directory.Exists(uploads) && Directory.EnumerateFiles(uploads).Any());

        var reply = Engrave("Mahala Đžš\nCNC", passes: 2);
        Assert.True(reply.Done, reply.Text);
        Assert.Contains("mm", reply.Text);
        Assert.StartsWith("Tekst - Mahala Đžš", Path.GetFileName(_ui.Invoke(() => _session.ProgramPath)));
        Assert.Equal((5.0, 3000, 2), (_settings.TextHeight, _settings.TextFeed, _settings.TextPasses));
        Assert.Equal(5.0, _ui.Invoke(api.BuildSnapshot).TextHeight);

        _ui.Invoke(() => _session.StartJob());
        WaitUntil(() => _session.Job?.State == JobState.Completed, tick: true);
        Assert.DoesNotContain(_log, line => line.Contains("error", StringComparison.OrdinalIgnoreCase));
        Assert.False(_session.Simulator!.LaserOn);
        // Back at the work zero at the end.
        WaitUntil(() => _session.LastStatus is { State: MachineState.Idle, WorkPosition: { } w } && Math.Abs(w.X) + Math.Abs(w.Y) + Math.Abs(w.Z) < 0.01);
        Assert.Equal(JobOutcome.Finished, _ui.Invoke(() => _session.History.Records[^1].Outcome));
    }

    /// <summary>Stands in for Windows' fonts: every character is a 0.6 × 1 square outline, except spaces.</summary>
    sealed class SquareFont : IFontOutlines
    {
        public IReadOnlyList<string> Families => ["Arial", "Times New Roman"];

        public GrblCore.Drawing.LaserDrawing? Layout(string family, string text, double capHeight)
        {
            if (!Families.Contains(family))
                return null;
            var paths = new List<IReadOnlyList<GrblCore.Drawing.PathPoint>>();
            var lines = text.Split('\n');
            for (int row = 0; row < lines.Length; row++)
                for (int i = 0; i < lines[row].Length; i++)
                    if (lines[row][i] != ' ')
                    {
                        double x = i * capHeight, y = -row * 1.5 * capHeight, w = 0.6 * capHeight;
                        paths.Add([new(x, y), new(x + w, y), new(x + w, y + capHeight), new(x, y + capHeight), new(x, y)]);
                    }
            return GrblCore.Drawing.LaserDrawing.FromPaths(paths);
        }
    }

    [Fact]
    public void EngraveText_WithAFontOfTheComputer_MakesOutlines()
    {
        var uploads = Path.Combine(Path.GetTempPath(), $"engrave-font-{Guid.NewGuid():N}");
        var api = new PageApi(_session, _ui.Post, uploads, new SquareFont());
        ConnectSimulator();
        WaitUntil(() => _session.PositionConfirmed);

        CncController.Remote.CommandReply Engrave(string text, string font) =>
            _ui.Invoke(() => api.Execute(new CncController.Remote.RemoteCommand
            {
                Name = CncController.Remote.RemoteCommands.EngraveText, Text = text, Font = font, Number = 10, Feeds = [3000], Passes = 1,
            }, RemoteLink.Origin));

        var fonts = _ui.Invoke(() => api.Execute(new CncController.Remote.RemoteCommand { Name = CncController.Remote.RemoteCommands.Fonts }, RemoteLink.Origin));
        Assert.True(fonts.Done);
        Assert.Equal("Arial\nTimes New Roman", fonts.Text);

        Assert.False(Engrave("Mahala", "Comic Sans").Done);

        // A computer's font has more characters than the single-stroke one: € is fine.
        var reply = Engrave("€ 5\nAB", "Arial");
        Assert.True(reply.Done, reply.Text);
        Assert.Equal("Arial", _settings.TextFont);
        Assert.Equal("Arial", _ui.Invoke(api.BuildSnapshot).TextFont);
        // 2 squares on the first line, 2 on the second: each burned as its own closed outline.
        var program = File.ReadAllLines(_ui.Invoke(() => _session.ProgramPath)!);
        Assert.Equal(4, program.Count(l => l.StartsWith("M3")));

        _ui.Invoke(() => _session.StartJob());
        WaitUntil(() => _session.Job?.State == JobState.Completed, tick: true);
        Assert.DoesNotContain(_log, line => line.Contains("error", StringComparison.OrdinalIgnoreCase));

        // Back to the single-stroke font: an empty font name.
        Assert.True(Engrave("ABC", "").Done);
        Assert.Equal("", _settings.TextFont);
    }

    [Fact]
    public void Fonts_WithoutTheComputersFonts_IsAnEmptyList()
    {
        var api = new PageApi(_session, _ui.Post, Path.GetTempPath());
        var reply = _ui.Invoke(() => api.Execute(new CncController.Remote.RemoteCommand { Name = CncController.Remote.RemoteCommands.Fonts }, null));
        Assert.True(reply.Done);
        Assert.Equal("", reply.Text);
    }

    [Fact]
    public void EngraveQr_MakesAFileThatRunsOnTheSimulator()
    {
        var uploads = Path.Combine(Path.GetTempPath(), $"qr-{Guid.NewGuid():N}");
        var api = new PageApi(_session, _ui.Post, uploads);
        ConnectSimulator();
        WaitUntil(() => _session.PositionConfirmed);

        CncController.Remote.CommandReply Qr(string text, double size = 10, double spacing = 1, int feed = 10000) =>
            _ui.Invoke(() => api.Execute(new CncController.Remote.RemoteCommand
            {
                Name = CncController.Remote.RemoteCommands.EngraveQr, Text = text, Number = size, Spacing = spacing, Feeds = [feed], Passes = 1,
            }, RemoteLink.Origin));

        Assert.False(Qr("   ").Done);
        Assert.False(Qr("mahalaworkshop.com", size: 1).Done);
        Assert.False(Qr("mahalaworkshop.com", spacing: 0).Done);

        var reply = Qr("https://mahalaworkshop.com/");
        Assert.True(reply.Done, reply.Text);
        Assert.Contains("10 × 10 mm", reply.Text);
        Assert.Equal("QR - mahalaworkshop.com.tap", Path.GetFileName(_ui.Invoke(() => _session.ProgramPath)));
        Assert.Equal(("https://mahalaworkshop.com/", 10.0, 1.0), (_settings.QrText, _settings.QrSize, _settings.QrSpacing));
        Assert.Equal("https://mahalaworkshop.com/", _ui.Invoke(api.BuildSnapshot).QrText);

        _ui.Invoke(() => _session.StartJob());
        WaitUntil(() => _session.Job?.State == JobState.Completed, tick: true);
        Assert.DoesNotContain(_log, line => line.Contains("error", StringComparison.OrdinalIgnoreCase));
        Assert.False(_session.Simulator!.LaserOn);
    }

    [Fact]
    public void Svg_UploadedThenConverted_RunsOnTheSimulator()
    {
        var uploads = Path.Combine(Path.GetTempPath(), $"svg-{Guid.NewGuid():N}");
        var api = new PageApi(_session, _ui.Post, uploads);
        ConnectSimulator();
        WaitUntil(() => _session.PositionConfirmed);

        // Not usable: only text. Told so, nothing loaded.
        var textOnly = _ui.Invoke(() => api.LoadUploaded(new CncController.Remote.UploadedFile("tekst.svg",
            System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><text>Mahala</text></svg>")), RemoteLink.Origin));
        Assert.False(textOnly.Done);
        Assert.Null(textOnly.Svg);
        Assert.Contains("SVG", textOnly.Text);

        var svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"40mm\" height=\"20mm\" viewBox=\"0 0 40 20\">"
            + "<rect x=\"1\" y=\"1\" width=\"38\" height=\"18\" rx=\"3\" /><circle cx=\"20\" cy=\"10\" r=\"5\" /><text>x</text></svg>";
        var upload = _ui.Invoke(() => api.LoadUploaded(new CncController.Remote.UploadedFile("pecat.svg", System.Text.Encoding.UTF8.GetBytes(svg)), RemoteLink.Origin));

        // Not loaded yet: the page asks for the size, offering the file's own.
        Assert.False(upload.Done);
        Assert.Equal("pecat.svg", upload.FileName);
        Assert.Equal(new CncController.Remote.SvgFileInfo(38, 18, 2, 1, 400, 1), upload.Svg);
        Assert.Null(_ui.Invoke(() => _session.ProgramPath));

        CncController.Remote.CommandReply Convert(double width, int feed = 3000, int passes = 1) =>
            _ui.Invoke(() => api.Execute(new CncController.Remote.RemoteCommand
            {
                Name = CncController.Remote.RemoteCommands.ConvertSvg, Text = upload.FileName, Number = width, Feeds = [feed], Passes = passes,
            }, RemoteLink.Origin));

        Assert.False(Convert(5000).Done);
        var reply = Convert(76, passes: 2);
        Assert.True(reply.Done, reply.Text);
        Assert.Contains("76 × 36 mm", reply.Text);
        Assert.Equal("pecat - laser.tap", Path.GetFileName(_ui.Invoke(() => _session.ProgramPath)));
        Assert.Equal((3000, 2), (_settings.SvgFeed, _settings.SvgPasses));

        _ui.Invoke(() => _session.StartJob());
        WaitUntil(() => _session.Job?.State == JobState.Completed, tick: true);
        Assert.DoesNotContain(_log, line => line.Contains("error", StringComparison.OrdinalIgnoreCase));
        Assert.False(_session.Simulator!.LaserOn);
    }
}
