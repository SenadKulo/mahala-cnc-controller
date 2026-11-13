using GrblCore.Drawing;

namespace GrblCore.Tests;

public class SvgDrawingTests
{
    static string Svg(string content, string size = "width=\"100mm\" height=\"50mm\" viewBox=\"0 0 100 50\"") =>
        $"<svg xmlns=\"http://www.w3.org/2000/svg\" {size}>{content}</svg>";

    static void Near(PathPoint expected, PathPoint actual)
    {
        Assert.Equal(expected.X, actual.X, 6);
        Assert.Equal(expected.Y, actual.Y, 6);
    }

    [Fact]
    public void Rectangle_InMm_IsClosed_AndYPointsUp()
    {
        var import = SvgDrawing.Parse(Svg("<rect x=\"10\" y=\"5\" width=\"30\" height=\"20\" />"));
        var drawing = import.Drawing;

        Assert.Equal(0, import.Skipped);
        Assert.Equal(30, drawing.Width, 6);
        Assert.Equal(20, drawing.Height, 6);
        var path = Assert.Single(drawing.Paths);
        Assert.Equal(5, path.Count);
        Assert.Equal(path[0], path[^1]);
        // SVG's y goes down; on the machine the rectangle's top edge (y=5) is the top.
        Assert.All(path, p => Assert.True(p.X is >= -1e-9 and <= 30 + 1e-9 && p.Y is >= -1e-9 and <= 20 + 1e-9));
    }

    [Fact]
    public void YIsFlipped()
    {
        // A line from the top-left down to the right: on the machine its left end is the high one.
        // (Burned from its lower end, the one nearer the zero.)
        var drawing = SvgDrawing.Parse(Svg("<line x1=\"0\" y1=\"0\" x2=\"10\" y2=\"20\" />")).Drawing;

        var path = Assert.Single(drawing.Paths);
        Near(new PathPoint(10, 0), path[0]);
        Near(new PathPoint(0, 20), path[1]);
    }

    [Theory]
    [InlineData("width=\"200mm\" height=\"100mm\" viewBox=\"0 0 100 50\"", 20)]   // a unit is 2 mm
    [InlineData("width=\"10cm\" height=\"5cm\" viewBox=\"0 0 100 50\"", 10)]
    [InlineData("width=\"96\" height=\"48\"", 25.4 / 96 * 10)]                      // no view box: pixels
    [InlineData("", 25.4 / 96 * 10)]
    [InlineData("width=\"1in\" viewBox=\"0 0 96 96\"", 25.4 / 96 * 10)]
    public void Units(string size, double expectedWidth)
    {
        var drawing = SvgDrawing.Parse(Svg("<line x1=\"0\" y1=\"0\" x2=\"10\" y2=\"0\" />", size)).Drawing;

        Assert.Equal(expectedWidth, drawing.Width, 6);
    }

    [Fact]
    public void Path_RelativeAndAbsolute_HV_AndImplicitLines()
    {
        // m then pairs are relative lines; H/V absolute; z closes.
        var drawing = SvgDrawing.Parse(Svg("<path d=\"m10,10 10,0 0,10 H10 z M50 10 l5-5e0\" />")).Drawing;

        Assert.Equal(2, drawing.Paths.Count);
        var square = drawing.Paths.Single(p => p.Count == 5);
        Assert.Equal(square[0], square[^1]);
        var line = drawing.Paths.Single(p => p.Count == 2);
        Assert.Equal(5 * Math.Sqrt(2), Math.Sqrt(Math.Pow(line[1].X - line[0].X, 2) + Math.Pow(line[1].Y - line[0].Y, 2)), 6);
    }

    [Fact]
    public void Circle_IsFlattenedCloseToTheRealOne()
    {
        var drawing = SvgDrawing.Parse(Svg("<circle cx=\"50\" cy=\"25\" r=\"20\" />")).Drawing;

        var path = Assert.Single(drawing.Paths);
        Assert.Equal(40, drawing.Width, 2);
        Assert.Equal(40, drawing.Height, 2);
        Assert.True(path.Count > 50, $"only {path.Count} points");
        // Every point and every chord's middle within 0.05 mm of the circle (centre 20,20 after moving to 0,0).
        for (int i = 1; i < path.Count; i++)
        {
            var middle = new PathPoint((path[i - 1].X + path[i].X) / 2, (path[i - 1].Y + path[i].Y) / 2);
            foreach (var p in new[] { path[i], middle })
                Assert.InRange(Math.Sqrt(Math.Pow(p.X - 20, 2) + Math.Pow(p.Y - 20, 2)), 19.95, 20.0001);
        }
    }

    [Fact]
    public void CubicCurve_EndsWhereItShould_AndStaysInsideItsControlPoints()
    {
        var drawing = SvgDrawing.Parse(Svg("<path d=\"M0 50 C0 0 100 0 100 50\" />")).Drawing;

        var path = Assert.Single(drawing.Paths);
        Assert.Equal(100, drawing.Width, 6);
        // The top of this curve is 3/4 of the way to its control points; short lines may cut it by the tolerance.
        Assert.InRange(drawing.Height, 37.5 - 0.021, 37.5);
        Near(new PathPoint(0, 0), path[0]);
        Near(new PathPoint(100, 0), path[^1]);
    }

    [Fact]
    public void SmoothAndQuadraticCurves_AreRead()
    {
        var drawing = SvgDrawing.Parse(Svg("<path d=\"M0 0 C10 10 20 10 30 0 S50 -10 60 0 Q70 10 80 0 T100 0\" />")).Drawing;

        var path = Assert.Single(drawing.Paths);
        Assert.Equal(100, drawing.Width, 6);
        Assert.True(path.Count > 10);
    }

    [Fact]
    public void Arc_WithCompactFlags()
    {
        // A half circle of radius 10, flags written together ("01").
        var drawing = SvgDrawing.Parse(Svg("<path d=\"M10 20a10 10 0 01 20 0\" />")).Drawing;

        var path = Assert.Single(drawing.Paths);
        Assert.Equal(20, drawing.Width, 6);
        Assert.InRange(drawing.Height, 10 - 0.021, 10);
        Near(new PathPoint(20, 0), path[^1]);
    }

    [Fact]
    public void Transforms_AreAppliedInOrder()
    {
        // Group: moved by 10,0; the line inside is scaled 2× and then rotated 90° around its start.
        var drawing = SvgDrawing.Parse(Svg(
            "<g transform=\"translate(10 0)\"><line x1=\"0\" y1=\"0\" x2=\"5\" y2=\"0\" transform=\"rotate(90) scale(2)\" /></g>" +
            "<line x1=\"0\" y1=\"0\" x2=\"1\" y2=\"0\" />")).Drawing;

        // The rotated line goes down in SVG (+y) 10 units, at x=10; the other one is at the top-left.
        Assert.Equal(10, drawing.Width, 6);
        Assert.Equal(10, drawing.Height, 6);
    }

    [Fact]
    public void HiddenAndDefinitions_AreLeftOut_TextAndImagesCounted()
    {
        var import = SvgDrawing.Parse(Svg(
            "<defs><rect width=\"500\" height=\"500\" /></defs>" +
            "<rect width=\"500\" height=\"500\" style=\"fill:red; display:none\" />" +
            "<g display=\"none\"><line x1=\"0\" y1=\"0\" x2=\"500\" y2=\"0\" /></g>" +
            "<text x=\"0\" y=\"0\">Mahala</text><image href=\"a.png\" />" +
            "<line x1=\"0\" y1=\"0\" x2=\"10\" y2=\"0\" />"));

        Assert.Equal(2, import.Skipped);
        Assert.Single(import.Drawing.Paths);
        Assert.Equal(10, import.Drawing.Width, 6);
    }

    [Fact]
    public void RoundedRectangle_HasArcsInTheCorners()
    {
        var drawing = SvgDrawing.Parse(Svg("<rect width=\"30\" height=\"20\" rx=\"5\" />")).Drawing;

        var path = Assert.Single(drawing.Paths);
        Assert.Equal(30, drawing.Width, 6);
        Assert.Equal(20, drawing.Height, 6);
        Assert.True(path.Count > 20);
        // The corner itself is cut off.
        Assert.DoesNotContain(path, p => p.X < 0.5 && p.Y < 0.5);
    }

    [Fact]
    public void Paths_AreOrderedForShortTravel()
    {
        // Saved far, near, middle: burned near, middle, far; the far one from its nearer end.
        var drawing = SvgDrawing.Parse(Svg(
            "<line x1=\"90\" y1=\"50\" x2=\"80\" y2=\"50\" />" +
            "<line x1=\"0\" y1=\"50\" x2=\"10\" y2=\"50\" />" +
            "<line x1=\"40\" y1=\"50\" x2=\"50\" y2=\"50\" />")).Drawing;

        Assert.Equal([0.0, 40, 80], drawing.Paths.Select(p => Math.Round(p[0].X, 6)));
    }

    [Fact]
    public void IllustratorEntities_AreRead_ExternalOnesAreNot()
    {
        var svg = """
            <?xml version="1.0"?>
            <!DOCTYPE svg PUBLIC "-//W3C//DTD SVG 1.1//EN" "http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd" [
                <!ENTITY ns_svg "http://www.w3.org/2000/svg">
            ]>
            <svg xmlns="&ns_svg;" width="10mm" height="10mm" viewBox="0 0 10 10"><line x1="0" y1="0" x2="10" y2="0" /></svg>
            """;

        Assert.Equal(10, SvgDrawing.Parse(svg).Drawing.Width, 6);
    }

    [Theory]
    [InlineData("not xml")]
    [InlineData("<html><body /></html>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><text>only text</text></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><path d=\"M0 0 L10 x\" /></svg>")]
    public void NotUsable_IsAFormatException(string svg) =>
        Assert.Throws<FormatException>(() => SvgDrawing.Parse(svg));

    [Fact]
    public void BrokenPathData_AfterClose_DoesNotHang()
    {
        var drawing = SvgDrawing.Parse(Svg("<path d=\"M0 0 L10 0 L10 10 Z 5 5\" />")).Drawing;

        Assert.Single(drawing.Paths);
    }

    [Fact]
    public void Scaled_KeepsTheCornerAtZero()
    {
        var drawing = SvgDrawing.Parse(Svg("<rect x=\"10\" y=\"5\" width=\"30\" height=\"20\" />")).Drawing.Scaled(2);

        Assert.Equal(60, drawing.Width, 6);
        Assert.Equal(40, drawing.Height, 6);
        Assert.Equal(0, drawing.Paths.SelectMany(p => p).Min(p => p.X), 6);
        Assert.Equal(0, drawing.Paths.SelectMany(p => p).Min(p => p.Y), 6);
    }

    [Fact]
    public void GCode_FromSvg_Runs()
    {
        var drawing = SvgDrawing.Parse(Svg("<circle cx=\"50\" cy=\"25\" r=\"20\" /><rect width=\"10\" height=\"10\" />")).Drawing;

        var program = GCodeProgram.Parse(LaserGCode.FromDrawing(drawing, 500));

        Assert.True(program.CanRun);
        Assert.Empty(program.Issues);
    }

    static double PathWidth(IReadOnlyList<PathPoint> path) => path.Max(p => p.X) - path.Min(p => p.X);

    [Fact]
    public void Use_CopiesShapesAndSymbols()
    {
        var import = SvgDrawing.Parse(Svg(
            "<defs><path id=\"dash\" d=\"M0,0 H10\" /><symbol id=\"box\" viewBox=\"0 0 1 1\"><rect width=\"1\" height=\"1\" /></symbol></defs>" +
            "<use href=\"#dash\" /><use href=\"#dash\" x=\"20\" />" +
            "<use xmlns:xlink=\"http://www.w3.org/1999/xlink\" xlink:href=\"#box\" x=\"40\" width=\"5\" height=\"5\" />"));

        Assert.Equal(0, import.Skipped);
        Assert.Equal(3, import.Drawing.Paths.Count);
        // Dashes at 0-10 and 20-30, the symbol's square scaled to 5 mm at 40.
        Assert.Equal(45, import.Drawing.Width, 6);
        Assert.Equal(5, import.Drawing.Height, 6);
        Assert.Equal([5.0, 10, 10], import.Drawing.Paths.Select(p => Math.Round(PathWidth(p), 6)).Order());
    }

    [Fact]
    public void Use_PointingIntoItselfOrNowhere_IsSkipped()
    {
        var import = SvgDrawing.Parse(Svg(
            "<g id=\"loop\"><line x1=\"0\" y1=\"0\" x2=\"10\" y2=\"0\" /><use href=\"#loop\" x=\"5\" /></g>" +
            "<use href=\"#missing\" /><use href=\"other.svg#loop\" />"));

        // The group once, its copy once (inside the copy, the use of itself stops).
        Assert.Equal(2, import.Drawing.Paths.Count);
        Assert.Equal(3, import.Skipped);
    }

    [Fact]
    public void Use_CopiesOfCopies_StopAtALimit()
    {
        // 10 to the 6th copies if followed to the end.
        var content = "<defs><line id=\"l0\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"0\" />";
        for (int level = 1; level <= 6; level++)
            content += $"<g id=\"l{level}\">" + string.Concat(Enumerable.Range(0, 10).Select(_ => $"<use href=\"#l{level - 1}\" />")) + "</g>";
        var import = SvgDrawing.Parse(Svg(content + "</defs><use href=\"#l6\" />"));

        Assert.InRange(import.Drawing.Paths.Count, 1, 10000);
        Assert.True(import.Skipped > 0);
    }

    [Fact]
    public void CssRules_HideByClassIdAndTag()
    {
        var import = SvgDrawing.Parse(Svg(
            "<style><![CDATA[/* Inkscape */ .hide, #gone { display: none } circle.ghost{visibility:hidden !important} .red { stroke: red }" +
            " @media print { .shown { display: none } }]]></style>" +
            "<rect class=\"red hide\" width=\"90\" height=\"40\" /><rect id=\"gone\" width=\"90\" height=\"40\" />" +
            "<circle class=\"ghost\" cx=\"50\" cy=\"25\" r=\"20\" /><circle class=\"red\" cx=\"5\" cy=\"5\" r=\"5\" />" +
            "<line class=\"shown\" x1=\"0\" y1=\"0\" x2=\"20\" y2=\"0\" />"));

        // Left: the red circle (not hidden) and the line (hidden only when printing).
        Assert.Equal(2, import.Drawing.Paths.Count);
        Assert.Equal(20, import.Drawing.Width, 6);
        Assert.InRange(import.Drawing.Height, 10 - 0.03, 10 + 1e-9);
    }

    [Fact]
    public void NestedSvg_FitsItsViewBoxIntoItsSize()
    {
        var import = SvgDrawing.Parse(Svg(
            "<line x1=\"0\" y1=\"0\" x2=\"1\" y2=\"0\" />" +
            "<svg x=\"10\" y=\"10\" width=\"20\" height=\"10\" viewBox=\"0 0 10 10\"><rect width=\"10\" height=\"10\" /></svg>" +
            "<svg x=\"40\" y=\"10\" width=\"20\" height=\"10\" viewBox=\"0 0 10 10\" preserveAspectRatio=\"none\"><rect width=\"10\" height=\"10\" /></svg>"));

        // Kept square and centred (15-25), or stretched to the whole size (40-60).
        Assert.Equal([1.0, 10, 20], import.Drawing.Paths.Select(p => Math.Round(PathWidth(p), 6)).Order());
        Assert.Equal(60, import.Drawing.Width, 6);
        Assert.Equal(20, import.Drawing.Height, 6);
        var square = import.Drawing.Paths.Single(p => Math.Abs(PathWidth(p) - 10) < 1e-6);
        Assert.Equal(15, square.Min(p => p.X), 6);
    }
}
