using GrblCore;

namespace GrblCore.Tests;

public class FrameTests
{
    [Fact]
    public void Traces_bounds_with_laser_off_and_returns()
    {
        Assert.Equal(
        [
            "M5",
            "G90 G0 X1.4 Y2",
            "G0 X62.25",
            "G0 Y100.5",
            "G0 X1.4",
            "G0 Y2",
            "G0 X0 Y0",
        ], Frame.Commands(new Bounds(1.4, 2, 62.25, 100.5), new Position(0, 0, 0)));
    }
}
