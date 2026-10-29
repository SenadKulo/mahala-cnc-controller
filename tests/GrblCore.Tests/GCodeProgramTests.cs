using GrblCore;

namespace GrblCore.Tests;

public class GCodeProgramTests
{
    [Theory]
    [InlineData("G0 X10 (move) Y5", "G0 X10 Y5")]
    [InlineData("G1 X1 ; cut", "G1 X1")]
    [InlineData("N120 g1 x1.5", "G1 X1.5")]
    [InlineData("%", "")]
    [InlineData("  G0   Z5  ", "G0 Z5")]
    [InlineData("(only a comment)", "")]
    public void Cleans_lines(string input, string expected) => Assert.Equal(expected, GCodeProgram.Clean(input));

    [Fact]
    public void Skips_empty_lines_and_keeps_source_line_numbers()
    {
        var p = GCodeProgram.Parse(["(header)", "G21", "", "G0 X1"]);

        Assert.Equal([new GCodeLine(2, "G21"), new GCodeLine(4, "G0 X1")], p.Lines);
        Assert.Empty(p.Issues);
        Assert.True(p.CanRun);
    }

    [Theory]
    [InlineData("M30")]
    [InlineData("M2")]
    [InlineData("M02")]
    public void Removes_program_end(string line)
    {
        var p = GCodeProgram.Parse(["G0 X1", line]);

        Assert.Single(p.Lines);
        Assert.Equal(GCodeIssueKind.ProgramEndRemoved, Assert.Single(p.Issues).Kind);
    }

    [Theory]
    [InlineData("M3")]
    [InlineData("G0 X2.30")]
    public void Keeps_lines_that_only_look_like_program_end(string line)
    {
        var p = GCodeProgram.Parse([line]);

        Assert.Single(p.Lines);
        Assert.Empty(p.Issues);
    }

    [Fact]
    public void M300_is_unsupported_not_program_end()
    {
        var p = GCodeProgram.Parse(["M300"]);

        Assert.Equal(GCodeIssueKind.UnsupportedRemoved, Assert.Single(p.Issues).Kind);
    }

    [Fact]
    public void Removes_program_end_but_keeps_rest_of_line()
    {
        var p = GCodeProgram.Parse(["M5 M30"]);

        Assert.Equal("M5", Assert.Single(p.Lines).Text);
    }

    [Fact]
    public void Removes_unsupported_words_from_artcam_header()
    {
        var p = GCodeProgram.Parse(["G90G80G21G49"]);

        Assert.Equal("G90G80G21", Assert.Single(p.Lines).Text);
        var issue = Assert.Single(p.Issues);
        Assert.Equal(GCodeIssueKind.UnsupportedRemoved, issue.Kind);
        Assert.Equal("G49", issue.Detail);
    }

    [Theory]
    [InlineData("T1 M6", "")]
    [InlineData("G0 X1 Y2 T1", "G0 X1 Y2")]
    [InlineData("G43 H1 Z5", "")]
    [InlineData("G0 G43 H1 Z5", "G0")]
    [InlineData("G64 G1 X1", "G1 X1")]
    public void Removes_unsupported_words(string input, string expected)
    {
        var p = GCodeProgram.Parse([input]);

        Assert.Equal(expected, p.Lines.SingleOrDefault()?.Text ?? "");
        Assert.Equal(GCodeIssueKind.UnsupportedRemoved, Assert.Single(p.Issues).Kind);
    }

    [Theory]
    [InlineData("G2 X1 Y2 I0.5 J-0.5 F400")]
    [InlineData("G4 P0.5")]
    [InlineData("M3 S15000")]
    [InlineData("G28.1")]
    [InlineData("X53.2274 Y43.0539")]
    public void Keeps_supported_lines_untouched(string line)
    {
        var p = GCodeProgram.Parse([line]);

        Assert.Equal(line, Assert.Single(p.Lines).Text);
        Assert.Empty(p.Issues);
    }

    [Fact]
    public void Shortens_long_line_by_rounding_decimals()
    {
        // 51 non-space characters - over the 0.8 limit of 50.
        var p = GCodeProgram.Parse(["G1 X10.12345678 Y20.12345678 Z-1.12345678 F100.00000000"]);

        Assert.Equal("G1 X10.123 Y20.123 Z-1.123 F100", Assert.Single(p.Lines).Text);
        Assert.Equal(GCodeIssueKind.Shortened, Assert.Single(p.Issues).Kind);
        Assert.True(p.CanRun);
    }

    [Fact]
    public void Spaces_do_not_count_towards_line_limit()
    {
        var p = GCodeProgram.Parse(["G1 X1 Y1 Z1 F100"], maxLineLength: 12);

        Assert.Equal("G1 X1 Y1 Z1 F100", Assert.Single(p.Lines).Text);
        Assert.Empty(p.Issues);
    }

    [Fact]
    public void Reports_line_that_stays_too_long()
    {
        var p = GCodeProgram.Parse(["G1 X100 Y200 Z300"], maxLineLength: 10);

        var issue = Assert.Single(p.Issues);
        Assert.Equal(GCodeIssueKind.TooLong, issue.Kind);
        Assert.Equal(1, issue.SourceLineNumber);
        Assert.False(p.CanRun);
    }
}
