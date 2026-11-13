using GrblCore.Drawing;
using QRCoder;

namespace GrblCore.Tests;

public class QrEngravingTests
{
    static bool[,] Expected(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        int n = data.ModuleMatrix.Count - 8;
        var dark = new bool[n, n];
        for (int row = 0; row < n; row++)
            for (int column = 0; column < n; column++)
                dark[row, column] = data.ModuleMatrix[row + 4][column + 4];
        return dark;
    }

    [Fact]
    public void BurnedLines_CoverExactlyTheDarkModules()
    {
        const string text = "https://mahalaworkshop.com/Cnc";
        var layout = QrEngraving.Layout(text, 25, 0.2);
        var expected = Expected(text);
        int n = expected.GetLength(0);

        Assert.Equal(n, layout.Modules);
        Assert.Equal(25.0 / n, layout.ModuleSize, 9);
        Assert.All(layout.Drawing.Paths, p => Assert.Equal(2, p.Count));

        // Each module: burned if some line crosses its centre.
        for (int row = 0; row < n; row++)
        {
            for (int column = 0; column < n; column++)
            {
                double x = (column + 0.5) * layout.ModuleSize, top = 25 - row * layout.ModuleSize, bottom = top - layout.ModuleSize;
                bool burned = layout.Drawing.Paths.Any(p =>
                    p[0].Y < top && p[0].Y > bottom && Math.Min(p[0].X, p[1].X) < x && Math.Max(p[0].X, p[1].X) > x);
                Assert.True(expected[row, column] == burned, $"module {row},{column}");
            }
        }
    }

    [Fact]
    public void Lines_StayInsideTheSize_AndAreSpacedAsAsked()
    {
        var layout = QrEngraving.Layout("Mahala", 20, 0.25);

        var points = layout.Drawing.Paths.SelectMany(p => p).ToList();
        Assert.All(points, p => Assert.True(p.X is > 0 and < 20 && p.Y is > 0 and < 20));
        Assert.Equal(20, layout.Drawing.Width);
        // Whole lines per module, as near the spacing asked as that allows.
        var ys = points.Select(p => Math.Round(p.Y, 6)).Distinct().Order().ToList();
        double spacing = layout.ModuleSize / Math.Round(layout.ModuleSize / 0.25);
        Assert.All(ys.Zip(ys.Skip(1)), pair =>
        {
            double steps = (pair.Second - pair.First) / spacing;
            Assert.True(Math.Abs(steps - Math.Round(steps)) < 1e-4, $"{steps} spacings");
        });
    }

    [Fact]
    public void Rows_GoBackAndForth()
    {
        var layout = QrEngraving.Layout("Mahala", 20, 0.25);

        // One line after the other: the first goes right, the next line (lower) left.
        var first = layout.Drawing.Paths[0];
        var nextLine = layout.Drawing.Paths.First(p => p[0].Y < first[0].Y - 1e-9);
        Assert.True(first[1].X > first[0].X);
        Assert.True(nextLine[1].X < nextLine[0].X);
    }

    [Fact]
    public void ModuleSmallerThanTheSpot_StillBurnsEveryModule()
    {
        var layout = QrEngraving.Layout("Mahala", 5, 0.5);

        Assert.True(layout.ModuleSize < 0.5);
        Assert.All(layout.Drawing.Paths, p => Assert.True(Math.Abs(p[1].X - p[0].X) > 0));
    }

    [Fact]
    public void TooLong_OrEmpty_IsAnArgumentException()
    {
        Assert.Throws<ArgumentException>(() => QrEngraving.Layout("", 20, 0.2));
        Assert.Throws<ArgumentException>(() => QrEngraving.Layout(new string('x', 5000), 20, 0.2));
    }

    [Fact]
    public void GCode_Runs()
    {
        var layout = QrEngraving.Layout("https://mahalaworkshop.com", 30, 0.2);

        var program = GCodeProgram.Parse(LaserGCode.FromDrawing(layout.Drawing, 1500));

        Assert.True(program.CanRun);
        Assert.Empty(program.Issues);
    }
}
