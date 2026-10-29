using GrblCore;

namespace GrblCore.Tests;

public class ArtCamLaserConverterTests
{
    // Shortened from "test 10252026.TAP": one toolpath, two depth passes over one contour.
    static readonly string[] SingleToolpath =
    [
        "(test 10252026)",
        "(Material Size) (X=4500.000, Y=3475.258, Z=0.000)",
        "G90G80G21G49",
        "(Tool Number:1) (0.300 mm dia. slot drill)",
        "G0Z5.0000",
        "M3 S15000",
        "G0 X2.3839 Y2.9541 Z5.0000",
        "G1   Z-1.0000 F6",
        "G1 X5.8693 Y2.9579  ",
        "X5.8590 Y2.9541 ",
        "G1   Z-2.0000 ",
        "X5.8693 Y2.9579 ",
        "G0   Z5.0000",
        "G0 X0.0000 Y0.0000 ",
        "M5",
        "M30",
    ];

    [Fact]
    public void Converts_artcam_file_like_the_old_normalizer()
    {
        var result = ArtCamLaserConverter.Convert(SingleToolpath, [400, 80]);

        Assert.Equal(
        [
            "(test 10252026)",
            "(Material Size) (X=4500.000, Y=3475.258, Z=0.000)",
            "M5",
            "G90G80G21G49",
            "(Tool Number:1) (0.300 mm dia. slot drill)",
            "F400",
            "G0 X2.3839 Y2.9541",
            "M3",
            "G1 X5.8693 Y2.9579",
            "X5.8590 Y2.9541",
            "X5.8693 Y2.9579",
            "M5",
            "G0 X0.0000 Y0.0000",
            "M30",
        ], result);
    }

    [Fact]
    public void Second_toolpath_gets_second_feed_rate()
    {
        string[] lines =
        [
            "(Tool Number:1) (0.300 mm dia. slot drill)",
            "M3 S15000",
            "G1 Z-1 F6",
            "G1 X1 Y1",
            "G0 Z5",
            "(Tool Number:2) (0.300 mm dia. slot drill)",
            "M3 S15000",
            "G0 X2 Y2",
            "G1 Z-1 F6",
            "G1 X3 Y3",
            "G0 Z5",
        ];

        var result = ArtCamLaserConverter.Convert(lines, [400, 80]);

        Assert.Equal(["F400", "F80"], result.Where(l => l.StartsWith('F')));
        var toolpaths = ArtCamLaserConverter.FindToolpaths(lines);
        Assert.Equal(2, toolpaths.Count);
        Assert.Equal("Tool Number:2 0.300 mm dia. slot drill", toolpaths[1].Tool);
    }

    [Fact]
    public void Handles_any_safe_height_and_feed_value()
    {
        // The old normalizer only knew Z5 and Z14.9140, and turned "F600" into "00".
        string[] lines = ["M3 S15000", "G0 X1 Y1 Z10", "G1 Z-1.5 F360", "G1 X2 Y2 F600", "G0 Z10", "G0 X0 Y0"];

        var result = ArtCamLaserConverter.Convert(lines, [80]);

        Assert.Equal(["M5", "F80", "G0 X1 Y1", "M3", "G1 X2 Y2", "M5", "G0 X0 Y0"], result);
    }

    [Fact]
    public void Carries_motion_mode_from_dropped_plunge_line()
    {
        // After "G0 X1 Y1" the plunge line sets G1. Once it's dropped, the next move must not run as G0.
        string[] lines = ["M3 S15000", "G0 X1 Y1 Z5", "G1 Z-1 F6", "X2 Y2", "X3 Y3"];

        var result = ArtCamLaserConverter.Convert(lines, [400]);

        Assert.Equal(["M5", "F400", "G0 X1 Y1", "M3", "G1 X2 Y2", "X3 Y3", "M5"], result);
    }

    [Fact]
    public void Keeps_arcs()
    {
        string[] lines = ["M3 S15000", "G1 Z-1 F6", "G2 X1 Y1 I0.5 J0", "G03 X0 Y0 I-0.5 J0", "G0 Z5"];

        var result = ArtCamLaserConverter.Convert(lines, [400]);

        Assert.Equal(["M5", "F400", "M3", "G2 X1 Y1 I0.5 J0", "G03 X0 Y0 I-0.5 J0", "M5"], result);
    }

    [Fact]
    public void Plain_M3_in_partly_converted_file_switches_laser_and_is_not_a_toolpath()
    {
        // Like "API Spoon - Posveta.TAP": one "M3 S15000", then the laser is switched with plain M3/M5.
        string[] lines = ["M5", "M3 S15000", "G0 X1 Y1 M5", "M3", "G1 X2 Y2", "G0 X3 Y3 M5", "M3", "G1 X4 Y4", "M5"];

        var result = ArtCamLaserConverter.Convert(lines, [400, 80]);

        Assert.Equal(["M5", "F400", "G0 X1 Y1", "M3", "G1 X2 Y2", "M5", "G0 X3 Y3", "M3", "G1 X4 Y4", "M5"], result);
        Assert.Single(ArtCamLaserConverter.FindToolpaths(lines));
    }

    [Fact]
    public void Moves_before_first_toolpath_start_get_first_feed_rate()
    {
        // Like "Maslacak.TAP": hand-edited, cuts with F500 before the only "M3 S15000".
        string[] lines = ["M5", "G1 F500", "G0 X1 Y1", "M3", "G1 X2 Y2", "M5", "G1 F200", "M3 S15000", "G0 X3 Y3", "M3", "G1 X4 Y4", "M5"];

        var result = ArtCamLaserConverter.Convert(lines, [400, 80]);

        Assert.Equal(["M5", "F400", "G0 X1 Y1", "M3", "G1 X2 Y2", "M5", "F80", "G0 X3 Y3", "M3", "G1 X4 Y4", "M5"], result);
        var toolpaths = ArtCamLaserConverter.FindToolpaths(lines);
        Assert.Equal(2, toolpaths.Count);
        Assert.Equal(1, toolpaths[0].Contours);
    }

    [Fact]
    public void Counts_contours_and_passes()
    {
        var toolpath = Assert.Single(ArtCamLaserConverter.FindToolpaths(SingleToolpath));

        Assert.Equal(1, toolpath.Contours);
        Assert.Equal(2, toolpath.Passes);
        Assert.Equal("Tool Number:1 0.300 mm dia. slot drill", toolpath.Tool);
    }

    [Fact]
    public void Recognizes_converted_files()
    {
        Assert.True(ArtCamLaserConverter.NeedsConversion(SingleToolpath));
        Assert.False(ArtCamLaserConverter.NeedsConversion(ArtCamLaserConverter.Convert(SingleToolpath, [400])));
    }
}
