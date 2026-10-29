using GrblCore;

namespace GrblCore.Tests;

public class JobResumeTests
{
    static readonly Position Origin = new(0, 0, 0);

    static GCodeProgram Program(params string[] lines) => GCodeProgram.Parse(lines);

    static ResumePlan Plan(GCodeProgram program, int fromIndex, Position? currentWork = null)
    {
        Assert.True(JobResume.TryPlan(program, fromIndex, currentWork ?? Origin, out var plan, out var problem), problem?.ToString());
        return plan;
    }

    [Fact]
    public void Goes_to_the_end_of_the_previous_line_and_restores_feed_and_laser()
    {
        var program = Program("G90 G21", "G0 X10 Y10", "M3", "G1 X20 Y10 F400", "X20 Y20", "X10 Y20");

        var plan = Plan(program, 4);

        Assert.Equal(4, plan.StartIndex);
        Assert.Equal(new Position(20, 10, 0), plan.Start);
        Assert.Equal(["M5", "G21 G90", "G0 X20 Y10", "G17 G21 G90 G94 F400", "M3"], plan.Preamble);
        // The preamble leaves Grbl in G0, so the modal G1 is written out.
        Assert.Equal("G1 X20 Y20", plan.FirstLine);
    }

    [Fact]
    public void Line_with_its_own_motion_word_is_sent_unchanged()
    {
        var plan = Plan(Program("G1 X5 Y5 F300", "G2X10Y0I5J0"), 1);
        Assert.Equal("G2X10Y0I5J0", plan.FirstLine);
    }

    [Fact]
    public void Modal_arc_gets_its_motion_word_without_spaces_in_a_compact_line()
    {
        var plan = Plan(Program("G2 X10 Y0 I5 J0 F300", "X0Y0I-5J0"), 1);
        Assert.Equal("G2X0Y0I-5J0", plan.FirstLine);
    }

    [Fact]
    public void Lines_without_moves_are_skipped_and_their_modes_replayed()
    {
        // Starting at "M5" or "F200" would leave the next "X.." running in G0.
        var program = Program("G1 X5 Y0 F400", "M3", "X10", "M5", "F200", "X15");

        var plan = Plan(program, 3);

        Assert.Equal(5, plan.StartIndex);
        Assert.Equal(["M5", "G21 G90", "G0 X10 Y0", "G17 G21 G90 G94 F200"], plan.Preamble);
        Assert.Equal("G1 X15", plan.FirstLine);
    }

    [Fact]
    public void Relative_and_inch_programs_are_positioned_in_mm_then_switched_back()
    {
        var program = Program("G20 G91", "G1 X1 Y1 F10", "X1", "X1");

        var plan = Plan(program, 3);

        Assert.Equal(new Position(50.8, 25.4, 0), plan.Start);
        Assert.Equal(["M5", "G21 G90", "G0 X50.8 Y25.4", "G17 G20 G91 G94 F10"], plan.Preamble);
    }

    [Fact]
    public void Z_is_left_alone_unless_the_program_moves_it()
    {
        var plan = Plan(Program("G1 X5 F100", "X10"), 1, currentWork: new(0, 0, 3));
        Assert.Equal(3, plan.Start.Z);
        Assert.DoesNotContain(plan.Preamble, l => l.Contains('Z'));
    }

    [Fact]
    public void Z_goes_up_before_and_down_after_the_travel()
    {
        var program = Program("G0 Z5", "G1 X5 Z-1 F100", "X10");

        Assert.Equal(["M5", "G21 G90", "G0 X5 Y0", "G0 Z-1"], Plan(program, 2, currentWork: new(0, 0, 0)).Preamble.Take(4));
        Assert.Equal(["M5", "G21 G90", "G0 Z-1", "G0 X5 Y0"], Plan(program, 2, currentWork: new(0, 0, -3)).Preamble.Take(4));
    }

    [Fact]
    public void Coordinate_system_changes_before_the_start_are_refused()
    {
        var program = Program("G1 X5 F100", "G92 X0 Y0", "X10");

        Assert.False(JobResume.TryPlan(program, 2, Origin, out _, out var problem));
        Assert.Equal(ResumeProblemKind.UnsupportedCommand, problem.Kind);
        Assert.Equal(2, problem.Line!.SourceLineNumber);
    }

    [Fact]
    public void Restores_the_laser_power_the_program_set()
    {
        // Grbl 1.1 has S0 after a reset, so "M3" alone would continue with the laser dark.
        var program = Program("M3 S800", "G1 X10 F400", "X20", "S300", "X30");

        Assert.Equal("M3 S800", Plan(program, 2).Preamble[^1]);
        Assert.Equal("M3 S300", Plan(program, 4).Preamble[^1]);
    }

    [Fact]
    public void Nothing_to_resume_after_the_last_move()
    {
        Assert.False(JobResume.TryPlan(Program("G1 X5 F100", "M5"), 1, Origin, out _, out var problem));
        Assert.Equal(ResumeProblemKind.NothingLeft, problem.Kind);
    }

    [Fact]
    public void Suggests_the_line_where_the_laser_was_last_seen()
    {
        // A square traced line by line; Grbl accepted all four lines, the laser was seen halfway up the right side.
        var program = Program("G1 X10 Y0 F400", "X10 Y10", "X0 Y10", "X0 Y0");
        var toolpath = Toolpath.Build(program);

        Assert.Equal(1, JobResume.SuggestStart(toolpath, lastCompletedIndex: 3, stoppedAt: new(10, 5, 0)));
    }

    [Fact]
    public void Suggests_the_earliest_pass_when_the_path_repeats()
    {
        // Two passes over the same line: going back to the first one may burn twice but never skips.
        var program = Program("G1 X10 F400", "X0", "X10", "X0");
        var toolpath = Toolpath.Build(program);

        Assert.Equal(0, JobResume.SuggestStart(toolpath, 3, new(5, 0, 0)));
    }

    [Fact]
    public void Without_a_position_goes_back_as_far_as_grbl_may_have_buffered()
    {
        var program = GCodeProgram.Parse(Enumerable.Range(1, 100).Select(i => $"G1 X{i} F400"));
        var toolpath = Toolpath.Build(program);

        Assert.Equal(90 - JobResume.SearchLines, JobResume.SuggestStart(toolpath, 90, null));
        Assert.Equal(0, JobResume.SuggestStart(toolpath, 10, null));
    }
}
