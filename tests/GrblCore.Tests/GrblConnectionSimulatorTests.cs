using GrblCore.Simulation;

namespace GrblCore.Tests;

/// <summary><see cref="GrblConnection"/> and <see cref="GCodeJob"/> against the simulator, with real threads and timers.</summary>
public class GrblConnectionSimulatorTests : IDisposable
{
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    readonly GrblSimulator _sim = new() { TimeScale = 20 };
    readonly GrblConnection _grbl = new() { StatusIntervalMs = 50 };

    public GrblConnectionSimulatorTests() => _sim.Start();

    public void Dispose()
    {
        _grbl.Dispose();
        _sim.Dispose();
    }

    static async Task<T> Within<T>(TaskCompletionSource<T> tcs) => await tcs.Task.WaitAsync(Timeout);

    async Task ConnectAsync(bool resetOnOpen = true)
    {
        var connected = new TaskCompletionSource<GrblVersion>(TaskCreationOptions.RunContinuationsAsynchronously);
        Action<GrblVersion> handler = v => connected.TrySetResult(v);
        _grbl.Connected += handler;
        _grbl.Open(_sim.Connect(resetOnOpen));
        Assert.Equal("0.8c", (await Within(connected)).ToString());
        _grbl.Connected -= handler;
    }

    static GCodeProgram Square(int repeats) => GCodeProgram.Parse(
        ["G21 G90", "M3", .. Enumerable.Repeat<string[]>(["G1 X5 Y0 F3000", "G1 X5 Y5", "G1 X0 Y5", "G1 X0 Y0"], repeats).SelectMany(l => l), "M5", "G0 X1 Y2"]);

    [Fact]
    public async Task Connect_WithoutBanner_SoftResetsAndConnects()
    {
        // Like the real port opened without DTR: no banner until the connection sends Ctrl-X (after 2.5 s).
        await ConnectAsync(resetOnOpen: false);

        Assert.True(_grbl.IsReady);
    }

    [Fact]
    public async Task Job_RunsToCompletion()
    {
        await ConnectAsync();
        var program = Square(10);
        using var job = new GCodeJob(_grbl, program);
        var done = new TaskCompletionSource<JobState>(TaskCreationOptions.RunContinuationsAsynchronously);
        job.StateChanged += s => { if (s is JobState.Completed or JobState.Aborted or JobState.Stopped) done.TrySetResult(s); };

        job.Start();

        Assert.Equal(JobState.Completed, await Within(done));
        Assert.Equal(program.Lines.Count, job.CompletedCount);
        Assert.Equal(new Position(1, 2, 0), _sim.WorkPosition);
        Assert.False(_sim.LaserOn);
    }

    [Fact]
    public async Task RestartDuringJob_AbortsJob_AndReconnects()
    {
        await ConnectAsync();
        using var job = new GCodeJob(_grbl, Square(200));
        var aborted = new TaskCompletionSource<JobState>(TaskCreationOptions.RunContinuationsAsynchronously);
        job.StateChanged += s => { if (s != JobState.Running) aborted.TrySetResult(s); };
        var reconnected = new TaskCompletionSource<GrblVersion>(TaskCreationOptions.RunContinuationsAsynchronously);
        var progressed = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        job.ProgressChanged += n => { if (n >= 30) progressed.TrySetResult(n); };

        job.Start();
        await Within(progressed);
        _grbl.Connected += v => reconnected.TrySetResult(v);
        _sim.Restart();

        Assert.Equal(JobState.Aborted, await Within(aborted));
        Assert.Equal(JobAbortReason.GrblRestarted, job.AbortReason);
        await Within(reconnected);
        Assert.Equal(0, _grbl.PendingCount);
        Assert.InRange(job.CompletedCount, 30, job.TotalCount - 1);
        Assert.Equal(new Position(0, 0, 0), _sim.MachinePosition);
    }

    [Fact]
    public async Task LastStartRequested_OursAfterSoftReset_NotAfterRestart()
    {
        await ConnectAsync(resetOnOpen: false);
        Assert.True(_grbl.LastStartRequested);

        var restarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _grbl.Connected += _ => restarted.TrySetResult(_grbl.LastStartRequested);
        _sim.Restart();

        Assert.False(await Within(restarted));
    }

    [Fact]
    public async Task ResetDuringJob_AbortReasonReset()
    {
        await ConnectAsync();
        using var job = new GCodeJob(_grbl, Square(200));
        var aborted = new TaskCompletionSource<JobState>(TaskCreationOptions.RunContinuationsAsynchronously);
        job.StateChanged += s => { if (s != JobState.Running) aborted.TrySetResult(s); };

        job.Start();
        _grbl.SoftReset();

        Assert.Equal(JobState.Aborted, await Within(aborted));
        Assert.Equal(JobAbortReason.Reset, job.AbortReason);
    }

    [Fact]
    public async Task Break_TurnsLaserOffAfterSentLines_ThenContinues()
    {
        await ConnectAsync();
        var program = Square(20);
        using var job = new GCodeJob(_grbl, program);
        int breakAt = 30;
        job.BreakBeforeLine = index => index == breakAt;
        var states = new System.Collections.Concurrent.BlockingCollection<JobState>();
        job.StateChanged += states.Add;

        job.Start();
        Assert.Equal(JobState.Running, states.Take());
        Assert.True(states.TryTake(out var state, Timeout));
        Assert.Equal(JobState.Break, state);

        // Nothing past line 30 is sent; once the M5 is answered the machine stands still with the laser off.
        await WaitUntil(() => job.IsBreakSettled);
        Assert.Equal(breakAt, job.CompletedCount);
        Assert.Equal(breakAt, job.SentCount);
        Assert.False(_sim.LaserOn);
        Assert.Equal("Idle", _sim.State);

        breakAt = -1;
        var sentAfter = new System.Collections.Concurrent.ConcurrentQueue<string>();
        _grbl.LineSent += sentAfter.Enqueue;
        job.EndBreak();
        await WaitUntil(() => !sentAfter.IsEmpty);
        Assert.True(sentAfter.TryPeek(out var first));
        Assert.Equal("M3", first);
        await WaitUntil(() => _sim.LaserOn || job.State == JobState.Completed);
        Assert.True(states.TryTake(out state, Timeout));
        Assert.Equal(JobState.Running, state);
        Assert.True(states.TryTake(out state, Timeout));
        Assert.Equal(JobState.Completed, state);
        Assert.Equal(program.Lines.Count, job.CompletedCount);
    }

    [Fact]
    public async Task StatusInterval_ChangesWhileConnected()
    {
        await ConnectAsync();
        _grbl.StatusIntervalMs = 1000;
        int count = 0;
        _grbl.StatusReceived += _ => Interlocked.Increment(ref count);
        await Task.Delay(1500);
        int slow = count;

        _grbl.StatusIntervalMs = 50;
        await Task.Delay(1500);

        Assert.InRange(slow, 1, 3);
        Assert.True(count - slow > 10, $"{count - slow} statuses at 50 ms");
    }

    static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException();
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task Unplug_ReportsPortRemoved()
    {
        await ConnectAsync();
        var disconnected = new TaskCompletionSource<DisconnectReason>(TaskCreationOptions.RunContinuationsAsynchronously);
        _grbl.Disconnected += (reason, _) => disconnected.TrySetResult(reason);

        _sim.Unplug();

        Assert.Equal(DisconnectReason.PortRemoved, await Within(disconnected));
        Assert.False(_grbl.IsOpen);
        Assert.False(_sim.IsConnected);
    }

    [Fact]
    public async Task Close_LeavesMachineRunning_Reconnect()
    {
        await ConnectAsync();
        _grbl.SendLine("G1 X50 F600");
        _grbl.Close();
        Assert.False(_sim.IsConnected);

        await ConnectAsync(resetOnOpen: false);

        Assert.NotEqual(0, _sim.MachinePosition.X);
    }
}
