using CncController.Remote;
using GrblCore;

namespace CncController.App;

/// <summary>
/// What the machine page asks of the session, whether it runs in a browser (through the website, <see cref="RemoteLink"/>)
/// or in the desktop window (<see cref="LocalPage"/>): its state, and its commands, checked against what the session
/// allows right now. One instance per session, shared by both.
/// Use it on the session's thread, like <see cref="MachineSession"/>.
/// </summary>
public sealed class PageApi
{
    const int MaxUploadNameLength = 100;
    const int MaxFeed = 10_000;
    static readonly TimeSpan GrblSettingsTimeout = TimeSpan.FromSeconds(20);
    static readonly TimeSpan PortListAge = TimeSpan.FromSeconds(5);
    /// <summary>The same limits as the desktop's window.</summary>
    const double MinCompressorPeriod = 0.5, MaxCompressorPeriod = 240;
    static readonly int[] Bauds = [9600, 19200, 38400, 57600, 115200];

    readonly MachineSession _session;
    readonly Action<Action> _post;
    readonly string _uploadFolder;

    // The open position question gets a new id each time, so a late answer can't answer the next one.
    PositionQuestion? _question;
    int _questionId;
    string? _alertText;
    DateTime? _alertTime;
    // Asking Windows for the ports on every snapshot (several a second) is wasteful; they rarely change.
    string[] _ports = [];
    DateTime _portsRead;

    /// <param name="post">Runs an action on the session's thread (like the session's own).</param>
    /// <param name="uploadFolder">Where files uploaded from the web page are saved before loading.</param>
    public PageApi(MachineSession session, Action<Action> post, string uploadFolder)
    {
        _session = session;
        _post = post;
        _uploadFolder = uploadFolder;
        _session.QuestionChanged += OnQuestionChanged;
        _session.Alerted += OnAlerted;
    }

    public MachineSession Session => _session;

    /// <summary>Something the snapshot shows changed outside the session's own events (ports, question, warning).</summary>
    public event Action? Changed;

    void OnQuestionChanged()
    {
        if (_session.PendingQuestion is { } question && !ReferenceEquals(question, _question))
        {
            _question = question;
            _questionId++;
        }
        Changed?.Invoke();
    }

    void OnAlerted(string text)
    {
        _alertText = text;
        _alertTime = DateTime.Now;
        Changed?.Invoke();
    }

    /// <summary>Runs <paramref name="func"/> on the session's thread and hands its result back.</summary>
    public Task<T> OnSessionThread<T>(Func<T> func)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _post(() =>
        {
            try
            {
                done.SetResult(func());
            }
            catch (Exception ex)
            {
                done.SetException(ex);
            }
        });
        return done.Task;
    }

    // ----- Commands -----

    /// <summary>
    /// A command from a page, checked against what the desktop window would allow right now (a page can show a state
    /// that is already gone).
    /// </summary>
    /// <param name="origin">For the log ("Sa weba"): what was done or refused is written there; null logs nothing,
    /// like a click in the desktop window.</param>
    public CommandReply Execute(RemoteCommand command, string? origin)
    {
        var s = _session;
        switch (command.Name)
        {
            case RemoteCommands.Pause: return Do(origin, T("Pauza", "Pause"), s.IsOpen, s.Pause);
            case RemoteCommands.Resume: return Do(origin, T("Nastavi", "Resume"), s.IsOpen, s.Resume);
            case RemoteCommands.Stop: return Do(origin, "Stop", s.JobActive, s.Stop);
            case RemoteCommands.Reset: return Do(origin, "Reset", s.IsOpen, s.Reset);
            case RemoteCommands.Start: return Do(origin, "Start", s.CanStart, () => s.StartJob());
            case RemoteCommands.Frame: return Do(origin, T("Okvir", "Frame"), s.CanFrame, s.Frame);
            case RemoteCommands.ResumeFrom: return ResumeFrom(origin, command.Line);
            case RemoteCommands.Jog: return Jog(origin, command.Axis, command.Distance);
            case RemoteCommands.Unlock: return Do(origin, T("Otključaj", "Unlock"), s.CanSendCommands, s.Unlock);
            case RemoteCommands.SetZero: return Do(origin, T("Postavi nulu", "Set zero"), s.CanSetPosition, s.SetZero);
            case RemoteCommands.RestorePosition: return Do(origin, T("Vrati zapamćenu poziciju", "Restore the remembered position"), s.CanRestorePosition, s.RestoreRememberedPosition);
            case RemoteCommands.GoToZero: return Do(origin, T("Idi na 0,0,0", "Go to 0,0,0"), s.CanMove, s.GoToZero);
            case RemoteCommands.Laser when command.Value is { } on:
                // On or off rather than a toggle, so a repeated click can't switch it back.
                return Do(origin, on ? T("Upali laser", "Laser on") : T("Ugasi laser", "Laser off"), s.CanSendCommands, () => { if (s.LaserOn != on) s.ToggleLaser(); });
            case RemoteCommands.AutoResume when command.Value is { } on:
                return Do(origin, T($"Sam nastavi nakon restarta {(on ? "uključeno" : "isključeno")}", $"Resume by itself after a restart {(on ? "on" : "off")}"),
                    true, () => { s.AutoResume = on; });
            case RemoteCommands.CompressorPause when command.Value is { } on:
                return Do(origin, T($"Planirana pauza {(on ? "uključena" : "isključena")}", $"Scheduled break {(on ? "on" : "off")}"),
                    true, () => { s.CompressorPause = on; });
            case RemoteCommands.Answer when command.Id is { } id && command.Value is { } restore:
                return Answer(origin, id, restore);
            case RemoteCommands.Send when command.Text is { } text && text.Trim().Length > 0:
                return Send(origin, text.Trim());
            case RemoteCommands.Connect when command.Text is { } port && Ports().Contains(port):
                return Do(origin, T($"Spoji {port}", $"Connect {port}"), !s.IsOpen, () => s.Connect(port));
            case RemoteCommands.CloseFile:
                return Do(origin, T("Zatvori fajl", "Close file"), s.CanCloseFile, s.CloseFile);
            case RemoteCommands.Disconnect:
                return Do(origin, T("Odspoji", "Disconnect"), s.IsOpen, s.Disconnect);
            case RemoteCommands.Baud when command.Number is { } baud && Bauds.Contains((int)baud):
                return Do(origin, T($"Brzina porta {(int)baud}", $"Port speed {(int)baud}"), !s.IsOpen, () => { s.Baud = (int)baud; });
            case RemoteCommands.LaserPower when command.Number is { } power && power == Math.Floor(power):
                return Do(origin, T($"Snaga lasera S{power}", $"Laser power S{power}"), s.UsesLaserPower, () => s.SetLaserPower((int)power));
            case RemoteCommands.Language when Languages.FromCode(command.Text) is { } language:
                // Logged in the new language, so the log shows the switch where it happened.
                s.Language = language;
                return Do(origin, T("Jezik: bosanski", "Language: English"), true, () => { });
            case RemoteCommands.Compressors:
                return SetCompressors(origin, command.Value, command.Text, command.Number);
            case RemoteCommands.Convert when command.Text is { } file && command.Feeds is { Length: > 0 } feeds:
                return Convert(origin, file, feeds);
            default:
                Note(origin, T($"neispravna komanda \"{command.Name}\", nije izvršena.", $"invalid command \"{command.Name}\", not done."));
                return new CommandReply(false, T("Neispravna komanda.", "Invalid command."));
        }
    }

    string T(string bosnian, string english) => _session.Language.T(bosnian, english);

    /// <summary>Writes to the log where the command came from and what it did; nothing for the desktop's own page.</summary>
    void Note(string? origin, string text)
    {
        if (origin is not null)
            _session.Log($"{origin switch { RemoteLink.Origin => T("Sa weba", "From the web"), LanServer.Origin => T("Iz lokalne mreže", "From the local network"), _ => origin }}: {text}");
    }

    CommandReply Do(string? origin, string what, bool allowed, Action action) => Do(origin, what, allowed, () => { action(); return true; });

    /// <param name="action">False if it wasn't done after all; the session logged why.</param>
    CommandReply Do(string? origin, string what, bool allowed, Func<bool> action)
    {
        if (!allowed)
            return Refused(origin, what);
        Note(origin, $"{what}.");
        return action() ? new CommandReply(true)
            : new CommandReply(false, T($"„{what}“ nije urađeno, razlog je u logu.", $"\"{what}\" was not done, the reason is in the log."));
    }

    CommandReply Refused(string? origin, string what)
    {
        Note(origin, T($"komanda \"{what}\" nije izvršena (nije dozvoljena sada).", $"command \"{what}\" not done (not allowed now)."));
        return new CommandReply(false, T($"„{what}“ sada nije dozvoljeno; stanje se u međuvremenu promijenilo.",
            $"\"{what}\" isn't allowed now; the state has changed in the meantime."));
    }

    CommandReply Jog(string? origin, string? axisName, double? distance)
    {
        // Only the steps the desktop offers: a broken or forged page must not send the machine 5 m away.
        if (!Enum.TryParse<Axis>(axisName, ignoreCase: false, out var axis) || !Enum.IsDefined(axis)
            || distance is not { } d || !GrblCore.Jog.Steps.Any(step => Math.Abs(Math.Abs(d) - step) < 1e-9))
        {
            Note(origin, T($"neispravan jog ({axisName} {distance}), nije izvršen.", $"invalid jog ({axisName} {distance}), not done."));
            return new CommandReply(false, T("Neispravna komanda.", "Invalid command."));
        }
        return Do(origin, FormattableString.Invariant($"Jog {axis}{d:+0.###;-0.###}"), _session.CanMove, () => _session.Jog(axis, d));
    }

    CommandReply ResumeFrom(string? origin, int? line)
    {
        var what = T("Nastavi od...", "Resume from...");
        if (line is not { } sourceLine)
            return new CommandReply(false, T("Neispravna komanda.", "Invalid command."));
        if (!_session.CanResumeFrom)
            return Refused(origin, what);
        var (plan, text) = _session.PlanResume(sourceLine);
        if (plan is null)
        {
            Note(origin, T($"Nastavi od linije {sourceLine} nije moguće. {text}", $"Resume from line {sourceLine} not possible. {text}"));
            return new CommandReply(false, text);
        }
        Note(origin, T($"Nastavi od linije {sourceLine}.", $"Resume from line {sourceLine}."));
        _session.StartJob(plan);
        return new CommandReply(true);
    }

    CommandReply Answer(string? origin, int id, bool restore)
    {
        if (_session.PendingQuestion is not { } question || id != _questionId)
            return new CommandReply(false, T("Na ovo pitanje je već odgovoreno.", "This question was already answered."));
        Note(origin, T($"vratiti zapamćenu poziciju? {(restore ? "Da" : "Ne")}.", $"restore the remembered position? {(restore ? "Yes" : "No")}."));
        _session.AnswerPositionQuestion(question, restore);
        return new CommandReply(true);
    }

    CommandReply Send(string? origin, string line)
    {
        if (!_session.CanSendCommands)
            return Refused(origin, line);
        Note(origin, T($"komanda {line}", $"command {line}"));
        // The session logs why a line wasn't sent.
        return _session.SendCommand(line) ? new CommandReply(true) : new CommandReply(false, T("Nije poslano, razlog je u logu.", "Not sent, the reason is in the log."));
    }

    /// <param name="set">False clears the rhythm.</param>
    /// <param name="time">A start, "HH:mm:ss" (or "HH:mm") today on this computer's clock.</param>
    CommandReply SetCompressors(string? origin, bool? set, string? time, double? minutes)
    {
        if (set == false)
            return Do(origin, T("Obriši raspored pauze", "Clear the break schedule"), true, () => _session.SetCompressors(null));
        if (!TimeSpan.TryParseExact(time, ["hh\\:mm\\:ss", "hh\\:mm"], System.Globalization.CultureInfo.InvariantCulture, out var timeOfDay)
            || timeOfDay >= TimeSpan.FromDays(1)
            || minutes is not { } period || period < MinCompressorPeriod || period > MaxCompressorPeriod)
        {
            Note(origin, T($"neispravan raspored pauze ({time}, {minutes} min), nije upisan.", $"invalid break schedule ({time}, {minutes} min), not set."));
            return new CommandReply(false, T($"Upiši vrijeme (npr. 14:05:30) i period od {MinCompressorPeriod} do {MaxCompressorPeriod} min.",
                $"Enter a time (e.g. 14:05:30) and a period of {MinCompressorPeriod} to {MaxCompressorPeriod} min."));
        }
        Note(origin, T("raspored pauze.", "break schedule."));
        _session.SetCompressors(new CompressorSchedule(DateTime.Today + timeOfDay, TimeSpan.FromMinutes(period)));
        return new CommandReply(true);
    }

    /// <summary>Converts an uploaded original ArtCAM file for the laser with these feeds, saves it next to it and loads it.</summary>
    CommandReply Convert(string? origin, string fileName, int[] feeds)
    {
        var name = UploadName(fileName);
        var what = T("Konverzija za laser", "Conversion for the laser");
        if (!_session.CanLoadFile)
            return Refused(origin, what);
        if (feeds.Any(f => f < 1 || f > MaxFeed))
            return new CommandReply(false, T($"Feed mora biti cijeli broj od 1 do {MaxFeed}.", $"The feed must be a whole number from 1 to {MaxFeed}."));
        var source = Path.Combine(_uploadFolder, name);
        string path;
        try
        {
            if (!File.Exists(source))
                return new CommandReply(false, T("Fajl više nije na računaru uz CNC; učitaj ga ponovo.", "The file is no longer on the CNC's computer; upload it again."));
            var lines = File.ReadAllLines(source);
            if (!ArtCamLaserConverter.NeedsConversion(lines))
                return new CommandReply(false, T("Ovaj fajl ne treba konverziju.", "This file doesn't need converting."));
            var converted = ArtCamLaserConverter.Convert(lines, feeds);
            var bytes = System.Text.Encoding.UTF8.GetBytes(string.Join(Environment.NewLine, converted) + Environment.NewLine);
            path = SaveUpload($"{Path.GetFileNameWithoutExtension(name)} - laser{Path.GetExtension(name)}", bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Note(origin, T($"konverzija fajla {name} nije uspjela: {ex.Message}", $"converting file {name} failed: {ex.Message}"));
            return new CommandReply(false, T("Konverzija nije uspjela: ", "Conversion failed: ") + ex.Message);
        }

        // Offered next time, like after converting on the desktop.
        _session.Settings.FirstToolpathFeed = feeds[0];
        if (feeds.Length > 1)
            _session.Settings.OtherToolpathsFeed = feeds[1];
        _session.SaveSettings();
        Note(origin, T($"konvertovano za laser ({feeds.Length} putanja, feed {string.Join(" / ", feeds)}) i spremljeno: {path}",
            $"converted for the laser ({feeds.Length} toolpaths, feed {string.Join(" / ", feeds)}) and saved: {path}"));
        return _session.LoadFile(path)
            ? new CommandReply(true)
            : new CommandReply(false, T("Fajl nije učitan, razlog je u logu.", "File not loaded, the reason is in the log."));
    }

    /// <summary>The computer's serial ports and the simulator, as offered on the desktop.</summary>
    public string[] Ports()
    {
        if (DateTime.UtcNow - _portsRead > PortListAge)
        {
            string[] ports = [.. System.IO.Ports.SerialPort.GetPortNames().Order(), .. MachineSession.SimulatorPortNames];
            _portsRead = DateTime.UtcNow;
            // A USB cable plugged in: the page should offer the port even while nothing else changes.
            bool changed = !ports.SequenceEqual(_ports);
            _ports = ports;
            if (changed)
                Changed?.Invoke();
        }
        return _ports;
    }

    /// <summary>"Nastavi od..." from <paramref name="line"/> (null: the suggested line); null without a program.</summary>
    public ResumePreview? PreviewResume(int? line)
    {
        if (_session.Program is not { Lines.Count: > 0 } program)
            return null;
        var (suggested, stoppedText) = _session.SuggestResume();
        int sourceLine = line ?? program.Lines[Math.Clamp(suggested, 0, program.Lines.Count - 1)].SourceLineNumber;
        var (plan, text) = _session.PlanResume(sourceLine);
        return new ResumePreview(sourceLine, program.Lines[^1].SourceLineNumber,
            stoppedText ?? T("Nema zapamćenog prekida za ovaj fajl - izaberi liniju sam.", "No interruption remembered for this file - choose the line yourself."),
            plan?.StartIndex ?? -1, text);
    }

    // ----- Grbl settings -----

    public Task<GrblSettingsReply> ReadGrblSettings(string? origin) =>
        GrblSettingsRequest(origin, T("čitam Grbl postavke.", "reading Grbl settings."), _session.ReadGrblSettings);

    public Task<GrblSettingsReply> WriteGrblSettings(GrblSettingValue[] values, string? origin) =>
        GrblSettingsRequest(origin, T("upis Grbl postavki.", "writing Grbl settings."),
            () => _session.WriteGrblSettings(values.DistinctBy(v => v.Number).ToDictionary(v => v.Number, v => v.Value)));

    /// <summary>
    /// Starts reading or writing Grbl's settings on the session's thread and answers once Grbl is done
    /// (<see cref="MachineSession.GrblSettingsDone"/>), or with why it didn't start.
    /// </summary>
    /// <param name="start">Returns why not, or null when started.</param>
    async Task<GrblSettingsReply> GrblSettingsRequest(string? origin, string logText, Func<string?> start)
    {
        var done = new TaskCompletionSource<GrblSettingsReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnDone(IReadOnlyList<GrblSetting>? settings, string? error)
        {
            _session.GrblSettingsDone -= OnDone;
            done.TrySetResult(settings is null ? new GrblSettingsReply(false, error, []) : SettingsReply(settings));
        }
        _post(() =>
        {
            Note(origin, logText);
            _session.GrblSettingsDone += OnDone;
            if (start() is { } error)
            {
                _session.GrblSettingsDone -= OnDone;
                Note(origin, T("  nije urađeno: ", "  not done: ") + error);
                done.TrySetResult(new GrblSettingsReply(false, error, []));
            }
        });
        try
        {
            // Reading 23 settings at 9600 baud takes well under a second; writing a few, a second or two.
            return await done.Task.WaitAsync(GrblSettingsTimeout);
        }
        catch (TimeoutException)
        {
            _post(() => _session.GrblSettingsDone -= OnDone);
            return new GrblSettingsReply(false, T("Grbl nije odgovorio na vrijeme.", "Grbl didn't answer in time."), []);
        }
    }

    GrblSettingsReply SettingsReply(IReadOnlyList<GrblSetting> settings) => new(true, null, [.. settings.Select(s =>
        new GrblSettingRow(s.Number, s.Value, s.Description, Texts.GrblSettingMeaning(_session.Version, s.Number, _session.Language), s.IsInteger))]);

    // ----- Files from a browser -----

    /// <summary>
    /// Saves a file uploaded from a browser in the upload folder and loads it, like opening it on the desktop.
    /// An original ArtCAM file is only saved: the page asks for the feeds, then converts it (<see cref="RemoteCommands.Convert"/>).
    /// </summary>
    public UploadReply LoadUploaded(UploadedFile file, string? origin)
    {
        var name = UploadName(file.Name);
        if (!_session.CanLoadFile)
            return new UploadReply(false, Refused(origin, T($"Učitaj {name}", $"Upload {name}")).Text);
        string path;
        string[] lines;
        try
        {
            path = SaveUpload(name, file.Content);
            lines = File.ReadAllLines(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Note(origin, T($"fajl {name} nije spremljen: {ex.Message}", $"file {name} not saved: {ex.Message}"));
            return new UploadReply(false, T("Fajl nije spremljen na računaru uz CNC: ", "The file wasn't saved on the CNC's computer: ") + ex.Message);
        }
        if (ArtCamLaserConverter.NeedsConversion(lines))
        {
            Note(origin, T($"fajl {name} je originalni ArtCAM fajl, čeka izbor feeda za konverziju.",
                $"file {name} is an original ArtCAM file, waiting for the feeds to convert it."));
            // A file with Z moves but no "M3 S" still converts as one toolpath, as on the desktop.
            var toolpaths = ArtCamLaserConverter.FindToolpaths(lines) is { Count: > 0 } found ? found : [new ArtCamToolpath(1, null, 0, 0)];
            var settings = _session.Settings;
            return new UploadReply(false, T("Originalni ArtCAM fajl: izaberi feed za svaku putanju pa konvertuj.",
                "Original ArtCAM file: choose a feed for each toolpath, then convert."), Path.GetFileName(path),
                [.. toolpaths.Select(t => new ArtCamToolpathInfo(t.Number, t.Tool, t.Contours, t.Passes,
                    t.Number == 1 ? settings.FirstToolpathFeed : settings.OtherToolpathsFeed))]);
        }
        Note(origin, T($"učitavam fajl {name} ({Math.Max(1, file.Content.Length / 1024)} KB), spremljen u {path}.",
            $"loading file {name} ({Math.Max(1, file.Content.Length / 1024)} KB), saved in {path}."));
        return _session.LoadFile(path)
            ? new UploadReply(true)
            : new UploadReply(false, T("Fajl nije učitan, razlog je u logu.", "File not loaded, the reason is in the log."));
    }

    /// <summary>A safe file name from what the browser sent (no folders, no invalid characters).</summary>
    static string UploadName(string name)
    {
        name = Path.GetFileName(name.Replace('\\', '/').Split('/')[^1]).Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        if (name.Length > MaxUploadNameLength)
            name = name[^MaxUploadNameLength..];
        return name.Trim('.', ' ').Length == 0 ? "web.tap" : name;
    }

    /// <summary>
    /// Saves under the file's own name; another file with that name gets "(2)", "(3)"..., so the remembered
    /// interrupted job never points at different contents. The same file uploaded again reuses its copy.
    /// </summary>
    string SaveUpload(string name, byte[] content)
    {
        Directory.CreateDirectory(_uploadFolder);
        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        for (int n = 1; ; n++)
        {
            var path = Path.Combine(_uploadFolder, n == 1 ? name : $"{stem} ({n}){extension}");
            if (!File.Exists(path))
            {
                File.WriteAllBytes(path, content);
                return path;
            }
            if (File.ReadAllBytes(path).AsSpan().SequenceEqual(content))
                return path;
        }
    }

    // ----- State for pages -----

    public MachineSnapshot BuildSnapshot()
    {
        var session = _session;
        var (connectionText, connectionTone) = Texts.Connection(session);
        var (rememberedText, rememberedTone) = Texts.Remembered(session);
        var status = session.LastStatus;
        var job = session.Job;
        var now = DateTime.Now;
        // Time left only while the job runs; the estimate counts from the lines Grbl has accepted.
        TimeSpan? remaining = job is { IsActive: true } && session.Estimate is { } estimate ? estimate.Remaining(job.CompletedCount) : null;
        return new MachineSnapshot
        {
            Time = DateTime.Now,
            Language = session.Language.Code(),
            ConnectionText = connectionText,
            ConnectionTone = connectionTone,
            StateText = session.StateText,
            StateTone = Texts.StateTone(session),
            Work = ToRemote(status?.WorkPosition),
            Machine = ToRemote(status?.MachinePosition),
            PositionConfirmed = !session.PositionWaiting,
            RememberedText = rememberedText,
            RememberedTone = rememberedTone,
            LaserOn = session.LaserOn,
            LaserPower = session.UsesLaserPower ? session.Settings.LaserPower : null,
            LaserPowerMax = session.UsesLaserPower ? session.LaserPowerMax : null,
            FileName = session.ProgramPath is { } path ? Path.GetFileName(path) : null,
            FileInfo = Texts.FileInfo(session),
            Completed = job?.CompletedCount ?? 0,
            Total = job?.TotalCount ?? session.Program?.Lines.Count ?? 0,
            JobActive = session.JobActive,
            JobState = job is null ? null : Texts.JobState(job.State, session.Language),
            ProgressText = Texts.Progress(session),
            RemainingText = remaining is { } left ? Texts.Duration(left) : null,
            FinishText = remaining is { } untilEnd ? (now + untilEnd).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture) : null,
            ElapsedText = job is not null && session.JobStartedAt is { } started ? Texts.Duration(now - started) : null,
            JobStart = job is null ? null : session.JobStartedAt,
            JobEnd = remaining is { } toEnd ? now + toEnd : null,
            JobEvents = job is null ? [] : [.. session.JobEvents.Select(e => new JobEventInfo(
                e.Time, e.Kind == JobEventKind.CompressorBreak ? "break" : "restart", e.Line, e.Until))],
            CurrentLine = Texts.CurrentLine(session),
            CanPause = session.IsOpen,
            CanResume = session.IsOpen,
            CanStop = session.JobActive,
            CanReset = session.IsOpen,
            Alarm = session.IsOpen && session.InAlarm,
            CanStart = session.CanStart,
            CanFrame = session.CanFrame,
            CanResumeFrom = session.CanResumeFrom,
            CanLoadFile = session.CanLoadFile,
            CanCloseFile = session.CanCloseFile,
            CanMove = session.CanMove,
            CanSetPosition = session.CanSetPosition,
            CanRestorePosition = session.CanRestorePosition,
            CanSendCommands = session.CanSendCommands,
            CanGrblSettings = session.CanGrblSettings,
            CanConnect = !session.IsOpen,
            CanDisconnect = session.IsOpen,
            Ports = Ports(),
            PortName = session.PortName,
            Baud = session.Baud,
            BaudRates = Bauds,
            CompressorStart = session.Compressors?.KnownStart.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
            CompressorPeriodMinutes = (session.Compressors?.Period ?? CompressorSchedule.DefaultPeriod).TotalMinutes,
            JogSteps = [.. GrblCore.Jog.Steps],
            AutoResume = session.AutoResume,
            CompressorPause = session.CompressorPause,
            CompressorText = Texts.Compressors(session, DateTime.Now),
            QuestionId = session.PendingQuestion is null ? null : _questionId,
            QuestionText = session.PendingQuestion is { } question ? Texts.Question(question, session.Language) : null,
            AlertText = _alertText,
            AlertTime = _alertTime,
        };
    }

    static RemotePosition? ToRemote(Position? p) => p is { } q ? new RemotePosition(q.X, q.Y, q.Z) : null;

    public ToolpathData? BuildToolpath()
    {
        if (_session.Toolpath is not { } toolpath)
            return null;
        var segments = toolpath.Segments;
        var coordinates = new double[segments.Count * 4];
        var lines = new int[segments.Count];
        var kinds = new int[segments.Count];
        for (int i = 0; i < segments.Count; i++)
        {
            var s = segments[i];
            coordinates[4 * i] = Math.Round(s.X1, 3);
            coordinates[4 * i + 1] = Math.Round(s.Y1, 3);
            coordinates[4 * i + 2] = Math.Round(s.X2, 3);
            coordinates[4 * i + 3] = Math.Round(s.Y2, 3);
            lines[i] = s.LineIndex;
            kinds[i] = s.Kind switch { SegmentKind.Rapid => 0, SegmentKind.Move => 1, _ => 2 };
        }
        return new ToolpathData(Path.GetFileName(_session.ProgramPath ?? ""), coordinates, lines, kinds);
    }
}
