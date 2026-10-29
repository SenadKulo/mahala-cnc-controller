using System.Text;
using GrblCore.Simulation;

namespace GrblCore.Tests;

/// <summary>The simulator playing Grbl 1.1: what other boards send, as the program sees it.</summary>
public class Grbl11SimulatorTests : IDisposable
{
    readonly GrblSimulator _sim = new(SimulatedFirmware.Grbl11);
    readonly SimulatorPort _port;

    public Grbl11SimulatorTests()
    {
        _port = _sim.Connect();
        _port.ReadTimeout = 0;
        Send("\x18");
        Read();
    }

    public void Dispose() => _sim.Dispose();

    void Send(string text) => _port.Write(Encoding.ASCII.GetBytes(text));

    void SendLine(string line) => Send(line + "\n");

    void Advance(double seconds) => _sim.Advance(TimeSpan.FromSeconds(seconds));

    List<string> Read()
    {
        var lines = new List<string>();
        while (_port.ReadLine() is { } line)
            lines.Add(line);
        return lines;
    }

    string StatusLine()
    {
        Read();
        Send("?");
        return Assert.Single(Read());
    }

    [Fact]
    public void Banner_Is11()
    {
        Send("\x18");

        Assert.Equal(["", GrblSimulator.Banner11], Read());
        Assert.True(GrblVersion.TryParseBanner(GrblSimulator.Banner11, out var version));
        Assert.True(version.UsesSpindleSpeed);
    }

    [Fact]
    public void Status_SendsMachinePosition_AndTheOffsetOnlyNowAndThen()
    {
        SendLine("G10 L2 P1 X5 Y6");
        var first = StatusLine();
        var second = StatusLine();

        Assert.Equal("<Idle|MPos:0.000,0.000,0.000|FS:0,0|WCO:5.000,6.000,0.000>", first);
        Assert.Equal("<Idle|MPos:0.000,0.000,0.000|FS:0,0>", second);
    }

    [Fact]
    public void Errors_AreNumbers_FeedIsRequired()
    {
        SendLine("G1 X10");
        SendLine("G5");

        Assert.Equal(["error:22", "error:20"], Read());
    }

    [Fact]
    public void Laser_NeedsAPower()
    {
        SendLine("M3");
        Advance(0.1);
        Assert.False(_sim.LaserOn);

        SendLine("M3 S300");
        Advance(0.1);
        Assert.True(_sim.LaserOn);
        Assert.Equal(300, _sim.LaserPower);

        Send("\x18");
        Assert.False(_sim.LaserOn);
        Assert.Equal(0, _sim.LaserPower);
    }

    [Fact]
    public void ResetWhileMoving_NumberedAlarm()
    {
        SendLine("G1 X50 F600");
        Advance(1);
        Read();

        Send("\x18");

        Assert.Equal(["ALARM:3", "", GrblSimulator.Banner11, "[MSG:'$H'|'$X' to unlock]"], Read());
        Assert.StartsWith("<Alarm|", StatusLine());
    }

    [Fact]
    public void FeedHold_ReportsHoldUntilResumed()
    {
        SendLine("G1 X50 F600");
        Advance(1);

        Send("!");
        Advance(2);
        Assert.StartsWith("<Hold:0|", StatusLine());

        Send("~");
        Advance(0.5);
        Assert.StartsWith("<Run|", StatusLine());
    }

    [Fact]
    public void Settings_HaveNoDescriptions()
    {
        SendLine("$$");

        var lines = Read();
        Assert.Contains("$32=1", lines);
        Assert.Contains("$110=500.000", lines);
        Assert.Equal("ok", lines[^1]);
    }
}

/// <summary><see cref="GrblConnection"/> and <see cref="GCodeJob"/> against a simulated Grbl 1.1.</summary>
public class GrblConnection11Tests : IDisposable
{
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    readonly GrblSimulator _sim = new(SimulatedFirmware.Grbl11) { TimeScale = 20 };
    readonly GrblConnection _grbl = new() { StatusIntervalMs = 50 };

    public GrblConnection11Tests() => _sim.Start();

    public void Dispose()
    {
        _grbl.Dispose();
        _sim.Dispose();
    }

    async Task ConnectAsync()
    {
        var connected = new TaskCompletionSource<GrblVersion>(TaskCreationOptions.RunContinuationsAsynchronously);
        _grbl.Connected += v => connected.TrySetResult(v);
        _grbl.Open(_sim.Connect(resetOnOpen: true));
        Assert.Equal("1.1h", (await connected.Task.WaitAsync(Timeout)).ToString());
    }

    [Fact]
    public async Task EveryStatus_HasBothPositions_ThoughWcoComesRarely()
    {
        await ConnectAsync();
        _grbl.SendLine("G10 L2 P1 X5 Y6");
        var statuses = new List<GrblStatus>();
        var enough = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _grbl.StatusReceived += s =>
        {
            lock (statuses)
            {
                statuses.Add(s);
                if (statuses.Count >= 15)
                    enough.TrySetResult();
            }
        };

        await enough.Task.WaitAsync(Timeout);

        lock (statuses)
        {
            Assert.Contains(statuses, s => s.WorkOffset is null);
            Assert.All(statuses, s => Assert.NotNull(s.WorkPosition));
            Assert.Equal(new Position(-5, -6, 0), statuses[^1].WorkPosition);
        }
    }

    [Fact]
    public async Task Job_SetupLineGivesM3ItsPower()
    {
        await ConnectAsync();
        var program = GCodeProgram.Parse(["G21 G90", "M3", "G1 X20 F600", "G1 X0"]);
        using var job = new GCodeJob(_grbl, program, setup: ["S500"]);
        var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        job.ProgressChanged += n => { if (n >= 3) running.TrySetResult(); };

        job.Start();
        await running.Task.WaitAsync(Timeout);

        Assert.Equal(500, _sim.LaserPower);
        Assert.True(_sim.LaserOn);
    }
}
