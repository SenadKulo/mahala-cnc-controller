using GrblCore.Drawing;

namespace CncController.App;

/// <summary>
/// The fonts installed on the computer, as outlines for text engraving. This layer isn't tied to Windows, so the
/// desktop window provides them; without it only the single-stroke font is offered.
/// </summary>
public interface IFontOutlines
{
    /// <summary>The installed font names, sorted.</summary>
    IReadOnlyList<string> Families { get; }

    /// <summary>
    /// The outline of every letter of <paramref name="text"/> (lines separated by "\n", left-aligned), capitals
    /// <paramref name="capHeight"/> mm high, the bottom-left corner of everything at 0,0; null when the font isn't installed.
    /// </summary>
    LaserDrawing? Layout(string family, string text, double capHeight);
}
