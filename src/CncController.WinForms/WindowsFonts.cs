using System.Drawing.Drawing2D;
using System.Drawing.Text;
using CncController.App;
using GrblCore.Drawing;

namespace CncController.WinForms;

/// <summary>
/// The fonts installed in Windows as outlines for the laser (GDI+ <see cref="GraphicsPath.AddString(string, FontFamily, int, float, PointF, StringFormat)"/>):
/// every letter's outline, curves flattened to short lines, holes (o, a, e) as their own outlines.
/// </summary>
sealed class WindowsFonts : IFontOutlines
{
    /// <summary>The size the text is laid out at before scaling; large, so the float coordinates stay precise.</summary>
    const float EmSize = 1000;
    /// <summary>How far a flattened curve may be from the real one, mm.</summary>
    const double Tolerance = 0.02;

    static readonly FontStyle[] Styles = [FontStyle.Regular, FontStyle.Bold, FontStyle.Italic, FontStyle.Bold | FontStyle.Italic];

    public IReadOnlyList<string> Families
    {
        get
        {
            using var installed = new InstalledFontCollection();
            return [.. installed.Families.Where(f => Styles.Any(f.IsStyleAvailable)).Select(f => f.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];
        }
    }

    public LaserDrawing? Layout(string family, string text, double capHeight)
    {
        if (!(capHeight > 0))
            throw new ArgumentOutOfRangeException(nameof(capHeight), "The height must be positive.");
        using var installed = new InstalledFontCollection();
        var font = installed.Families.FirstOrDefault(f => string.Equals(f.Name, family, StringComparison.OrdinalIgnoreCase));
        if (font is null || Styles.Where(font.IsStyleAvailable).ToArray() is not [var style, ..])
            return null;

        using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        text = text.Replace("\r\n", "\n").Replace('\r', '\n').Replace('\t', ' ');

        // The capital H's height in layout units gives the scale; a font without H (symbols) uses most of its ascent.
        double capUnits;
        using (var h = new GraphicsPath())
        {
            h.AddString("H", font, (int)style, EmSize, PointF.Empty, format);
            capUnits = h.PointCount > 0 ? h.GetBounds().Height : 0;
        }
        if (!(capUnits > 0))
            capUnits = EmSize * font.GetCellAscent(style) / font.GetEmHeight(style) * 0.7;
        double scale = capHeight / capUnits;

        using var path = new GraphicsPath();
        path.AddString(text, font, (int)style, EmSize, PointF.Empty, format);
        if (path.PointCount == 0)
            return LaserDrawing.FromPaths([]);
        using (var identity = new Matrix())
            path.Flatten(identity, (float)Math.Max(Tolerance / scale, 0.01));

        // A figure starts with type 0 and ends with the close flag (0x80); Y goes down in GDI+, up on the machine.
        var points = path.PathPoints;
        var types = path.PathTypes;
        var figures = new List<IReadOnlyList<PathPoint>>();
        List<PathPoint>? figure = null;
        for (int i = 0; i < points.Length; i++)
        {
            var point = new PathPoint(points[i].X * scale, -points[i].Y * scale);
            if ((types[i] & (byte)PathPointType.PathTypeMask) == (byte)PathPointType.Start || figure is null)
            {
                End(figure);
                figure = [point];
            }
            else if (figure[^1] != point)
                figure.Add(point);
            if ((types[i] & (byte)PathPointType.CloseSubpath) != 0)
            {
                if (figure[0] != figure[^1])
                    figure.Add(figure[0]);
                End(figure);
                figure = null;
            }
        }
        End(figure);
        return LaserDrawing.FromPaths(PathOrder.NearestNext(figures));

        void End(List<PathPoint>? done)
        {
            if (done is { Count: > 1 })
                figures.Add(done);
        }
    }
}
