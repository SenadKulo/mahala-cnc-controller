using CncController.Remote;
using GrblCore;

namespace CncController.App;

/// <summary>Texts shown the same way on every screen (desktop window and web page), in Bosnian or English.</summary>
public static class Texts
{
    /// <summary>The Bosnian or the English text, for the language chosen.</summary>
    public static string T(this Language language, string bosnian, string english) =>
        language == Language.English ? english : bosnian;

    public static string Duration(TimeSpan time) => time.TotalMinutes switch
    {
        < 1 => $"{Math.Max(1, (int)Math.Round(time.TotalSeconds))} s",
        < 60 => $"{(int)Math.Round(time.TotalMinutes)} min",
        _ => $"{(int)time.TotalHours} h {time.Minutes} min",
    };

    public static string State(GrblStatus status, Language l) => status.State switch
    {
        MachineState.Idle => l.T("MIRUJE", "IDLE"),
        MachineState.Run => l.T("RADI", "RUNNING"),
        MachineState.Jog => l.T("POMJERA", "JOGGING"),
        MachineState.Home => "HOMING",
        MachineState.Hold => l.T("PAUZA", "PAUSED"),
        MachineState.Queue => l.T("ČEKA", "QUEUED"),
        MachineState.Door => l.T("VRATA", "DOOR"),
        MachineState.Alarm => "ALARM",
        MachineState.Check => l.T("PROVJERA", "CHECK"),
        MachineState.Sleep => l.T("SPAVA", "SLEEP"),
        _ => status.RawState.Length > 0 ? status.RawState.ToUpperInvariant() : "?",
    };

    /// <summary>Text for the state display: Grbl's state, or what the connection is waiting for.</summary>
    public static string StateText(MachineSession session)
    {
        var l = session.Language;
        return !session.IsOpen ? l.T("NIJE SPOJENO", "NOT CONNECTED")
            : session.Connection == ConnectionStatus.WaitingForGrbl ? l.T("ČEKAM GRBL", "WAITING FOR GRBL")
            : session.LastStatus is { } status ? State(status, l)
            : l.T("ČEKAM STATUS", "WAITING FOR STATUS");
    }

    public static Tone StateTone(MachineSession session) =>
        !session.IsOpen ? Tone.Grey
        : session.Connection == ConnectionStatus.WaitingForGrbl ? Tone.Orange
        : session.LastStatus?.State switch
        {
            MachineState.Idle => Tone.Green,
            MachineState.Run or MachineState.Jog or MachineState.Home => Tone.Blue,
            MachineState.Hold or MachineState.Queue or MachineState.Door => Tone.Orange,
            MachineState.Alarm => Tone.Red,
            _ => Tone.Grey,
        };

    public static (string Text, Tone Tone) Connection(MachineSession session)
    {
        var l = session.Language;
        return session.Connection switch
        {
            ConnectionStatus.WaitingForGrbl => (l.T("Čekam Grbl...", "Waiting for Grbl..."), Tone.Orange),
            ConnectionStatus.Connected => ($"Grbl {session.Version}", Tone.Green),
            ConnectionStatus.Lost => (l.T("Veza prekinuta", "Connection lost"), Tone.Red),
            _ => (l.T("Nije spojeno", "Not connected"), Tone.Muted),
        };
    }

    public static (string Text, Tone Tone) Remembered(MachineSession session)
    {
        var l = session.Language;
        if (session.RememberedPosition is not { } work)
            return (l.T("Zapamćeno: -", "Remembered: -"), Tone.Muted);
        bool waiting = session.PositionWaiting;
        return (l.T("Zapamćeno: ", "Remembered: ") + work
                + (waiting ? l.T("  - pozicija čeka potvrdu", "  - position waits for confirmation") : $"  ({session.RememberedAt:dd.MM. HH:mm})"),
            waiting ? Tone.Orange : Tone.Muted);
    }

    /// <summary>Grbl restarted and lost its position: the question whether to restore the remembered one.</summary>
    public static string Question(PositionQuestion q, Language l)
    {
        var nl = Environment.NewLine;
        var job = q.JobLine is { } line
            ? nl + nl + l.T($"Posao {Path.GetFileName(q.JobFile)} je stao na liniji {line}.", $"Job {Path.GetFileName(q.JobFile)} stopped at line {line}.")
            : "";
        return l.T("Grbl je ponovo pokrenut (USB ili struja) i izgubio je poziciju.", "Grbl restarted (USB or power) and lost its position.") + nl + nl +
            l.T($"Sada:       {q.Current}", $"Now:        {q.Current}") + nl +
            l.T($"Vratiti na: {q.Target}", $"Restore to: {q.Target}") + nl +
            l.T($"(zapamćeno {q.SavedAt:dd.MM.yyyy HH:mm:ss})", $"(remembered {q.SavedAt:dd.MM.yyyy HH:mm:ss})") + job + nl + nl +
            l.T("Vratiti zapamćenu poziciju? Odgovori Da samo ako CNC u međuvremenu nije ručno pomjeran.",
                "Restore the remembered position? Answer Yes only if the CNC wasn't moved by hand in the meantime.");
    }

    /// <summary>Lines, duration and size of the loaded program; null without one.</summary>
    public static string? FileInfo(MachineSession session)
    {
        if (session.Program is not { } program || session.Estimate is not { } estimate)
            return null;
        var l = session.Language;
        var size = session.Toolpath?.CutBounds is { } b ? FormattableString.Invariant($"  ·  {b.Width:0.#} × {b.Height:0.#} mm") : "";
        return l.T($"{program.Lines.Count} linija  ·  trajanje ≈ {Duration(estimate.Total)}", $"{program.Lines.Count} lines  ·  duration ≈ {Duration(estimate.Total)}") + size;
    }

    /// <summary>Lines done, percentage and time left of the current job; null without one.</summary>
    public static string? Progress(MachineSession session)
    {
        if (session.Job is not { } job || session.Estimate is not { } estimate)
            return null;
        int completed = job.CompletedCount;
        return $"{completed} / {job.TotalCount} ({100.0 * completed / Math.Max(1, job.TotalCount):0}%), "
            + session.Language.T("još", "left") + $" ≈ {Duration(estimate.Remaining(completed))}";
    }

    /// <summary>The last line Grbl accepted, with its number in the file.</summary>
    public static string? CurrentLine(MachineSession session) =>
        session.Job?.LastCompletedLine is { } last ? $"{last.SourceLineNumber}: {last.Text}" : null;

    /// <summary>When the compressors switch on next, for the link next to the compressor pause.</summary>
    public static string Compressors(MachineSession session, DateTime now)
    {
        var l = session.Language;
        return session.Compressors is { } schedule
            ? l.T("sljedeće", "next") + $" ≈ {schedule.NextStart(now):HH:mm:ss}"
            : l.T("ritam nepoznat", "rhythm unknown");
    }

    /// <summary>Why "Nastavi od..." can't start from the chosen line.</summary>
    public static string ResumeProblem(ResumeProblem problem, Language l) => problem.Kind switch
    {
        ResumeProblemKind.NothingLeft => l.T("Od ove linije do kraja fajla nema više pokreta.", "There are no more moves from this line to the end of the file."),
        ResumeProblemKind.UnsupportedCommand => l.T(
            $"Ne može se nastaviti: linija {problem.Line!.SourceLineNumber} ({problem.Line.Text}) mijenja koordinatni sistem, pa se ne zna gdje bi laser trebao biti.",
            $"Can't resume: line {problem.Line!.SourceLineNumber} ({problem.Line.Text}) changes the coordinate system, so where the laser should be is unknown."),
        _ => l.T(
            $"Ne može se nastaviti: linija {problem.Line!.SourceLineNumber} je predugačka kad joj se doda način kretanja.",
            $"Can't resume: line {problem.Line!.SourceLineNumber} is too long once its motion mode is added."),
    };

    /// <summary>What "Nastavi od..." will send: the lines around the start, the preparation, then the rest of the file.</summary>
    public static string ResumePlan(GCodeProgram program, ResumePlan plan, JobEstimate? estimate, Language l)
    {
        const int contextLines = 2;
        var lines = program.Lines;
        var text = new System.Text.StringBuilder();
        var remaining = estimate is null ? "" : l.T(", ostaje", ", left") + $" ≈ {Duration(estimate.Remaining(plan.StartIndex))}";
        text.AppendLine(l.T(
            $"Počinje od linije {lines[plan.StartIndex].SourceLineNumber} ({plan.StartIndex + 1}. od {lines.Count} za slanje{remaining}):",
            $"Starts at line {lines[plan.StartIndex].SourceLineNumber} ({plan.StartIndex + 1} of {lines.Count} to send{remaining}):"));
        for (int i = Math.Max(0, plan.StartIndex - contextLines); i <= Math.Min(lines.Count - 1, plan.StartIndex + contextLines); i++)
            text.AppendLine($"{(i == plan.StartIndex ? ">" : " ")} {lines[i].SourceLineNumber,6}: {lines[i].Text}");
        text.AppendLine();
        text.AppendLine(l.T("Prvo se šalje (laser ugašen, odlazak na početak linije):", "Sent first (laser off, moving to the start of the line):"));
        foreach (var line in plan.Preamble)
            text.AppendLine("  " + line);
        text.AppendLine(l.T("pa ostatak fajla:", "then the rest of the file:"));
        text.AppendLine("  " + plan.FirstLine);
        return text.ToString();
    }

    public static string JobState(JobState state, Language l) => state switch
    {
        GrblCore.JobState.Ready => l.T("Spreman", "Ready"),
        GrblCore.JobState.Running => l.T("Radi", "Running"),
        GrblCore.JobState.Paused => l.T("Pauziran", "Paused"),
        GrblCore.JobState.Break => l.T("Planirana pauza", "Scheduled break"),
        GrblCore.JobState.Completed => l.T("Završen", "Finished"),
        GrblCore.JobState.Stopped => l.T("Zaustavljen", "Stopped"),
        _ => l.T("Prekinut", "Aborted"),
    };

    // ----- Grbl's settings, errors and alarms -----

    /// <summary>What a Grbl setting does: 0.8 has its own numbering, 0.9 and 1.1 share theirs.</summary>
    public static string GrblSettingMeaning(GrblVersion? version, int number, Language l)
    {
        var table = version switch
        {
            { UsesSpindleSpeed: true } => Grbl11Settings,
            { Major: 0, Minor: 8 } => Grbl08Settings,
            _ => null,
        };
        return table is not null && table.TryGetValue(number, out var meaning) ? l.T(meaning.Bosnian, meaning.English) : "";
    }

    /// <summary>A Grbl error with its meaning when Grbl gave only a number (1.1: "error:20"); 0.8's text errors unchanged.</summary>
    public static string GrblError(string error, Language l) =>
        GrblResponse.TryGetErrorCode(error, out var code) && Grbl11Errors.TryGetValue(code, out var meaning)
            ? $"{error} ({l.T(meaning.Bosnian, meaning.English)})" : error;

    /// <summary>A line from Grbl, with the meaning added if it is a numbered alarm (1.1: "ALARM:3").</summary>
    public static string GrblAlarm(string line, Language l) =>
        GrblResponse.TryGetAlarmCode(line, out var code) && Grbl11Alarms.TryGetValue(code, out var meaning)
            ? $"{line} ({l.T(meaning.Bosnian, meaning.English)})" : line;

    static readonly Dictionary<int, (string Bosnian, string English)> Grbl11Settings = new()
    {
        [0] = ("Dužina step impulsa (µs)", "Step pulse length (µs)"),
        [1] = ("Koliko motori drže nakon kretanja (ms); 255 = drže stalno", "How long the motors hold after a move (ms); 255 = always"),
        [2] = ("Invertovanje step pinova (maska bitova)", "Step pins inverted (bit mask)"),
        [3] = ("Invertovanje smjera (maska bitova) - osa ide na krivu stranu", "Direction inverted (bit mask) - an axis moves the wrong way"),
        [4] = ("Invertovanje enable pina motora (0/1)", "Motor enable pin inverted (0/1)"),
        [5] = ("Invertovanje limit pinova (0/1)", "Limit pins inverted (0/1)"),
        [6] = ("Invertovanje probe pina (0/1)", "Probe pin inverted (0/1)"),
        [10] = ("Šta status prijavljuje (maska): 1 = apsolutna pozicija, 0 = radna", "What the status reports (mask): 1 = absolute position, 0 = work"),
        [11] = ("Odstupanje u ćoškovima (mm) - veće = brže kroz uglove, manje = preciznije", "Junction deviation (mm) - larger = faster through corners, smaller = more precise"),
        [12] = ("Tačnost luka G2/G3 (mm)", "Arc tolerance G2/G3 (mm)"),
        [13] = ("Izvještaj u inčima (0/1) - ostavi 0, ovaj program računa u mm", "Report in inches (0/1) - leave 0, this program works in mm"),
        [20] = ("Soft limiti (0/1) - traže homing", "Soft limits (0/1) - need homing"),
        [21] = ("Hard limit prekidači (0/1)", "Hard limit switches (0/1)"),
        [22] = ("Homing ciklus (0/1) - kad je 1, Grbl kreće u Alarm dok ne uradiš homing ili $X", "Homing cycle (0/1) - when 1, Grbl starts in Alarm until you home or $X"),
        [23] = ("Smjer hominga (maska bitova)", "Homing direction (bit mask)"),
        [24] = ("Homing: sporo, precizno (mm/min)", "Homing: slow, precise (mm/min)"),
        [25] = ("Homing: brzo traženje prekidača (mm/min)", "Homing: fast search for the switch (mm/min)"),
        [26] = ("Homing: debounce prekidača (ms)", "Homing: switch debounce (ms)"),
        [27] = ("Homing: odmicanje od prekidača (mm)", "Homing: pull-off from the switch (mm)"),
        [30] = ("Najveća snaga lasera / brzina vretena (S za punu snagu)", "Maximum laser power / spindle speed (S for full power)"),
        [31] = ("Najmanja snaga lasera / brzina vretena", "Minimum laser power / spindle speed"),
        [32] = ("Laserski način (0/1) - kad je 1, laser svijetli samo dok se kreće", "Laser mode (0/1) - when 1, the laser is lit only while moving"),
        [100] = ("Koraka po mm, osa X", "Steps per mm, X axis"),
        [101] = ("Koraka po mm, osa Y", "Steps per mm, Y axis"),
        [102] = ("Koraka po mm, osa Z", "Steps per mm, Z axis"),
        [110] = ("Najveća brzina X (mm/min) - i za prazni hod G0", "Maximum rate X (mm/min) - also for G0 rapids"),
        [111] = ("Najveća brzina Y (mm/min) - i za prazni hod G0", "Maximum rate Y (mm/min) - also for G0 rapids"),
        [112] = ("Najveća brzina Z (mm/min)", "Maximum rate Z (mm/min)"),
        [120] = ("Ubrzanje X (mm/s²)", "Acceleration X (mm/s²)"),
        [121] = ("Ubrzanje Y (mm/s²)", "Acceleration Y (mm/s²)"),
        [122] = ("Ubrzanje Z (mm/s²)", "Acceleration Z (mm/s²)"),
        [130] = ("Najveći hod X (mm), za soft limite", "Maximum travel X (mm), for soft limits"),
        [131] = ("Najveći hod Y (mm), za soft limite", "Maximum travel Y (mm), for soft limits"),
        [132] = ("Najveći hod Z (mm), za soft limite", "Maximum travel Z (mm), for soft limits"),
    };

    static readonly Dictionary<int, (string Bosnian, string English)> Grbl11Errors = new()
    {
        [1] = ("linija ne počinje slovom komande", "the line doesn't start with a command letter"),
        [2] = ("broj nije ispravno napisan", "a number is badly written"),
        [3] = ("nepoznata $ komanda", "unknown $ command"),
        [4] = ("negativna vrijednost", "negative value"),
        [5] = ("homing nije uključen ($22)", "homing isn't enabled ($22)"),
        [6] = ("step impuls mora biti bar 3 µs", "the step pulse must be at least 3 µs"),
        [7] = ("EEPROM se ne može pročitati, vraćene su osnovne postavke", "EEPROM can't be read, default settings restored"),
        [8] = ("$ komanda radi samo kad CNC miruje", "$ commands work only while the CNC is idle"),
        [9] = ("G-code je zaključan zbog alarma ili ručnog pomjeranja", "G-code is locked by an alarm or jogging"),
        [10] = ("soft limiti traže uključen homing", "soft limits need homing enabled"),
        [11] = ("linija je predugačka", "the line is too long"),
        [12] = ("prebrzo za ove korake po mm", "too fast for these steps per mm"),
        [13] = ("vrata su otvorena", "the door is open"),
        [14] = ("startna linija je predugačka", "the startup line is too long"),
        [15] = ("pomak bi izašao van radnog područja", "the move would leave the work area"),
        [16] = ("neispravna komanda za ručno pomjeranje", "invalid jog command"),
        [17] = ("laserski način traži PWM izlaz", "laser mode needs a PWM output"),
        [20] = ("komanda koju Grbl ne podržava", "a command Grbl doesn't support"),
        [21] = ("dvije komande iz iste grupe u liniji", "two commands of the same group in one line"),
        [22] = ("nedostaje brzina (F)", "feed rate (F) missing"),
        [23] = ("vrijednost mora biti cijeli broj", "the value must be a whole number"),
        [24] = ("dvije komande koriste ose u istoj liniji", "two commands use axes in the same line"),
        [25] = ("ista riječ dvaput u liniji", "the same word twice in a line"),
        [26] = ("komanda traži ose, a nema ih", "the command needs axes and has none"),
        [27] = ("broj linije (N) je prevelik", "line number (N) too large"),
        [28] = ("nedostaje vrijednost koju komanda traži", "a value the command needs is missing"),
        [29] = ("G59.x koordinatni sistemi nisu podržani", "G59.x coordinate systems aren't supported"),
        [30] = ("G53 radi samo sa G0 i G1", "G53 works only with G0 and G1"),
        [31] = ("ose u liniji koja ih ne koristi", "axes in a line that doesn't use them"),
        [32] = ("luk nema osa u svojoj ravni", "the arc has no axes in its plane"),
        [33] = ("luk ili pomak ne može doći do cilja", "the arc or move can't reach its target"),
        [34] = ("luk: radijus se ne slaže sa centrom", "arc: the radius doesn't match the centre"),
        [35] = ("luk nema I/J/K u svojoj ravni", "the arc has no I/J/K in its plane"),
        [36] = ("višak vrijednosti u liniji", "unused values in the line"),
        [37] = ("korekcija alata (G43.1) nije na osi Z", "tool offset (G43.1) isn't on the Z axis"),
        [38] = ("broj alata je prevelik", "tool number too large"),
    };

    static readonly Dictionary<int, (string Bosnian, string English)> Grbl11Alarms = new()
    {
        [1] = ("udaren limit prekidač, pozicija je izgubljena", "a limit switch was hit, the position is lost"),
        [2] = ("pomak bi izašao van radnog područja (soft limit)", "the move would leave the work area (soft limit)"),
        [3] = ("reset dok se CNC kretao, pozicija je možda izgubljena", "reset while the CNC was moving, the position may be lost"),
        [4] = ("sonda je već bila dodirnuta prije početka", "the probe was already touching before the start"),
        [5] = ("sonda nije dodirnula ništa", "the probe didn't touch anything"),
        [6] = ("homing prekinut resetom", "homing aborted by a reset"),
        [7] = ("homing prekinut otvaranjem vrata", "homing aborted by the door opening"),
        [8] = ("homing: prekidač i dalje pritisnut nakon odmicanja", "homing: the switch is still pressed after the pull-off"),
        [9] = ("homing: prekidač nije nađen", "homing: the switch wasn't found"),
    };

    static readonly Dictionary<int, (string Bosnian, string English)> Grbl08Settings = new()
    {
        [0] = ("Koraka po mm, osa X", "Steps per mm, X axis"),
        [1] = ("Koraka po mm, osa Y", "Steps per mm, Y axis"),
        [2] = ("Koraka po mm, osa Z", "Steps per mm, Z axis"),
        [3] = ("Dužina step impulsa (µs)", "Step pulse length (µs)"),
        [4] = ("Feed kad G-code nema F (mm/min)", "Feed when the G-code has no F (mm/min)"),
        [5] = ("Brzina praznog hoda G0 (mm/min) - i za ručno pomjeranje", "G0 rapid rate (mm/min) - also for jogging"),
        [6] = ("Invertovanje step/dir pinova (maska bitova)", "Step/dir pins inverted (bit mask)"),
        [7] = ("Koliko motori drže nakon kretanja (ms); 255 = drže stalno", "How long the motors hold after a move (ms); 255 = always"),
        [8] = ("Ubrzanje (mm/s²)", "Acceleration (mm/s²)"),
        [9] = ("Odstupanje u ćoškovima (mm) - veće = brže kroz uglove, manje = preciznije", "Junction deviation (mm) - larger = faster through corners, smaller = more precise"),
        [10] = ("Dužina segmenta luka G2/G3 (mm)", "Arc segment length G2/G3 (mm)"),
        [11] = ("Korekcija luka (svaki N-ti segment tačno)", "Arc correction (every Nth segment exact)"),
        [12] = ("Broj decimala u izvještajima", "Decimal places in reports"),
        [13] = ("Izvještaj u inčima (0/1) - ostavi 0, ovaj program računa u mm", "Report in inches (0/1) - leave 0, this program works in mm"),
        [14] = ("Automatski start (0/1) - ostavi 1, inače svaki posao čeka Nastavak", "Auto start (0/1) - leave 1, otherwise every job waits for Resume"),
        [15] = ("Invertovanje enable pina motora (0/1)", "Motor enable pin inverted (0/1)"),
        [16] = ("Hard limit prekidači (0/1)", "Hard limit switches (0/1)"),
        [17] = ("Homing ciklus (0/1) - kad je 1, Grbl kreće u Alarm dok ne uradiš homing ili $X", "Homing cycle (0/1) - when 1, Grbl starts in Alarm until you home or $X"),
        [18] = ("Smjer hominga (maska bitova)", "Homing direction (bit mask)"),
        [19] = ("Homing: sporo, precizno (mm/min)", "Homing: slow, precise (mm/min)"),
        [20] = ("Homing: brzo traženje prekidača (mm/min)", "Homing: fast search for the switch (mm/min)"),
        [21] = ("Homing: debounce prekidača (ms)", "Homing: switch debounce (ms)"),
        [22] = ("Homing: odmicanje od prekidača (mm)", "Homing: pull-off from the switch (mm)"),
    };
}
