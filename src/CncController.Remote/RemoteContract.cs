namespace CncController.Remote;

/// <summary>
/// How the desktop program and the website talk. The desktop connects to <see cref="DesktopHub"/> (SignalR) with
/// <see cref="DesktopKeyHeader"/>; browsers connect to <see cref="BrowserHub"/> after logging in.
/// The website only relays: the desktop sends its state, browsers send commands, the desktop carries them out.
/// </summary>
public static class RemoteContract
{
    public const string DesktopHub = "/hubs/desktop";
    public const string BrowserHub = "/hubs/browser";
    public const string DesktopKeyHeader = "X-Desktop-Key";

    /// <summary>Largest message the desktop may send (a toolpath of a big file).</summary>
    public const long MaxDesktopMessageBytes = 32 * 1024 * 1024;

    // Desktop -> website (hub methods on DesktopHub).
    public const string SendSnapshot = "Snapshot";
    public const string SendLog = "Log";
    public const string SendToolpath = "Toolpath";
    /// <summary>A <see cref="MachineNotification"/> for the phones that turned notifications on.</summary>
    public const string SendNotification = "Notify";

    // Website -> desktop; the desktop answers each one (SignalR client results).
    /// <summary>A <see cref="RemoteCommand"/>; answered with a <see cref="CommandReply"/>.</summary>
    public const string Command = "Command";
    /// <summary>"Nastavi od...": a line number in the file (null = the suggested one); answered with a <see cref="ResumePreview"/>.</summary>
    public const string PreviewResume = "PreviewResume";
    /// <summary>An <see cref="UploadedFile"/> to load; answered with an <see cref="UploadReply"/>.</summary>
    public const string Upload = "Upload";
    /// <summary>Reads Grbl's settings ('$$'); answered with a <see cref="GrblSettingsReply"/> once Grbl has sent them.</summary>
    public const string ReadGrblSettings = "ReadGrblSettings";
    /// <summary><see cref="GrblSettingValue"/>s to store; answered with a <see cref="GrblSettingsReply"/> read back after writing.</summary>
    public const string WriteGrblSettings = "WriteGrblSettings";

    /// <summary>Largest G-code file a browser may upload.</summary>
    public const long MaxUploadBytes = 20 * 1024 * 1024;

    // Website -> browser.
    public const string DesktopStatus = "Desktop";
}

/// <summary>Commands a browser may send; the desktop checks each one against what is allowed at that moment.</summary>
public static class RemoteCommands
{
    public const string Pause = "Pause";
    public const string Resume = "Resume";
    public const string Stop = "Stop";
    public const string Reset = "Reset";
    public const string Start = "Start";
    public const string Frame = "Frame";
    /// <summary>Continue the loaded program from <see cref="RemoteCommand.Line"/> (a line number in the file).</summary>
    public const string ResumeFrom = "ResumeFrom";
    /// <summary>Move <see cref="RemoteCommand.Axis"/> by <see cref="RemoteCommand.Distance"/> (a jog step, signed).</summary>
    public const string Jog = "Jog";
    /// <summary>Laser on or off (<see cref="RemoteCommand.Value"/>).</summary>
    public const string Laser = "Laser";
    public const string Unlock = "Unlock";
    public const string SetZero = "SetZero";
    public const string RestorePosition = "RestorePosition";
    public const string GoToZero = "GoToZero";
    /// <summary>The checkboxes; on or off (<see cref="RemoteCommand.Value"/>).</summary>
    public const string AutoResume = "AutoResume";
    public const string CompressorPause = "CompressorPause";
    /// <summary>Answers the position question <see cref="RemoteCommand.Id"/>: restore the remembered position or not (<see cref="RemoteCommand.Value"/>).</summary>
    public const string Answer = "Answer";
    /// <summary>A line typed by the user (<see cref="RemoteCommand.Text"/>).</summary>
    public const string Send = "Send";
    /// <summary>Opens the port <see cref="RemoteCommand.Text"/> (one of <see cref="MachineSnapshot.Ports"/>).</summary>
    public const string Connect = "Connect";
    public const string Disconnect = "Disconnect";
    /// <summary>
    /// The compressor rhythm: a start at <see cref="RemoteCommand.Text"/> ("HH:mm:ss" today, the desktop's clock),
    /// every <see cref="RemoteCommand.Number"/> minutes; <see cref="RemoteCommand.Value"/> false clears it.
    /// </summary>
    public const string Compressors = "Compressors";
    /// <summary>Converts the uploaded ArtCAM file <see cref="RemoteCommand.Text"/> for the laser with <see cref="RemoteCommand.Feeds"/> and loads it.</summary>
    public const string Convert = "Convert";
    /// <summary>Closes the loaded program (not during a job).</summary>
    public const string CloseFile = "CloseFile";
    /// <summary>Port speed for the next connect (<see cref="RemoteCommand.Number"/>, one of <see cref="MachineSnapshot.BaudRates"/>).</summary>
    public const string Baud = "Baud";
    /// <summary>Laser power S for Grbl 0.9+ (<see cref="RemoteCommand.Number"/>).</summary>
    public const string LaserPower = "LaserPower";
    /// <summary>The program's language, "bs" or "en" (<see cref="RemoteCommand.Text"/>), for the desktop and every page.</summary>
    public const string Language = "Language";

    public static readonly IReadOnlyList<string> All =
    [
        Pause, Resume, Stop, Reset, Start, Frame, ResumeFrom, Jog, Laser, Unlock,
        SetZero, RestorePosition, GoToZero, AutoResume, CompressorPause, Answer, Send,
        Connect, Disconnect, Compressors, Convert, CloseFile, Baud, LaserPower, Language,
    ];
}

/// <summary>A command from a browser; which fields matter depends on <see cref="Name"/> (see <see cref="RemoteCommands"/>).</summary>
public sealed record RemoteCommand
{
    public string Name { get; init; } = "";
    public string? Axis { get; init; }
    public double? Distance { get; init; }
    public int? Line { get; init; }
    public bool? Value { get; init; }
    public int? Id { get; init; }
    public string? Text { get; init; }
    public double? Number { get; init; }
    public int[]? Feeds { get; init; }
}

/// <summary>The desktop's answer to a command: done, or why not (in the program's language, ready to show).</summary>
public sealed record CommandReply(bool Done, string? Text = null);

/// <summary>
/// What "Nastavi od..." would do from <see cref="Line"/>: it starts at program line <see cref="StartIndex"/>
/// (-1 if it can't), <see cref="Text"/> describes it or says why not.
/// </summary>
public sealed record ResumePreview(int Line, int MaxLine, string? StoppedText, int StartIndex, string Text);

/// <summary>A G-code file uploaded from a browser.</summary>
public sealed record UploadedFile(string Name, byte[] Content);

/// <summary>
/// The desktop's answer to an upload. An original ArtCAM file isn't loaded: it comes back as <see cref="FileName"/>
/// with its <see cref="Toolpaths"/>, to be converted with a feed per toolpath (<see cref="RemoteCommands.Convert"/>).
/// </summary>
public sealed record UploadReply(bool Done, string? Text = null, string? FileName = null, ArtCamToolpathInfo[]? Toolpaths = null);

/// <summary>A toolpath of an ArtCAM file, with the feed offered for it (the last one used, mm/min).</summary>
public sealed record ArtCamToolpathInfo(int Number, string? Tool, int Contours, int Passes, int Feed);

/// <summary>One of Grbl's settings: as Grbl has it, its own short text, and what it does (in the program's language).</summary>
public sealed record GrblSettingRow(int Number, string Value, string? GrblText, string Meaning, bool IsInteger);

/// <summary>Grbl's settings as read (after writing, as read back), or why not.</summary>
public sealed record GrblSettingsReply(bool Done, string? Text, GrblSettingRow[] Settings);

/// <summary>A new value for a setting.</summary>
public sealed record GrblSettingValue(int Number, string Value);

/// <summary>Colour of a status text, so every screen shows it the same way.</summary>
public enum Tone
{
    Muted,
    Grey,
    Green,
    Blue,
    Orange,
    Red,
}

public sealed record RemotePosition(double X, double Y, double Z);

/// <summary>Everything a browser shows, as the desktop sees it right now. Texts are ready to show, in <see cref="Language"/>.</summary>
public sealed record MachineSnapshot
{
    public DateTime Time { get; init; }

    /// <summary>The program's language, "bs" or "en": the page shows its own texts in it too.</summary>
    public string Language { get; init; } = "bs";

    public string ConnectionText { get; init; } = "";
    public Tone ConnectionTone { get; init; }
    public string StateText { get; init; } = "";
    public Tone StateTone { get; init; }

    public RemotePosition? Work { get; init; }
    public RemotePosition? Machine { get; init; }
    public bool PositionConfirmed { get; init; }
    public string RememberedText { get; init; } = "";
    public Tone RememberedTone { get; init; }

    public bool LaserOn { get; init; }

    /// <summary>Laser power S sent with M3 and before a job; null when Grbl ignores it (0.8) or isn't connected.</summary>
    public int? LaserPower { get; init; }
    /// <summary>Full power, Grbl's $30, once the settings were read; null while unknown.</summary>
    public int? LaserPowerMax { get; init; }

    public string? FileName { get; init; }
    public string? FileInfo { get; init; }

    /// <summary>Lines Grbl accepted so far; the toolpath is drawn as done up to this line index.</summary>
    public int Completed { get; init; }
    public int Total { get; init; }
    public bool JobActive { get; init; }
    public string? JobState { get; init; }
    public string? ProgressText { get; init; }
    /// <summary>While a job runs: time left ("≈ 37 min"), when it should be done ("03:42"), and how long it runs since its start.</summary>
    public string? RemainingText { get; init; }
    public string? FinishText { get; init; }
    public string? ElapsedText { get; init; }
    /// <summary>The job's start and its expected end (or its end), for the timeline; null without a job.</summary>
    public DateTime? JobStart { get; init; }
    public DateTime? JobEnd { get; init; }
    /// <summary>Compressor breaks and restarts since the start, oldest first.</summary>
    public JobEventInfo[] JobEvents { get; init; } = [];
    public string? CurrentLine { get; init; }

    // What may be done now, the same as on the desktop.
    public bool CanPause { get; init; }
    public bool CanResume { get; init; }
    public bool CanStop { get; init; }
    public bool CanReset { get; init; }
    /// <summary>Grbl is in an alarm: only then is "Otključaj" shown.</summary>
    public bool Alarm { get; init; }
    public bool CanStart { get; init; }
    public bool CanFrame { get; init; }
    public bool CanResumeFrom { get; init; }
    public bool CanLoadFile { get; init; }
    public bool CanCloseFile { get; init; }
    /// <summary>Jog and going to zero.</summary>
    public bool CanMove { get; init; }
    public bool CanSetPosition { get; init; }
    public bool CanRestorePosition { get; init; }
    /// <summary>Laser, unlock and typed lines.</summary>
    public bool CanSendCommands { get; init; }
    /// <summary>Reading and writing Grbl's settings: connected, no job, the machine standing still.</summary>
    public bool CanGrblSettings { get; init; }
    public bool CanConnect { get; init; }
    public bool CanDisconnect { get; init; }

    /// <summary>Ports on the desktop's computer, the simulator last.</summary>
    public string[] Ports { get; init; } = [];
    /// <summary>The port connected to (or last used).</summary>
    public string? PortName { get; init; }
    public int Baud { get; init; }
    public int[] BaudRates { get; init; } = [];

    /// <summary>Jog steps offered, mm.</summary>
    public double[] JogSteps { get; init; } = [];

    public bool AutoResume { get; init; }
    public bool CompressorPause { get; init; }
    public string CompressorText { get; init; } = "";
    /// <summary>The rhythm: a known start ("HH:mm:ss", the desktop's clock) and the period; null while unknown.</summary>
    public string? CompressorStart { get; init; }
    public double CompressorPeriodMinutes { get; init; }

    /// <summary>The open position question (answered by its id), or null.</summary>
    public int? QuestionId { get; init; }
    public string? QuestionText { get; init; }

    /// <summary>The last warning the desktop showed in a message box (a rejected line, a file that can't run), and when.</summary>
    public string? AlertText { get; init; }
    public DateTime? AlertTime { get; init; }
}

public sealed record LogLine(DateTime Time, string Text);

/// <summary>A compressor break ("break", until when) or a restart ("restart") during the job, and the lines done by then.</summary>
public sealed record JobEventInfo(DateTime Time, string Kind, int Line, DateTime? Until);

/// <summary>Something to tell the phone (job finished, alarm...), as the desktop's session raised it, in its language.</summary>
public sealed record MachineNotification(string Title, string Text, DateTime Time);

/// <summary>
/// The loaded program's moves, compact: segment i goes from (Coordinates[4i], [4i+1]) to ([4i+2], [4i+3]),
/// belongs to program line Lines[i] and is a move of kind Kinds[i] (0 rapid, 1 move without laser, 2 cut).
/// </summary>
public sealed record ToolpathData(string FileName, double[] Coordinates, int[] Lines, int[] Kinds);

/// <summary>Whether the desktop program is connected to the website.</summary>
public sealed record DesktopStatus(bool Online, DateTime? LastSeen);
