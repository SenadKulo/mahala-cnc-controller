using CncController.Remote;

namespace CncController.App.Tests;

public sealed class JobHistoryTests : IDisposable
{
    readonly string _file = Path.Combine(Path.GetTempPath(), $"jobs-test-{Guid.NewGuid():N}.json");

    public void Dispose() => File.Delete(_file);

    [Fact]
    public void SaveAndLoad_KeepsTheJobs_ARunningOneWasClosedWithTheProgram()
    {
        var history = JobHistory.Load(_file);
        Assert.Empty(history.Records);
        history.Add(new JobRecord { FileName = "a.tap", Outcome = JobOutcome.Finished, Lines = 10, LinesDone = 10, RunSeconds = 75 });
        history.Add(new JobRecord { FileName = "b.tap", Outcome = JobOutcome.Running, Lines = 10, LinesDone = 4 });
        history.Save();

        var loaded = JobHistory.Load(_file);

        Assert.Equal(["a.tap", "b.tap"], loaded.Records.Select(r => r.FileName));
        Assert.Equal([JobOutcome.Finished, JobOutcome.ProgramClosed], loaded.Records.Select(r => r.Outcome));
        Assert.Equal(75, loaded.Records[0].RunSeconds);
        // The outcome is written as a word, readable in the file.
        Assert.Contains("\"Finished\"", File.ReadAllText(_file));
    }

    [Fact]
    public void Load_BrokenFile_StartsEmpty()
    {
        File.WriteAllText(_file, "{ not json");
        Assert.Empty(JobHistory.Load(_file).Records);
    }

    [Fact]
    public void Add_KeepsOnlyTheLastJobs()
    {
        var history = new JobHistory();
        for (int i = 0; i < JobHistory.MaxRecords + 5; i++)
            history.Add(new JobRecord { FileName = $"{i}.tap" });
        Assert.Equal(JobHistory.MaxRecords, history.Records.Count);
        Assert.Equal("5.tap", history.Records[0].FileName);
    }

    [Fact]
    public void HistoryEntry_TextsForThePage()
    {
        var started = new DateTime(2026, 11, 11, 14, 30, 0);
        var record = new JobRecord
        {
            FileName = "znak.tap",
            StartLine = 120,
            Started = started,
            // 50 min running plus a 10 min break.
            Ended = started.AddMinutes(60),
            Outcome = JobOutcome.Finished,
            Lines = 5000,
            LinesDone = 5000,
            EstimateSeconds = 40 * 60,
            EstimateDoneSeconds = 40 * 60,
            RunSeconds = 50 * 60,
            CompressorBreaks = 1,
            Restarts = 1,
            AutoResumes = 1,
        };

        var bs = Texts.HistoryEntry(record, Language.Bosnian);
        Assert.Equal(("Završen", Tone.Green, "50 min", "40 min", "+25 %", "5000"), (bs.Outcome, bs.Tone, bs.Duration, bs.Estimate, bs.Deviation, bs.Lines));
        Assert.Equal("od linije 120, planirane pauze: 1, restarti: 1, sam nastavio: 1, ukupno sa pauzama 1 h 0 min", bs.Details);

        record.Outcome = JobOutcome.EmergencyStop;
        record.LinesDone = 1200;
        record.EstimateDoneSeconds = 30;
        record.Ended = started.AddSeconds(record.RunSeconds);
        var en = Texts.HistoryEntry(record, Language.English);
        Assert.Equal(("Emergency stop", Tone.Red, "1200/5000"), (en.Outcome, en.Tone, en.Lines));
        // Too little estimated to compare.
        Assert.Null(en.Deviation);
        Assert.Equal("from line 120, scheduled breaks: 1, restarts: 1, resumed by itself: 1", en.Details);
    }
}
