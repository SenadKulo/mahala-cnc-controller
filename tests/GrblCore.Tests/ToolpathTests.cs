using GrblCore;

namespace GrblCore.Tests;

public class ToolpathTests
{
    static Toolpath Build(params string[] lines) => Toolpath.Build(GCodeProgram.Parse(lines));

    [Fact]
    public void Lines_start_at_origin_and_follow_laser_state()
    {
        var t = Build("G0 X10 Y0", "M3", "G1 X10 Y5", "M5", "G1 X0 Y5");

        Assert.Equal(
        [
            new ToolpathSegment(0, SegmentKind.Rapid, 0, 0, 10, 0),
            new ToolpathSegment(2, SegmentKind.Cut, 10, 0, 10, 5),
            new ToolpathSegment(4, SegmentKind.Move, 10, 5, 0, 5),
        ], t.Segments);
    }

    [Fact]
    public void Modal_motion_and_missing_axis_keep_previous_values()
    {
        var t = Build("M3", "G1 X5", "Y5", "X0");

        Assert.Equal([(5.0, 0.0), (5.0, 5.0), (0.0, 5.0)], t.Segments.Select(s => (s.X2, s.Y2)));
        Assert.All(t.Segments, s => Assert.Equal(SegmentKind.Cut, s.Kind));
    }

    [Fact]
    public void Quarter_arc_with_ij_stays_on_circle_and_ends_exactly()
    {
        // Counter-clockwise quarter circle around 0,0 from 10,0 to 0,10.
        var t = Build("G0 X10 Y0", "M3", "G3 X0 Y10 I-10 J0");

        var arc = t.Segments.Where(s => s.LineIndex == 2).ToList();
        Assert.True(arc.Count > 10);
        Assert.All(arc, s => Assert.Equal(10, Math.Sqrt(s.X2 * s.X2 + s.Y2 * s.Y2), 6));
        Assert.All(arc, s => Assert.True(s.X2 >= -1e-9 && s.Y2 >= -1e-9)); // stays in the first quadrant
        Assert.Equal((0.0, 10.0), (arc[^1].X2, arc[^1].Y2));
    }

    [Fact]
    public void Clockwise_arc_goes_the_other_way()
    {
        // Same endpoints, clockwise: the long way round through the other three quadrants.
        var t = Build("G0 X10 Y0", "G2 X0 Y10 I-10 J0");

        Assert.Contains(t.Segments, s => s.Y2 < -9);
    }

    [Fact]
    public void Full_circle_when_start_equals_end()
    {
        var t = Build("G0 X10 Y0", "M3", "G2 X10 Y0 I-10 J0");

        // Arcs are drawn as chords at most 0.01 mm inside the circle.
        var b = t.CutBounds!.Value;
        Assert.Equal(-10, b.MinX, 0.011);
        Assert.Equal(10, b.MaxX, 0.011);
        Assert.Equal(-10, b.MinY, 0.011);
        Assert.Equal(10, b.MaxY, 0.011);
    }

    [Fact]
    public void Arc_with_radius_matches_ij_arc()
    {
        var withR = Build("G0 X10 Y0", "G3 X0 Y10 R10");
        var withIj = Build("G0 X10 Y0", "G3 X0 Y10 I-10 J0");

        Assert.Equal(withIj.Segments.Count, withR.Segments.Count);
        for (int k = 0; k < withIj.Segments.Count; k++)
        {
            Assert.Equal(withIj.Segments[k].X2, withR.Segments[k].X2, 6);
            Assert.Equal(withIj.Segments[k].Y2, withR.Segments[k].Y2, 6);
        }
    }

    [Fact]
    public void Relative_inch_and_g92()
    {
        var t = Build("G91", "G0 X1 Y1", "G0 X1", "G90 G20", "G0 X1 Y0", "G21", "G92 X0 Y0", "G0 X5");

        Assert.Equal(
        [(1.0, 1.0), (2.0, 1.0), (25.4, 0.0), (5.0, 0.0)],
        t.Segments.Select(s => (s.X2, s.Y2)));
    }

    [Fact]
    public void Cut_bounds_ignore_rapids()
    {
        var t = Build("G0 X100 Y100", "M3", "G1 X110 Y100", "G1 X110 Y120", "M5", "G0 X0 Y0");

        Assert.Equal(new Bounds(100, 100, 110, 120), t.CutBounds);
        Assert.Equal(new Bounds(0, 0, 110, 120), t.AllBounds);
    }

    [Fact]
    public void Dwell_and_home_lines_are_not_moves()
    {
        var t = Build("G4 P0.5", "G28 X0 Y0", "G0 X1 Y1");

        Assert.Single(t.Segments);
    }
}
