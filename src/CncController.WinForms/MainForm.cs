using CncController.App;
using CncController.Remote;
using GrblCore;

namespace CncController.WinForms;

/// <summary>
/// The desktop window: shows the machine page (MainForm.Page.cs) and adds what only a window has:
/// its own dialogs, the simulator's window, remote access and the local network, updates.
/// The logic (connection, jobs, position, automatic resume, scheduled break) lives in the session.
/// </summary>
public partial class MainForm : Form
{
    readonly MachineSession _session;
    readonly PageApi _api;
    readonly RemoteLink _remote;
    readonly LanServer _lan;
    readonly AppUpdates _updates;
    SimulatorForm? _simulatorForm;
    readonly System.Windows.Forms.Timer _jobTimer;
    readonly Queue<LogLine> _recentLog = new();

    AppSettings Settings => _session.Settings;

    string T(string bosnian, string english) => _session.Language.T(bosnian, english);

    public MainForm()
    {
        InitializeComponent();
        // The program's own icon (app.ico), in the title bar and on the taskbar.
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        _session = new MachineSession(
            AppSettings.Load(AppData.SettingsFile), PositionMemory.Load(AppData.PositionFile), AppData.LogFolder, OnUi);
        // The page shows the log, warnings and questions itself; it loads after the first lines, which it gets from here.
        _session.Logged += (time, text) =>
        {
            _recentLog.Enqueue(new LogLine(time, text));
            while (_recentLog.Count > PagePublisher.LogBufferSize)
                _recentLog.Dequeue();
        };
        _session.Changed += ShowSimulatorIfConnected;
        _api = new PageApi(_session, OnUi, AppData.UploadFolder);
        _remote = new RemoteLink(_api, OnUi);
        _remote.StateChanged += ShowPageHost;
        RemoteAccessAvailable = BuiltWithWebsite || (Settings.RemoteUrl is { Length: > 0 } && Settings.RemoteKey is { Length: > 0 });
        _lan = new LanServer(_api, OnUi, Path.Combine(AppContext.BaseDirectory, "ui"));
        _lan.StateChanged += ShowPageHost;
        _updates = new AppUpdates(Settings.UpdateUrl);
        _updates.Changed += ShowPageHost;
        // The language changes the bar's texts too.
        _session.Changed += ShowPageHost;

        _jobTimer = new System.Windows.Forms.Timer(components!) { Interval = 250 };
        _jobTimer.Tick += (_, _) => _session.Tick();
        _jobTimer.Start();

        _session.Log(T("Program pokrenut.", "Program started."));
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        RestoreWindow();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // Before anything else, once: the terms of use and the laser safety warning.
        if (!AcceptTerms())
            return;
        // After a power cut or closing the program mid-job, have the job ready to continue.
        _session.LoadInterruptedJob();
        StartRemote();
        _ = StartLanAsync();
        _ = ShowPageAsync();
        StartUpdateChecks();
    }

    // ----- Remote access -----

#if REMOTE_ACCESS
    /// <summary>Built next to the owner's website (src/CncController.Web): remote access can be set up from scratch.</summary>
    const bool BuiltWithWebsite = true;
#else
    const bool BuiltWithWebsite = false;
#endif

    /// <summary>
    /// Remote access needs the owner's own website. The public build (GitHub releases) offers it only where it is
    /// already set up (address and key in settings.json: the owner's computer); everyone else never sees it.
    /// Decided once at start, so clearing the key in the dialog doesn't hide the dialog.
    /// </summary>
    readonly bool RemoteAccessAvailable;

    /// <summary>Connects to the website if remote access is on and set up; otherwise makes sure it is off.</summary>
    void StartRemote()
    {
        if (RemoteAccessAvailable && Settings.RemoteEnabled && Settings.RemoteUrl is { Length: > 0 } url && Settings.RemoteKey is { Length: > 0 } key)
            _remote.Start(url, key);
        else
            _remote.Stop();
        ShowPageHost();
    }

    void ShowRemoteSettings()
    {
        using var dialog = new RemoteDialog(Settings, _remote.StateText);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        bool changed = dialog.RemoteEnabled != Settings.RemoteEnabled || dialog.Url != Settings.RemoteUrl || dialog.Key != Settings.RemoteKey;
        if (!changed)
            return;
        Settings.RemoteEnabled = dialog.RemoteEnabled;
        Settings.RemoteUrl = dialog.Url;
        Settings.RemoteKey = dialog.Key;
        _session.SaveSettings();
        _session.Log(dialog.RemoteEnabled ? T("Daljinski pristup uključen: ", "Remote access on: ") + dialog.Url : T("Daljinski pristup isključen.", "Remote access off."));
        StartRemote();
    }

    // ----- Simulator -----

    /// <summary>Connected to a simulator (from the page here, on the phone or the website): its window with the fault buttons.</summary>
    void ShowSimulatorIfConnected()
    {
        if (_session.IsOpen && MachineSession.IsSimulatorPort(_session.PortName) && _simulatorForm?.Simulator != _session.Simulator)
            ShowSimulatorForm();
    }

    void ShowSimulatorForm()
    {
        // Switching between "Simulator" and "Simulator 1.1" replaces the simulator; the old window would drive the old one.
        if (_simulatorForm is not null && _simulatorForm.Simulator != _session.Simulator)
        {
            _simulatorForm.Dispose();
            _simulatorForm = null;
        }
        if (_simulatorForm is null)
        {
            _simulatorForm = new SimulatorForm(_session.Simulator!, _session.Language) { Owner = this };
            _simulatorForm.Location = new Point(Right - _simulatorForm.Width - 30, Top + 90);
        }
        _simulatorForm.Show();
    }

    // ----- Files -----

    string GCodeFilter => "G-code (*.tap;*.nc;*.gcode;*.ngc;*.txt)|*.tap;*.nc;*.gcode;*.ngc;*.txt|" + T("Svi fajlovi", "All files") + " (*.*)|*.*";

    /// <summary>The page's "Otvori fajl...": the system's file dialog, in the last folder.</summary>
    void OpenFile()
    {
        if (!_session.CanLoadFile)
            return;
        using var dialog = new OpenFileDialog { Title = T("Otvori G-code fajl", "Open a G-code file"), Filter = GCodeFilter };
        if (Directory.Exists(Settings.LastFolder))
            dialog.InitialDirectory = Settings.LastFolder;
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        Settings.LastFolder = Path.GetDirectoryName(dialog.FileName);
        _session.SaveSettings();
        LoadFile(dialog.FileName);
    }

    /// <summary>Loads a G-code file, converting an original ArtCAM file for the laser first.</summary>
    public void LoadFile(string path)
    {
        try
        {
            var lines = File.ReadAllLines(path);
            if (ArtCamLaserConverter.NeedsConversion(lines))
            {
                var converted = ConvertForLaser(path, lines);
                if (converted is null)
                    return;
                path = converted;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, T("Greška s fajlom:", "File error:") + "\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        _session.LoadFile(path);
    }

    /// <summary>Asks for feed rates, converts an ArtCAM file for the laser and saves it. Returns the saved path, or null if cancelled.</summary>
    string? ConvertForLaser(string path, string[] lines)
    {
        var toolpaths = ArtCamLaserConverter.FindToolpaths(lines);
        IReadOnlyList<int> feeds;
        using (var convertDialog = new LaserConvertDialog(Path.GetFileName(path), toolpaths, Settings.FirstToolpathFeed, Settings.OtherToolpathsFeed, _session.Language))
        {
            if (convertDialog.ShowDialog(this) != DialogResult.OK)
                return null;
            feeds = convertDialog.FeedRates;
        }
        Settings.FirstToolpathFeed = feeds[0];
        if (feeds.Count > 1)
            Settings.OtherToolpathsFeed = feeds[1];
        _session.SaveSettings();

        using var saveDialog = new SaveFileDialog
        {
            Title = T("Spremi fajl za laser", "Save the file for the laser"),
            Filter = GCodeFilter,
            InitialDirectory = Path.GetDirectoryName(path),
            FileName = $"{Path.GetFileNameWithoutExtension(path)} - laser{Path.GetExtension(path)}",
        };
        if (saveDialog.ShowDialog(this) != DialogResult.OK)
            return null;

        File.WriteAllLines(saveDialog.FileName, ArtCamLaserConverter.Convert(lines, feeds));
        _session.Log(T($"Konvertovano za laser ({toolpaths.Count} putanja, feed {string.Join(" / ", feeds)}) i spremljeno: {saveDialog.FileName}",
            $"Converted for the laser ({toolpaths.Count} toolpaths, feed {string.Join(" / ", feeds)}) and saved: {saveDialog.FileName}"));
        return saveDialog.FileName;
    }

    // ----- Closing and the window's place -----

    void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_session.JobActive && !Confirm(T("Posao je u toku. Zatvoriti program i prekinuti slanje?", "A job is running. Close the program and stop sending?")))
        {
            e.Cancel = true;
            return;
        }
        SaveOnExit();
    }

    /// <summary>What closing the program does, also before an update restarts it: the log, the network page, the window's place.</summary>
    void SaveOnExit()
    {
        _session.Log(T("Program zatvoren.", "Program closed."));
        StopLan();
        var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        Settings.Window = new WindowPlacement(bounds.X, bounds.Y, bounds.Width, bounds.Height, WindowState == FormWindowState.Maximized);
        _session.SaveSettings();
    }

    /// <summary>Opens where it was last closed, unless that spot is off every screen now.</summary>
    void RestoreWindow()
    {
        if (Settings.Window is not { } w)
            return;
        var bounds = new Rectangle(w.X, w.Y, Math.Max(w.Width, MinimumSize.Width), Math.Max(w.Height, MinimumSize.Height));
        if (!Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(bounds)))
            return;
        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
        if (w.Maximized)
            WindowState = FormWindowState.Maximized;
    }

    bool Confirm(string question) =>
        MessageBox.Show(this, question, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    /// <summary>The session's thread is the UI thread: GrblCore's events are passed here from its background threads.</summary>
    void OnUi(Action action)
    {
        if (IsDisposed || !IsHandleCreated)
            return;
        try
        {
            BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
            // Form is closing.
        }
    }
}
