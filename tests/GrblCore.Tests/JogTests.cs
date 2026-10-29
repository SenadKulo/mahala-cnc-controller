using GrblCore;

namespace GrblCore.Tests;

public class JogTests
{
    [Fact]
    public void Jog_is_relative_rapid_then_back_to_absolute()
    {
        Assert.Equal(["G91 G21 G0 X10", "G90"], Jog.Commands(Axis.X, 10));
        Assert.Equal(["G91 G21 G0 Y-0.1", "G90"], Jog.Commands(Axis.Y, -0.1));
        Assert.Equal(["G91 G21 G0 Z100", "G90"], Jog.Commands(Axis.Z, 100));
    }

    [Fact]
    public void Distance_uses_dot_whatever_the_culture()
    {
        var culture = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("bs-Latn-BA");
            Assert.Equal("G91 G21 G0 X0.5", Jog.Commands(Axis.X, 0.5)[0]);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = culture;
        }
    }

    [Fact]
    public void Zero_distance_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Jog.Commands(Axis.X, 0));
    }
}
