using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace GrblCore.Drawing;

/// <summary>
/// What an SVG file gives for the laser: its outlines as a <see cref="LaserDrawing"/> in mm at the file's own size,
/// and how many elements that aren't lines (text, images, broken references) were left out.
/// </summary>
public sealed record SvgImport(LaserDrawing Drawing, int Skipped);

/// <summary>
/// Reads the outlines of an SVG file: paths (every command, curves and arcs flattened to short lines), lines,
/// polylines, polygons, rectangles, circles and ellipses, with their transforms and the document's units; copies made
/// with &lt;use&gt; (of shapes, groups and symbols) and nested &lt;svg&gt; with their own view box. Elements hidden by an
/// attribute, a style or a CSS rule in &lt;style&gt; are left out. Fill and stroke are ignored: every outline is burned once. Text and images aren't lines; they are counted in
/// <see cref="SvgImport.Skipped"/> (text must be converted to paths in the drawing program).
/// </summary>
public static class SvgDrawing
{
    /// <summary>How far a flattened curve may be from the real one, mm.</summary>
    const double Tolerance = 0.02;
    const int MaxSegments = 1000;
    const double MmPerPx = 25.4 / 96;

    static readonly HashSet<string> NotDrawn =
    [
        "defs", "clipPath", "mask", "symbol", "marker", "pattern", "metadata", "title", "desc", "style", "script",
        "linearGradient", "radialGradient", "filter", "namedview",
    ];
    static readonly HashSet<string> NotLines = ["text", "image", "foreignObject"];
    /// <summary>Copies made with &lt;use&gt;, in all; past this (copies of copies of copies) the rest are skipped.</summary>
    const int MaxUses = 10000;

    /// <exception cref="FormatException">Not an SVG file, or nothing in it to burn.</exception>
    public static SvgImport Parse(string svg)
    {
        XElement root;
        try
        {
            var settings = new XmlReaderSettings
            {
                // Illustrator declares its namespaces as entities; nothing is fetched, and entities can't blow up.
                DtdProcessing = DtdProcessing.Parse,
                XmlResolver = null,
                MaxCharactersFromEntities = 1024 * 1024,
            };
            using var reader = XmlReader.Create(new StringReader(svg), settings);
            root = XDocument.Load(reader).Root ?? throw new FormatException("The file is empty.");
        }
        catch (XmlException ex)
        {
            throw new FormatException("Not a valid SVG file: " + ex.Message, ex);
        }
        if (root.Name.LocalName != "svg")
            throw new FormatException("Not an SVG file.");

        var content = new Reader(root);
        content.Element(root, RootMatrix(root));
        if (content.Paths.Count == 0)
            throw new FormatException("The SVG has no lines to burn.");
        var drawing = LaserDrawing.FromPaths(PathOrder.NearestNext(content.Paths));
        return new SvgImport(drawing, content.Skipped);
    }

    /// <summary>From the root's user units to mm, Y up (the machine's way).</summary>
    static Matrix RootMatrix(XElement root)
    {
        double mmPerUnit = MmPerPx;
        double offsetX = 0, offsetY = 0;
        var viewBox = Numbers(Attr(root, "viewBox"));
        if (viewBox.Count == 4 && viewBox[2] > 0 && viewBox[3] > 0)
        {
            offsetX = -viewBox[0];
            offsetY = -viewBox[1];
            // Without a size, a user unit is a pixel. With one, the view box fills it (uniformly, as SVG's default).
            double? width = LengthMm(Attr(root, "width")), height = LengthMm(Attr(root, "height"));
            if (width is { } w && height is { } h)
                mmPerUnit = Math.Min(w / viewBox[2], h / viewBox[3]);
            else if (width is { } w2)
                mmPerUnit = w2 / viewBox[2];
            else if (height is { } h2)
                mmPerUnit = h2 / viewBox[3];
        }
        return Matrix.Scale(mmPerUnit, -mmPerUnit) * Matrix.Translate(offsetX, offsetY);
    }

    /// <summary>A size on the root ("100mm", "4in", "300", "300px") in mm; null without one or in %.</summary>
    static double? LengthMm(string? value)
    {
        if (value is null)
            return null;
        var match = Regex.Match(value.Trim(), @"^([-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?)\s*([a-z%]*)$");
        if (!match.Success)
            return null;
        double number = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        double? mm = match.Groups[2].Value switch
        {
            "" or "px" => number * MmPerPx,
            "mm" => number,
            "cm" => number * 10,
            "in" => number * 25.4,
            "pt" => number * 25.4 / 72,
            "pc" => number * 25.4 / 6,
            _ => null,
        };
        return mm > 0 ? mm : null;
    }

    /// <summary>A length on a shape, in user units (a unit like "mm" is taken as on the root).</summary>
    static double Length(XElement element, string name)
    {
        var value = Attr(element, name);
        if (value is null)
            return 0;
        if (double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return number;
        return LengthMm(value) is { } mm ? mm / MmPerPx : 0;
    }

    static string? Attr(XElement element, string name) => element.Attribute(name)?.Value;

    // ----- Paths in the file's order, then shorter travel -----

    static double Distance2(PathPoint a, PathPoint b) => (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);

    // ----- Elements -----

    sealed class Reader
    {
        public readonly List<List<PathPoint>> Paths = [];
        public int Skipped;

        readonly XElement _root;
        /// <summary>Elements by id, for &lt;use&gt; (the first one when an id repeats).</summary>
        readonly Dictionary<string, XElement> _ids = [];
        readonly List<CssSelector> _hiddenByCss;
        /// <summary>The size of the current viewport in user units, for sizes in % (nested &lt;svg&gt;, &lt;use&gt; of a symbol).</summary>
        (double Width, double Height) _viewport;
        /// <summary>Elements being copied right now: a &lt;use&gt; that points back into itself is skipped.</summary>
        readonly HashSet<XElement> _using = [];
        int _uses;

        public Reader(XElement root)
        {
            _root = root;
            foreach (var element in root.DescendantsAndSelf())
                if (Attr(element, "id") is { Length: > 0 } id)
                    _ids.TryAdd(id, element);
            _hiddenByCss = CssSelector.Hiding(string.Concat(root.Descendants().Where(e => e.Name.LocalName == "style").Select(e => e.Value + "\n")));
            var viewBox = Numbers(Attr(root, "viewBox"));
            _viewport = viewBox.Count == 4 && viewBox[2] > 0 && viewBox[3] > 0
                ? (viewBox[2], viewBox[3])
                : ((LengthMm(Attr(root, "width")) ?? 100) / MmPerPx, (LengthMm(Attr(root, "height")) ?? 100) / MmPerPx);
        }

        public void Element(XElement element, Matrix parent)
        {
            var name = element.Name.LocalName;
            if (NotDrawn.Contains(name) || Hidden(element))
                return;
            if (NotLines.Contains(name))
            {
                Skipped++;
                return;
            }
            var matrix = parent * Transform(Attr(element, "transform"));
            switch (name)
            {
                case "svg" when !ReferenceEquals(element, _root):
                    {
                        // A nested <svg>: its own place and size in the parent, and its view box fitted into it.
                        var fit = Fit(element, Length(element, "x"), Length(element, "y"),
                            Size(element, "width", _viewport.Width), Size(element, "height", _viewport.Height), out var inner);
                        Children(element, matrix * fit, inner);
                        break;
                    }
                case "svg" or "g" or "a" or "switch":
                    Children(element, matrix, _viewport);
                    break;
                case "use":
                    Use(element, matrix);
                    break;
                case "path":
                    PathData(Attr(element, "d") ?? "", matrix);
                    break;
                case "line":
                    Polyline([Length(element, "x1"), Length(element, "y1"), Length(element, "x2"), Length(element, "y2")], false, matrix);
                    break;
                case "polyline" or "polygon":
                    Polyline(Numbers(Attr(element, "points")), name == "polygon", matrix);
                    break;
                case "rect":
                    Rect(element, matrix);
                    break;
                case "circle":
                    {
                        double r = Length(element, "r");
                        Ellipse(Length(element, "cx"), Length(element, "cy"), r, r, matrix);
                        break;
                    }
                case "ellipse":
                    Ellipse(Length(element, "cx"), Length(element, "cy"), Length(element, "rx"), Length(element, "ry"), matrix);
                    break;
            }
        }

        void Children(XElement element, Matrix matrix, (double Width, double Height) viewport)
        {
            var outer = _viewport;
            _viewport = viewport;
            foreach (var child in element.Elements())
                Element(child, matrix);
            _viewport = outer;
        }

        /// <summary>
        /// &lt;use href="#id"&gt;: a copy of that element moved by x, y. A symbol or an &lt;svg&gt; is fitted into the
        /// use's width and height (its own, or the whole viewport, when the use has none), as a nested &lt;svg&gt;.
        /// </summary>
        void Use(XElement use, Matrix matrix)
        {
            var href = (use.Attribute("href") ?? use.Attribute(XName.Get("href", "http://www.w3.org/1999/xlink")))?.Value.Trim();
            if (href is not ['#', _, ..] || !_ids.TryGetValue(href[1..], out var target) || _using.Contains(target)
                || ReferenceEquals(target, _root) || ++_uses > MaxUses)
            {
                Skipped++;
                return;
            }
            double x = Length(use, "x"), y = Length(use, "y");
            _using.Add(target);
            try
            {
                if (target.Name.LocalName is "symbol" or "svg")
                {
                    if (Hidden(target))
                        return;
                    double width = Attr(use, "width") is not null ? Size(use, "width", _viewport.Width) : Size(target, "width", _viewport.Width);
                    double height = Attr(use, "height") is not null ? Size(use, "height", _viewport.Height) : Size(target, "height", _viewport.Height);
                    var fit = Fit(target, x, y, width, height, out var inner);
                    Children(target, matrix * fit, inner);
                }
                else
                    Element(target, matrix * Matrix.Translate(x, y));
            }
            finally
            {
                _using.Remove(target);
            }
        }

        /// <summary>A width or height in user units; "50%" of the viewport, and the whole viewport when missing.</summary>
        static double Size(XElement element, string name, double full)
        {
            var value = Attr(element, name)?.Trim();
            if (value is null or "auto")
                return full;
            if (value.EndsWith('%'))
                return double.TryParse(value[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var percent) ? full * percent / 100 : full;
            return Length(element, name);
        }

        /// <summary>
        /// Places a viewport at <paramref name="x"/>, <paramref name="y"/> of <paramref name="width"/> × <paramref name="height"/>
        /// and fits <paramref name="owner"/>'s view box into it by its preserveAspectRatio (default: whole, centred).
        /// </summary>
        /// <param name="inner">The viewport's size in its own units, for % inside it.</param>
        static Matrix Fit(XElement owner, double x, double y, double width, double height, out (double Width, double Height) inner)
        {
            var viewBox = Numbers(Attr(owner, "viewBox"));
            if (viewBox.Count != 4 || !(viewBox[2] > 0 && viewBox[3] > 0) || !(width > 0 && height > 0))
            {
                inner = (width, height);
                return Matrix.Translate(x, y);
            }
            inner = (viewBox[2], viewBox[3]);
            double sx = width / viewBox[2], sy = height / viewBox[3];
            var words = (Attr(owner, "preserveAspectRatio") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w != "defer").ToList();
            var align = words.FirstOrDefault() ?? "xMidYMid";
            double tx = x - viewBox[0] * sx, ty = y - viewBox[1] * sy;
            if (align != "none")
            {
                double s = words.Contains("slice") ? Math.Max(sx, sy) : Math.Min(sx, sy);
                double fx = align.Contains("xMid") ? 0.5 : align.Contains("xMax") ? 1 : 0;
                double fy = align.Contains("YMid") ? 0.5 : align.Contains("YMax") ? 1 : 0;
                sx = sy = s;
                tx = x - viewBox[0] * s + (width - viewBox[2] * s) * fx;
                ty = y - viewBox[1] * s + (height - viewBox[3] * s) * fy;
            }
            return Matrix.Translate(tx, ty) * Matrix.Scale(sx, sy);
        }

        bool Hidden(XElement element)
        {
            if (Attr(element, "display") == "none" || Attr(element, "visibility") is "hidden" or "collapse")
                return true;
            var style = Attr(element, "style");
            if (style is not null && HidingDeclaration.IsMatch(style))
                return true;
            return _hiddenByCss.Count > 0 && _hiddenByCss.Any(s => s.Matches(element));
        }

        void Polyline(List<double> numbers, bool closed, Matrix matrix)
        {
            var path = new List<PathPoint>();
            for (int i = 0; i + 1 < numbers.Count; i += 2)
                path.Add(matrix.Apply(numbers[i], numbers[i + 1]));
            if (closed && path.Count > 2)
                path.Add(path[0]);
            Add(path);
        }

        void Rect(XElement element, Matrix matrix)
        {
            double x = Length(element, "x"), y = Length(element, "y"), w = Length(element, "width"), h = Length(element, "height");
            if (!(w > 0 && h > 0))
                return;
            // A missing rx or ry takes the other's value; neither is more than half the side.
            double? rxAttr = Attr(element, "rx") is null ? null : Length(element, "rx");
            double? ryAttr = Attr(element, "ry") is null ? null : Length(element, "ry");
            double rx = Math.Min(Math.Abs(rxAttr ?? ryAttr ?? 0), w / 2), ry = Math.Min(Math.Abs(ryAttr ?? rxAttr ?? 0), h / 2);
            if (rx <= 0 || ry <= 0)
            {
                Polyline([x, y, x + w, y, x + w, y + h, x, y + h], true, matrix);
                return;
            }
            PathData(FormattableString.Invariant(
                $"M{x + rx},{y} H{x + w - rx} A{rx},{ry} 0 0 1 {x + w},{y + ry} V{y + h - ry} A{rx},{ry} 0 0 1 {x + w - rx},{y + h} H{x + rx} A{rx},{ry} 0 0 1 {x},{y + h - ry} V{y + ry} A{rx},{ry} 0 0 1 {x + rx},{y} Z"),
                matrix);
        }

        void Ellipse(double cx, double cy, double rx, double ry, Matrix matrix)
        {
            if (!(rx > 0 && ry > 0))
                return;
            PathData(FormattableString.Invariant(
                $"M{cx - rx},{cy} A{rx},{ry} 0 1 0 {cx + rx},{cy} A{rx},{ry} 0 1 0 {cx - rx},{cy} Z"), matrix);
        }

        void Add(List<PathPoint> path)
        {
            if (path.Count >= 2)
                Paths.Add(path);
        }

        // ----- Path data -----

        void PathData(string d, Matrix matrix)
        {
            var data = new PathTokens(d);
            List<PathPoint>? current = null;
            double x = 0, y = 0, startX = 0, startY = 0;
            // The last control point, for the smooth curves S and T (null after other commands).
            double? cubicX = null, cubicY = null, quadX = null, quadY = null;
            char command = ' ';

            void Finish()
            {
                if (current is not null)
                    Add(current);
                current = null;
            }
            void LineTo(double nx, double ny)
            {
                current ??= [matrix.Apply(x, y)];
                current.Add(matrix.Apply(nx, ny));
                x = nx;
                y = ny;
            }

            while (data.More())
            {
                if (data.Command() is { } next)
                    command = next;
                else if (command == ' ')
                    return; // numbers before any command: broken data, nothing more to read
                bool relative = char.IsLower(command);
                double ox = relative ? x : 0, oy = relative ? y : 0;
                char upper = char.ToUpperInvariant(command);
                if (upper is not ('C' or 'S'))
                    cubicX = cubicY = null;
                if (upper is not ('Q' or 'T'))
                    quadX = quadY = null;
                switch (upper)
                {
                    case 'M':
                        Finish();
                        x = startX = ox + data.Number();
                        y = startY = oy + data.Number();
                        // Pairs after a move are lines.
                        command = relative ? 'l' : 'L';
                        break;
                    case 'L':
                        {
                            double nx = ox + data.Number(), ny = oy + data.Number();
                            LineTo(nx, ny);
                            break;
                        }
                    case 'H':
                        LineTo(ox + data.Number(), y);
                        break;
                    case 'V':
                        LineTo(x, oy + data.Number());
                        break;
                    case 'C' or 'S':
                        {
                            double x1, y1;
                            if (upper == 'C')
                            {
                                x1 = ox + data.Number();
                                y1 = oy + data.Number();
                            }
                            else
                            {
                                x1 = cubicX is { } cx ? 2 * x - cx : x;
                                y1 = cubicY is { } cy ? 2 * y - cy : y;
                            }
                            double x2 = ox + data.Number(), y2 = oy + data.Number(), nx = ox + data.Number(), ny = oy + data.Number();
                            current ??= [matrix.Apply(x, y)];
                            Cubic(current, matrix.Apply(x, y), matrix.Apply(x1, y1), matrix.Apply(x2, y2), matrix.Apply(nx, ny));
                            (cubicX, cubicY, x, y) = (x2, y2, nx, ny);
                            break;
                        }
                    case 'Q' or 'T':
                        {
                            double x1, y1;
                            if (upper == 'Q')
                            {
                                x1 = ox + data.Number();
                                y1 = oy + data.Number();
                            }
                            else
                            {
                                x1 = quadX is { } qx ? 2 * x - qx : x;
                                y1 = quadY is { } qy ? 2 * y - qy : y;
                            }
                            double nx = ox + data.Number(), ny = oy + data.Number();
                            current ??= [matrix.Apply(x, y)];
                            Quadratic(current, matrix.Apply(x, y), matrix.Apply(x1, y1), matrix.Apply(nx, ny));
                            (quadX, quadY, x, y) = (x1, y1, nx, ny);
                            break;
                        }
                    case 'A':
                        {
                            double rx = data.Number(), ry = data.Number(), angle = data.Number();
                            bool large = data.Flag(), sweep = data.Flag();
                            double nx = ox + data.Number(), ny = oy + data.Number();
                            current ??= [matrix.Apply(x, y)];
                            Arc(current, matrix, x, y, rx, ry, angle, large, sweep, nx, ny);
                            x = nx;
                            y = ny;
                            break;
                        }
                    case 'Z':
                        if (current is not null)
                        {
                            current.Add(matrix.Apply(startX, startY));
                            Finish();
                        }
                        x = startX;
                        y = startY;
                        // Z takes no numbers: numbers after it, without a command, are broken data.
                        command = ' ';
                        break;
                    default:
                        return; // an unknown command: the rest can't be read
                }
            }
            Finish();
        }

        static void Cubic(List<PathPoint> path, PathPoint p0, PathPoint p1, PathPoint p2, PathPoint p3)
        {
            // Uniform steps; the error is at most |second derivative| / (8 n²), and that is at most 6 × the larger bend.
            double bend = Math.Max(Bend(p0, p1, p2), Bend(p1, p2, p3));
            int n = Segments(Math.Sqrt(6 * bend / (8 * Tolerance)));
            for (int i = 1; i <= n; i++)
            {
                double t = (double)i / n, u = 1 - t;
                double a = u * u * u, b = 3 * u * u * t, c = 3 * u * t * t, e = t * t * t;
                path.Add(new PathPoint(a * p0.X + b * p1.X + c * p2.X + e * p3.X, a * p0.Y + b * p1.Y + c * p2.Y + e * p3.Y));
            }
        }

        static void Quadratic(List<PathPoint> path, PathPoint p0, PathPoint p1, PathPoint p2)
        {
            int n = Segments(Math.Sqrt(2 * Bend(p0, p1, p2) / (8 * Tolerance)));
            for (int i = 1; i <= n; i++)
            {
                double t = (double)i / n, u = 1 - t;
                double a = u * u, b = 2 * u * t, c = t * t;
                path.Add(new PathPoint(a * p0.X + b * p1.X + c * p2.X, a * p0.Y + b * p1.Y + c * p2.Y));
            }
        }

        static double Bend(PathPoint a, PathPoint b, PathPoint c) => Math.Sqrt(Distance2(new(a.X - 2 * b.X + c.X, a.Y - 2 * b.Y + c.Y), new(0, 0)));

        static int Segments(double n) => double.IsFinite(n) ? Math.Clamp((int)Math.Ceiling(n), 1, MaxSegments) : 1;

        /// <summary>SVG's elliptical arc, from its end points to its centre (SVG spec, appendix "Implementation notes").</summary>
        static void Arc(List<PathPoint> path, Matrix matrix, double x1, double y1, double rx, double ry, double angleDegrees,
            bool large, bool sweep, double x2, double y2)
        {
            rx = Math.Abs(rx);
            ry = Math.Abs(ry);
            if (rx == 0 || ry == 0 || (x1 == x2 && y1 == y2))
            {
                path.Add(matrix.Apply(x2, y2));
                return;
            }
            double phi = angleDegrees * Math.PI / 180, cos = Math.Cos(phi), sin = Math.Sin(phi);
            double dx = (x1 - x2) / 2, dy = (y1 - y2) / 2;
            double x1p = cos * dx + sin * dy, y1p = -sin * dx + cos * dy;
            double lambda = x1p * x1p / (rx * rx) + y1p * y1p / (ry * ry);
            if (lambda > 1)
            {
                rx *= Math.Sqrt(lambda);
                ry *= Math.Sqrt(lambda);
            }
            double numerator = rx * rx * ry * ry - rx * rx * y1p * y1p - ry * ry * x1p * x1p;
            double denominator = rx * rx * y1p * y1p + ry * ry * x1p * x1p;
            double coefficient = (large == sweep ? -1 : 1) * Math.Sqrt(Math.Max(0, numerator / denominator));
            double cxp = coefficient * rx * y1p / ry, cyp = -coefficient * ry * x1p / rx;
            double cx = cos * cxp - sin * cyp + (x1 + x2) / 2, cy = sin * cxp + cos * cyp + (y1 + y2) / 2;
            double theta1 = Math.Atan2((y1p - cyp) / ry, (x1p - cxp) / rx);
            double theta2 = Math.Atan2((-y1p - cyp) / ry, (-x1p - cxp) / rx);
            double delta = theta2 - theta1;
            if (sweep && delta < 0)
                delta += 2 * Math.PI;
            else if (!sweep && delta > 0)
                delta -= 2 * Math.PI;

            // Steps so a chord is at most the tolerance from the arc, at the arc's size on the machine.
            double radiusMm = Math.Max(rx, ry) * matrix.ScaleFactor;
            double step = radiusMm > Tolerance ? 2 * Math.Acos(1 - Tolerance / radiusMm) : Math.PI / 2;
            int n = Segments(Math.Abs(delta) / step);
            for (int i = 1; i <= n; i++)
            {
                double t = theta1 + delta * i / n;
                double px = cx + rx * cos * Math.Cos(t) - ry * sin * Math.Sin(t);
                double py = cy + rx * sin * Math.Cos(t) + ry * cos * Math.Sin(t);
                path.Add(i == n ? matrix.Apply(x2, y2) : matrix.Apply(px, py));
            }
        }
    }

    /// <summary>Reads path data: command letters, numbers in any of SVG's compact forms ("1.5.5", "1-2", "1e-3"), arc flags.</summary>
    sealed class PathTokens(string text)
    {
        int _at;

        void SkipSeparators()
        {
            while (_at < text.Length && (char.IsWhiteSpace(text[_at]) || text[_at] == ','))
                _at++;
        }

        public bool More()
        {
            SkipSeparators();
            return _at < text.Length;
        }

        /// <summary>The next command letter, or null when numbers follow (the last command repeats).</summary>
        public char? Command()
        {
            SkipSeparators();
            if (_at < text.Length && char.IsLetter(text[_at]) && text[_at] is not ('e' or 'E'))
                return text[_at++];
            return null;
        }

        public double Number()
        {
            SkipSeparators();
            int start = _at;
            if (_at < text.Length && text[_at] is '+' or '-')
                _at++;
            bool dot = false, digits = false;
            while (_at < text.Length && (char.IsAsciiDigit(text[_at]) || (text[_at] == '.' && !dot)))
            {
                if (text[_at] == '.')
                    dot = true;
                else
                    digits = true;
                _at++;
            }
            if (digits && _at < text.Length && text[_at] is 'e' or 'E')
            {
                int mark = _at++;
                if (_at < text.Length && text[_at] is '+' or '-')
                    _at++;
                if (_at < text.Length && char.IsAsciiDigit(text[_at]))
                    while (_at < text.Length && char.IsAsciiDigit(text[_at]))
                        _at++;
                else
                    _at = mark;
            }
            if (!digits)
                throw new FormatException($"A number was expected in the path data at character {start + 1}.");
            return double.Parse(text.AsSpan(start, _at - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        /// <summary>An arc's flag: a single 0 or 1, which may touch the next number ("a5 5 0 01 10 10").</summary>
        public bool Flag()
        {
            SkipSeparators();
            if (_at < text.Length && text[_at] is '0' or '1')
                return text[_at++] == '1';
            throw new FormatException($"An arc flag (0 or 1) was expected in the path data at character {_at + 1}.");
        }
    }

    // ----- Transforms -----

    static readonly Regex TransformPart = new(@"(matrix|translate|scale|rotate|skewX|skewY)\s*\(([^)]*)\)", RegexOptions.CultureInvariant);

    static Matrix Transform(string? value)
    {
        var result = Matrix.Identity;
        if (value is null)
            return result;
        foreach (Match part in TransformPart.Matches(value))
        {
            var n = Numbers(part.Groups[2].Value);
            double At(int i, double otherwise = 0) => i < n.Count ? n[i] : otherwise;
            var next = part.Groups[1].Value switch
            {
                "matrix" when n.Count == 6 => new Matrix(n[0], n[1], n[2], n[3], n[4], n[5]),
                "translate" => Matrix.Translate(At(0), At(1)),
                "scale" => Matrix.Scale(At(0, 1), At(1, At(0, 1))),
                "rotate" => Matrix.Translate(At(1), At(2)) * Matrix.Rotate(At(0)) * Matrix.Translate(-At(1), -At(2)),
                "skewX" => new Matrix(1, 0, Math.Tan(At(0) * Math.PI / 180), 1, 0, 0),
                "skewY" => new Matrix(1, Math.Tan(At(0) * Math.PI / 180), 0, 1, 0, 0),
                _ => Matrix.Identity,
            };
            result *= next;
        }
        return result;
    }

    static readonly Regex NumberPattern = new(@"[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?", RegexOptions.CultureInvariant);

    static List<double> Numbers(string? text) =>
        text is null ? [] : [.. NumberPattern.Matches(text).Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture))];

    /// <summary>display:none or visibility:hidden in a style attribute or a CSS rule's declarations.</summary>
    static readonly Regex HidingDeclaration = new(
        @"(^|;)\s*(display\s*:\s*none|visibility\s*:\s*(hidden|collapse))\s*(!\s*important\s*)?(;|$)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>
    /// A simple CSS selector from a &lt;style&gt; rule that hides: "tag", ".class", "#id" or a mix ("path.hidden").
    /// Selectors with combinators, attributes or pseudo-classes are ignored, as are rules inside @media and the like.
    /// </summary>
    sealed record CssSelector(string? Tag, string? Id, string[] Classes)
    {
        static readonly Regex Simple = new(@"^([A-Za-z][\w-]*)?((?:[.#][\w-]+)*)$", RegexOptions.CultureInvariant);
        static readonly Regex Part = new(@"([.#])([\w-]+)", RegexOptions.CultureInvariant);

        public bool Matches(XElement element)
        {
            if (Tag is not null && element.Name.LocalName != Tag)
                return false;
            if (Id is not null && Attr(element, "id") != Id)
                return false;
            if (Classes.Length == 0)
                return true;
            var classes = (Attr(element, "class") ?? "").Split((char[])[' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
            return Classes.All(classes.Contains);
        }

        /// <summary>The selectors of the top-level rules in <paramref name="css"/> that set display:none or visibility:hidden.</summary>
        public static List<CssSelector> Hiding(string css)
        {
            var hiding = new List<CssSelector>();
            css = Regex.Replace(css, @"/\*.*?\*/", "", RegexOptions.Singleline);
            int at = 0;
            while (at < css.Length)
            {
                int open = css.IndexOf('{', at);
                if (open < 0)
                    break;
                int depth = 1, end = open + 1;
                for (; end < css.Length && depth > 0; end++)
                    depth += css[end] == '{' ? 1 : css[end] == '}' ? -1 : 0;
                var selectors = css[at..open];
                var body = css[(open + 1)..Math.Max(open + 1, end - 1)];
                at = end;
                // "@import x; .a {": only what follows the last statement belongs to the rule.
                if (selectors.LastIndexOf(';') is var semicolon and >= 0)
                    selectors = selectors[(semicolon + 1)..];
                if (selectors.TrimStart().StartsWith('@') || !HidingDeclaration.IsMatch(body.Trim()))
                    continue;
                foreach (var selector in selectors.Split(','))
                    if (Parse(selector.Trim()) is { } parsed)
                        hiding.Add(parsed);
            }
            return hiding;
        }

        static CssSelector? Parse(string selector)
        {
            var match = Simple.Match(selector);
            if (!match.Success || selector.Length == 0)
                return null;
            var parts = Part.Matches(match.Groups[2].Value);
            return new CssSelector(
                match.Groups[1].Success && match.Groups[1].Length > 0 ? match.Groups[1].Value : null,
                parts.Where(p => p.Groups[1].Value == "#").Select(p => p.Groups[2].Value).FirstOrDefault(),
                [.. parts.Where(p => p.Groups[1].Value == ".").Select(p => p.Groups[2].Value)]);
        }
    }

    /// <summary>An affine transform as SVG writes it: x' = a x + c y + e, y' = b x + d y + f.</summary>
    readonly record struct Matrix(double A, double B, double C, double D, double E, double F)
    {
        public static readonly Matrix Identity = new(1, 0, 0, 1, 0, 0);
        public static Matrix Translate(double x, double y) => new(1, 0, 0, 1, x, y);
        public static Matrix Scale(double x, double y) => new(x, 0, 0, y, 0, 0);

        public static Matrix Rotate(double degrees)
        {
            double r = degrees * Math.PI / 180, cos = Math.Cos(r), sin = Math.Sin(r);
            return new(cos, sin, -sin, cos, 0, 0);
        }

        /// <summary><c>m * n</c> applies <paramref name="n"/> first, then <paramref name="m"/>, as SVG nests transforms.</summary>
        public static Matrix operator *(Matrix m, Matrix n) => new(
            m.A * n.A + m.C * n.B, m.B * n.A + m.D * n.B,
            m.A * n.C + m.C * n.D, m.B * n.C + m.D * n.D,
            m.A * n.E + m.C * n.F + m.E, m.B * n.E + m.D * n.F + m.F);

        public PathPoint Apply(double x, double y) => new(A * x + C * y + E, B * x + D * y + F);

        /// <summary>How much lengths grow on average (for the arc's steps).</summary>
        public double ScaleFactor => Math.Sqrt(Math.Abs(A * D - B * C));
    }
}
