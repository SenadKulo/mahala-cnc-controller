using GrblCore.Drawing;

namespace GrblCore.Tests;

public class TextEngravingTests
{
    [Fact]
    public void Capital_IsAsHighAsAsked_WithItsCornerAtZero()
    {
        var drawing = TextEngraving.Layout("E", 10);

        // E: stem, top, middle and bottom bars.
        Assert.Equal(4, drawing.Paths.Count);
        Assert.Equal(10, drawing.Height, 6);
        Assert.Equal(13 * 10 / 21.0, drawing.Width, 6);
        var points = drawing.Paths.SelectMany(p => p).ToList();
        Assert.Equal(0, points.Min(p => p.X), 6);
        Assert.Equal(0, points.Min(p => p.Y), 6);
    }

    [Fact]
    public void Lines_GoDown_AndSpacesMoveOn()
    {
        var one = TextEngraving.Layout("I I", 21);
        // I is 8 wide with its stem at 4, a space 16: the stems are 24 apart.
        Assert.Equal(24, one.Width, 6);
        var two = TextEngraving.Layout("I\nI", 21);
        Assert.Equal(0, two.Width, 6);
        Assert.Equal(36 + 21, two.Height, 6);
        Assert.Equal(two.Height, TextEngraving.Layout("I\r\nI", 21).Height, 6);
    }

    [Fact]
    public void BosnianLetters_HaveTheirMarks()
    {
        Assert.Equal("", TextEngraving.Unsupported("ČĆŠŽĐ čćšžđ"));
        // One stroke more than the base letter, above it (or across it for đ).
        Assert.Equal(TextEngraving.Layout("C", 21).Paths.Count + 1, TextEngraving.Layout("Č", 21).Paths.Count);
        Assert.True(TextEngraving.Layout("Č", 21).Height > TextEngraving.Layout("C", 21).Height);
        Assert.Equal(TextEngraving.Layout("d", 21).Paths.Count + 1, TextEngraving.Layout("đ", 21).Paths.Count);
        Assert.Equal(TextEngraving.Layout("d", 21).Height, TextEngraving.Layout("đ", 21).Height, 6);
    }

    [Fact]
    public void UnknownCharacters_AreListedOnce()
    {
        Assert.Equal("€ü", TextEngraving.Unsupported("€ 5, € 6, für"));
        Assert.Throws<ArgumentException>(() => TextEngraving.Layout("5 €", 10));
    }

    [Fact]
    public void GCode_BurnsEachStroke_LaserOffBetween()
    {
        var drawing = new LaserDrawing([
            [new(0, 0), new(10, 0), new(10, 5)],
            [new(10, 5), new(0, 5)], // continues where the last one ended: no M5/M3 between
            [new(20, 0), new(20, 0)], // a single point: nothing to burn
            [new(20, 0), new(30, 0.12345)],
        ], 30, 5);

        var lines = LaserGCode.FromDrawing(drawing, 400);

        Assert.Equal([
            "G21", "G90", "M5", "F400",
            "G0 X0 Y0", "M3", "G1 X10 Y0", "G1 X10 Y5", "G1 X0 Y5",
            "M5", "G0 X20 Y0", "M3", "G1 X30 Y0.123",
            "M5", "G0 X0 Y0", "G0 Z0",
        ], lines);
    }

    [Fact]
    public void GCode_RepeatsForPasses_AndFitsGrbl08()
    {
        var drawing = TextEngraving.Layout("Mahala Workshop\nŠtiklić 123", 37.5);
        var once = LaserGCode.FromDrawing(drawing, 300);
        var twice = LaserGCode.FromDrawing(drawing, 300, passes: 2);

        Assert.Equal(once.Count * 2 - 6, twice.Count); // the header and the way back once
        Assert.Equal(["M5", "G0 X0 Y0", "G0 Z0"], twice[^3..]);
        var program = GCodeProgram.Parse(twice);
        Assert.True(program.CanRun);
        Assert.Empty(program.Issues);
        Assert.Equal(once.Count(l => l == "M3") * 2, twice.Count(l => l == "M3"));
        Assert.Equal(twice.Count(l => l == "M3"), twice.Count(l => l == "M5") - 1);
    }
}
