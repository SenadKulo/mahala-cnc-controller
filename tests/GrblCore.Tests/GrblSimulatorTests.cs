using System.Text;
using GrblCore.Simulation;

namespace GrblCore.Tests;

public class GrblSimulatorTests : IDisposable
{
    readonly GrblSimulator _sim = new();
    readonly SimulatorPort _port;

    public GrblSimulatorTests()
    {
        _port = _sim.Connect();
        _port.ReadTimeout = 0;
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

    GrblStatus Status()
    {
        Read(); // earlier answers
        Send("?");
        var line = Assert.Single(Read());
        Assert.True(StatusParser.TryParse(line, out var status), line);
        return status;
    }

    static void AssertNear(double expected, double actual, double tolerance = 0.01) =>
        Assert.InRange(actual, expected - tolerance, expected + tolerance);

    [Fact]
    public void Connecting_DoesNotRestartTheBoard_SoftResetPrintsBanner()
    {
        Advance(5);
        Assert.Empty(Read());

        Send("\x18");

        Assert.Equal(["", GrblSimulator.Banner], Read());
        Assert.True(GrblVersion.TryParseBanner(GrblSimulator.Banner, out var version));
        Assert.Equal("0.8c", version.ToString());
    }

    [Fact]
    public void ResetOnOpen_BannerAfterBoot_InputIgnoredWhileBooting()
    {
        _port.Dispose();
        var port = _sim.Connect(resetOnOpen: true);
        port.ReadTimeout = 0;

        port.Write(Encoding.ASCII.GetBytes("?G1 X10\n"));
        Advance(0.5);
        Assert.Null(port.ReadLine());

        Advance(0.6);
        Assert.Equal("", port.ReadLine());
        Assert.Equal(GrblSimulator.Banner, port.ReadLine());
        Assert.Null(port.ReadLine());
    }

    [Fact]
    public void Status_HasGrbl08cFormat()
    {
        SendLine("G10 L2 P1 X1 Y2 Z3");
        Assert.Equal(["ok"], Read());

        Send("?");

        Assert.Equal(["<Idle,MPos:0.000,0.000,0.000,WPos:-1.000,-2.000,-3.000>"], Read());
    }

    [Fact]
    public void Move_RunsAtFeedRate_OkComesWhenPlanned()
    {
        SendLine("G1 X10 F600");
        Assert.Equal(["ok"], Read());

        Advance(0.5);
        var moving = Status();
        Assert.Equal(MachineState.Run, moving.State);
        Assert.Equal("Cycle", moving.RawState);
        AssertNear(5, moving.MachinePosition!.Value.X);

        Advance(1);
        var done = Status();
        Assert.Equal(MachineState.Idle, done.State);
        Assert.Equal(new Position(10, 0, 0), done.MachinePosition);
    }

    [Fact]
    public void RapidMove_UsesSeekRateSetting()
    {
        SendLine("G0 X10");
        Advance(0.6); // $5 = 500 mm/min = 8.33 mm/s

        AssertNear(5, Status().MachinePosition!.Value.X);
    }

    [Fact]
    public void FullPlanner_DelaysOk()
    {
        for (int i = 1; i <= 20; i++)
            SendLine($"G1 X{i} F60");

        Assert.Equal(GrblSimulator.PlannerSize, Read().Count(l => l == "ok"));

        Advance(1.01); // the first 1 mm move is done, one block is free
        Assert.Equal(["ok"], Read());
    }

    [Fact]
    public void FeedHold_BrakesAndWaits_CycleStartContinues()
    {
        SendLine("G1 X100 F600");
        Advance(1);
        Send("!");
        Read();
        Assert.Equal(MachineState.Hold, Status().State);

        Advance(2);
        var held = Status();
        // 10 mm/s braking at $8 = 20 mm/s² takes 0.5 s and 2.5 mm.
        Assert.Equal(MachineState.Queue, held.State);
        AssertNear(12.5, held.MachinePosition!.Value.X);

        Send("~");
        Advance(1);
        AssertNear(22.5, Status().MachinePosition!.Value.X);
    }

    [Fact]
    public void LaserOff_WaitsForMovesBeforeIt()
    {
        SendLine("M3");
        SendLine("G1 X10 F600");
        Assert.Equal(["ok", "ok"], Read());
        Assert.True(_sim.LaserOn);

        SendLine("M5");
        Assert.Empty(Read());
        Assert.True(_sim.LaserOn);

        Advance(2);
        Assert.Equal(["ok"], Read());
        Assert.False(_sim.LaserOn);
    }

    [Fact]
    public void Dwell_AnswersAfterTheWait()
    {
        SendLine("G4 P2");
        Advance(1);
        Assert.Empty(Read());

        Advance(1.5);
        Assert.Equal(["ok"], Read());
    }

    [Fact]
    public void SoftReset_KeepsMachinePositionAndG54_ClearsG92()
    {
        SendLine("G0 X10");
        Advance(5);
        SendLine("G10 L2 P1 X5 Y0 Z0");
        SendLine("G92 X1");
        Read();
        Assert.Equal(new Position(1, 0, 0), Status().WorkPosition);

        Send("\x18");
        Read();

        var status = Status();
        Assert.Equal(new Position(10, 0, 0), status.MachinePosition);
        Assert.Equal(new Position(5, 0, 0), status.WorkPosition);
    }

    [Fact]
    public void SoftResetWhileMoving_Alarm_UntilUnlocked()
    {
        SendLine("G1 X100 F600");
        Advance(1);
        Read();

        Send("\x18");
        Assert.Equal(["ALARM: Abort during cycle", "", GrblSimulator.Banner, "['$H'|'$X' to unlock]"], Read());
        Assert.Equal(MachineState.Alarm, Status().State);

        SendLine("G0 X0");
        Assert.Equal(["error: Alarm lock"], Read());

        SendLine("$X");
        Assert.Equal(["[Caution: Unlocked]", "ok"], Read());
        Assert.Equal(MachineState.Idle, Status().State);
    }

    [Fact]
    public void Restart_LosesMachinePosition_KeepsG54_TurnsLaserOff()
    {
        SendLine("G10 L2 P1 X5 Y0 Z0");
        SendLine("M3");
        SendLine("G1 X20 F600");
        Advance(1);
        Read();

        _sim.Restart();
        Advance(1.1);

        Assert.Equal(["", GrblSimulator.Banner], Read());
        var status = Status();
        Assert.Equal(MachineState.Idle, status.State);
        Assert.Equal(new Position(0, 0, 0), status.MachinePosition);
        Assert.Equal(new Position(-5, 0, 0), status.WorkPosition);
        Assert.False(_sim.LaserOn);
        Assert.Equal(1, _sim.Restarts);
    }

    [Fact]
    public void RestartInterval_RestartsOnSchedule()
    {
        _sim.RestartInterval = TimeSpan.FromMinutes(15);

        for (int i = 0; i < 14; i++)
            Advance(60);
        Assert.Empty(Read());

        Advance(60);
        Advance(1.1);
        Assert.Equal(["", GrblSimulator.Banner], Read());
        Assert.Equal(1, _sim.Restarts);
        Assert.Equal(TimeSpan.FromMinutes(15), _sim.NextRestartIn!.Value + TimeSpan.FromSeconds(1.1));
    }

    [Fact]
    public void Settings_DumpParses_StoreValidates()
    {
        SendLine("$$");
        var lines = Read();
        Assert.Equal("ok", lines[^1]);
        var settings = lines[..^1].Select(l => Assert.IsType<GrblSetting>(GrblSetting.TryParse(l, out var s) ? s : null)).ToList();
        Assert.Equal(23, settings.Count);
        Assert.Equal("500.000", settings.Single(s => s.Number == 5).Value);
        Assert.Equal("default seek, mm/min", settings.Single(s => s.Number == 5).Description);
        Assert.Equal("step port invert mask, int:11000000", settings.Single(s => s.Number == 6).Description);

        SendLine("$5=600");
        Assert.Equal(["ok"], Read());
        SendLine("$3=1");
        Assert.Equal(["error: Value < 3 usec"], Read());
        SendLine("$99=1");
        Assert.Equal(["error: Invalid statement"], Read());

        Send("\x18");
        Read();
        SendLine("$$");
        Assert.Contains("$5=600.000 (default seek, mm/min)", Read());
    }

    [Fact]
    public void Parameters_ShowOffsets()
    {
        SendLine("G10 L2 P1 X1.5");
        SendLine("$#");

        var lines = Read();
        Assert.Contains("[G54:1.500,0.000,0.000]", lines);
        Assert.Contains("[G92:0.000,0.000,0.000]", lines);
        Assert.Equal("ok", lines[^1]);
    }

    [Theory]
    [InlineData("G49", "error: Unsupported statement")]
    [InlineData("M6", "error: Unsupported statement")]
    [InlineData("G1 X", "error: Bad number format")]
    [InlineData("1X", "error: Expected command letter")]
    [InlineData("$Q", "error: Bad number format")]
    [InlineData("G1 X1000.0000 Y2000.0000 Z3000.0000 F1000.0000 I1000.0000", "error: Line overflow")] // 52 without spaces
    public void BadLines_AreRejected(string line, string error)
    {
        SendLine(line);

        Assert.Equal([error], Read());
    }

    [Fact]
    public void CommentsSpacesAndLowercase_AreIgnored()
    {
        SendLine("g0 (to the right) x 3 ; rest");
        Advance(5);

        Assert.Equal(["ok"], Read());
        Assert.Equal(new Position(3, 0, 0), Status().MachinePosition);
    }

    [Fact]
    public void Arc_FollowsTheCircle()
    {
        // Clockwise half circle around (5,0), over the top: 15.7 mm at 100 mm/s.
        SendLine("G2 X10 Y0 I5 J0 F6000");
        Advance(Math.PI * 5 / 2 / 100);

        var half = Status().MachinePosition!.Value;
        AssertNear(5, half.X, 0.1);
        AssertNear(5, half.Y, 0.1);

        Advance(1);
        Assert.Equal(["ok"], Read().Where(l => l == "ok"));
        Assert.Equal(new Position(10, 0, 0), Status().MachinePosition);
    }

    [Fact]
    public void RelativeAndInches()
    {
        SendLine("G91 G0 X1");
        SendLine("G0 X1");
        SendLine("G90 G20 G0 Y1");
        Advance(5);

        Assert.Equal(new Position(2, 25.4, 0), Status().MachinePosition);
    }

    [Fact]
    public void CorruptStatus_LosesCharacters_OnlyOnce()
    {
        _sim.CorruptNextStatus();

        Send("?");
        var corrupt = Assert.Single(Read());
        Send("?");
        var good = Assert.Single(Read());

        Assert.Equal(good.Length - 6, corrupt.Length);
        Assert.True(StatusParser.TryParse(good, out _));
    }

    [Fact]
    public void Unplug_PortDisappears_ReconnectedBoardLostPosition()
    {
        SendLine("G0 X10");
        Advance(5);

        _sim.Unplug();

        Assert.False(_port.IsPresent);
        Assert.Throws<IOException>(() => SendLine("?"));

        _port.Dispose();
        var port = _sim.Connect();
        Assert.Equal(new Position(0, 0, 0), _sim.MachinePosition);
        port.ReadTimeout = 0;
        Assert.Null(port.ReadLine());
    }

    [Fact]
    public void SecondConnection_IsRefused()
    {
        Assert.Throws<InvalidOperationException>(() => _sim.Connect());
    }
}
