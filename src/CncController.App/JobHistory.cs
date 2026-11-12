using System.Text.Json;
using System.Text.Json.Serialization;

namespace CncController.App;

/// <summary>How a job in the history ended.</summary>
public enum JobOutcome
{
    /// <summary>Still running, or waiting for the automatic resume.</summary>
    Running,
    Finished,
    Stopped,
    /// <summary>Emergency stop (Grbl reset from the program).</summary>
    EmergencyStop,
    /// <summary>Grbl restarted by itself and the job wasn't continued automatically.</summary>
    GrblRestarted,
    ConnectionLost,
    /// <summary>The program was closed (or crashed) while the job ran.</summary>
    ProgramClosed,
}

/// <summary>One job, from the start by hand to its end; automatic resumes after a restart belong to the same job.</summary>
public sealed class JobRecord
{
    public string FileName { get; set; } = "";
    /// <summary>Line in the file the job started from ("Nastavi od..."); null from the beginning.</summary>
    public int? StartLine { get; set; }
    public DateTime Started { get; set; }
    public DateTime? Ended { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public JobOutcome Outcome { get; set; }
    /// <summary>Program lines from the start to the end of the file, and how many of them Grbl accepted.</summary>
    public int Lines { get; set; }
    public int LinesDone { get; set; }
    /// <summary>The estimate for the whole job and for the part done, seconds.</summary>
    public double EstimateSeconds { get; set; }
    public double EstimateDoneSeconds { get; set; }
    /// <summary>Time the job was actually running (not paused, not in a break, not waiting after a restart), seconds.</summary>
    public double RunSeconds { get; set; }
    /// <summary>Pauses by the user (feed hold or with the laser off).</summary>
    public int Pauses { get; set; }
    public int CompressorBreaks { get; set; }
    public int Restarts { get; set; }
    public int AutoResumes { get; set; }
}

/// <summary>
/// The last jobs, newest last, stored in %AppData%\CncController\jobs.json: what ran, how it ended,
/// and how long it really took against the estimate.
/// </summary>
public sealed class JobHistory
{
    public const int MaxRecords = 100;

    public List<JobRecord> Records { get; set; } = [];

    /// <summary>File the history was loaded from and is saved to; null keeps it in memory only (tests).</summary>
    [JsonIgnore]
    public string? FilePath { get; private set; }

    public static JobHistory Load(string path)
    {
        JobHistory history;
        try
        {
            history = JsonSerializer.Deserialize<JobHistory>(File.ReadAllText(path)) ?? new JobHistory();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            history = new JobHistory();
        }
        history.FilePath = path;
        // Still "running" from the last time: the program was closed during the job.
        foreach (var record in history.Records.Where(r => r.Outcome == JobOutcome.Running))
            record.Outcome = JobOutcome.ProgramClosed;
        return history;
    }

    public void Add(JobRecord record)
    {
        Records.Add(record);
        if (Records.Count > MaxRecords)
            Records.RemoveRange(0, Records.Count - MaxRecords);
    }

    public void Save()
    {
        if (FilePath is null)
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        // A temp file plus move: a crash or power cut leaves either the old or the new file, never half of one.
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, FilePath, overwrite: true);
    }
}
