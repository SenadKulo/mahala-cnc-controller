using System.Text;

namespace CncController.App;

/// <summary>
/// Copy of the log window in one file per day, e.g. %AppData%\CncController\logs\2026-10-26.txt,
/// so a night's restarts can be read in the morning even after the program was closed.
/// </summary>
public sealed class LogFile(string folder)
{
    bool _failed;

    /// <summary>Appends a line. A failure (disk full, folder locked) is reported once and then ignored.</summary>
    /// <returns>Error text the first time writing fails, otherwise null.</returns>
    public string? Write(DateTime time, string text)
    {
        try
        {
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, $"{time:yyyy-MM-dd}.txt"), $"{time:HH:mm:ss}  {text}{Environment.NewLine}", Encoding.UTF8);
            _failed = false;
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (_failed)
                return null;
            _failed = true;
            return ex.Message;
        }
    }
}
