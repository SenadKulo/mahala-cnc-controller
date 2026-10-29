using System.Text.Json;
using System.Text.Json.Serialization;
using GrblCore;

namespace CncController.App;

/// <summary>
/// Last position reported by Grbl, stored in %AppData%\CncController\position.json
/// so it survives a reset, a USB disconnect or closing the program.
/// </summary>
public sealed class PositionMemory
{
    public Position? WorkPosition { get; set; }
    public Position? MachinePosition { get; set; }
    public DateTime? SavedAt { get; set; }

    /// <summary>File and last line Grbl accepted of the job that was running, if any.</summary>
    public string? JobFile { get; set; }
    public int? JobLine { get; set; }
    /// <summary>Last work position reported while that job ran - where it stopped, unlike <see cref="WorkPosition"/> after jogging.</summary>
    public Position? JobPosition { get; set; }

    /// <summary>File the position was loaded from and is saved to; null keeps it in memory only (tests).</summary>
    [JsonIgnore]
    public string? FilePath { get; private set; }

    public static PositionMemory Load(string path)
    {
        PositionMemory memory;
        try
        {
            memory = JsonSerializer.Deserialize<PositionMemory>(File.ReadAllText(path)) ?? new PositionMemory();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            memory = new PositionMemory();
        }
        memory.FilePath = path;
        return memory;
    }

    /// <summary>Remembers a new position. Returns false if it didn't change, so nothing was written.</summary>
    public bool Update(Position work, Position machine)
    {
        if (WorkPosition is { } w && MachinePosition is { } m && PositionRecovery.Same(w, work) && PositionRecovery.Same(m, machine))
            return false;
        WorkPosition = work;
        MachinePosition = machine;
        SavedAt = DateTime.Now;
        Save();
        return true;
    }

    public void Save()
    {
        if (FilePath is null)
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        // Written twice a second while the machine moves; a temp file plus move means a crash
        // or power cut leaves either the old or the new file, never half of one.
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, FilePath, overwrite: true);
    }
}
