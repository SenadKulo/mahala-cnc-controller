using System.IO.Ports;
using CncController.App;
using GrblCore;

namespace CncController.WinForms;

/// <summary>
/// The desktop window: shows the <see cref="MachineSession"/> and passes clicks and keys to it.
/// The logic (connection, jobs, position, automatic resume, compressor pause) lives in the session.
/// </summary>
public partial class MainForm : Form
{
    const string DefaultPort = "COM4";
    const string DefaultBaud = "9600";
    const int MaxLogLength = 100_000;

    readonly MachineSession _session;
    readonly PageApi _api;
    readonly RemoteLink _remote;
    readonly LanServer _lan;
    readonly AppUpdates _updates;
    SimulatorForm? _simulatorForm;
    QuestionDialog? _questionDialog;
    bool? _unlockShown;
    readonly System.Windows.Forms.Timer _jobTimer;

    AppSettings Settings => _session.Settings;

    string T(string bosnian, string english) => _session.Language.T(bosnian, english);

    public MainForm()
    {
        InitializeComponent();
        // The program's own icon (app.ico), in the title bar and on the taskbar.
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        _session = new MachineSession(
            AppSettings.Load(AppData.SettingsFile), PositionMemory.Load(AppData.PositionFile), AppData.LogFolder, OnUi);
        _session.Logged += AppendLog;
        // The page shows warnings itself (red box at the top).
        _session.Alerted += text => { if (!PageShown) MessageBox.Show(this, text, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); };
        _session.Changed += UpdateUi;
        _session.StatusReceived += ShowStatus;
        _session.ProgramChanged += ShowProgram;
        _session.ProgressChanged += ShowProgress;
        _session.QuestionChanged += OnQuestionChanged;
        _api = new PageApi(_session, OnUi, AppData.UploadFolder);
        _remote = new RemoteLink(_api, OnUi);
        _remote.StateChanged += ShowRemote;
        lblRemote.Visible = RemoteAccessAvailable;
        _lan = new LanServer(_api, OnUi, Path.Combine(AppContext.BaseDirectory, "ui"));
        _lan.StateChanged += ShowPageHost;
        _updates = new AppUpdates(Settings.UpdateUrl);
        _updates.Changed += ShowPageHost;
        // The language changes the bar's texts too.
        _session.Changed += ShowPageHost;

        Ui.FlattenButtons(this);
        Ui.Accent(btnConnect, Ui.Blue);
        Ui.Accent(btnStart, Ui.Green);
        Ui.Accent(btnStop, Ui.Red);
        Ui.Accent(btnPause, Ui.Orange);
        Ui.Accent(btnReset, Ui.Red);
        Ui.Accent(btnUnlock, Ui.Orange);

        jogControl.Step = Settings.JogStep;
        jogControl.StepChanged += step => { Settings.JogStep = step; _session.SaveSettings(); };
        jogControl.JogRequested += _session.Jog;

        RefreshPorts();
        cmbBaud.SelectedItem = DefaultBaud;
        cmbBaud.SelectedIndexChanged += (_, _) => _session.Baud = int.Parse((string)cmbBaud.SelectedItem!);

        chkAutoResume.Checked = _session.AutoResume;
        chkCompressorPause.Checked = _session.CompressorPause;
        chkAutoResume.CheckedChanged += (_, _) => _session.AutoResume = chkAutoResume.Checked;
        chkCompressorPause.CheckedChanged += (_, _) => _session.CompressorPause = chkCompressorPause.Checked;

        _jobTimer = new System.Windows.Forms.Timer(components!) { Interval = 250 };
        _jobTimer.Tick += (_, _) => OnJobTimer();
        _jobTimer.Start();

        UpdateUi();
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
        if (!Settings.ClassicView)
            _ = ShowPageAsync();
        StartUpdateChecks();
    }

    // ----- Showing the session -----

    void ShowConnection()
    {
        var (text, tone) = Texts.Connection(_session);
        lblConnection.Text = "● " + text;
        lblConnection.ForeColor = Ui.Of(tone);
    }

    void ShowState()
    {
        var status = _session.LastStatus;
        var color = Ui.Of(Texts.StateTone(_session));
        var text = _session.StateText;
        if (lblState.Text == text && lblState.BackColor == color)
            return;
        lblState.Text = text;
        lblState.BackColor = color;
        toolTip.SetToolTip(lblState, status is null ? null
            : $"Grbl stanje: {status.RawState}" + (status.State == MachineState.Alarm ? ". Klikni \"Otključaj ($X)\"." : ""));
    }

    /// <summary>"Otključaj" is needed only in an alarm, so it is shown only then, next to the emergency stop.</summary>
    void ShowUnlock()
    {
        bool show = _session.IsOpen && _session.InAlarm;
        if (_unlockShown == show)
            return;
        _unlockShown = show;
        btnUnlock.Visible = show;
        // The remembered position takes the room while the button is hidden.
        lblRemembered.Width = (show ? btnUnlock.Left : btnReset.Left) - 6 - lblRemembered.Left;
    }

    void ShowStatus(GrblStatus status)
    {
        ShowState();
        ShowUnlock();
        positionDisplay.Work = status.WorkPosition;
        positionDisplay.Machine = status.MachinePosition;
        toolpathView.LaserPosition = status.WorkPosition;
    }

    void ShowRememberedPosition()
    {
        positionDisplay.Confirmed = !_session.PositionWaiting;
        var (text, tone) = Texts.Remembered(_session);
        lblRemembered.Text = text;
        lblRemembered.ForeColor = Ui.Of(tone);
    }

    void ShowProgram()
    {
        toolpathView.Toolpath = _session.Toolpath;
        progressJob.Value = 0;
        lblProgress.Text = "";
        lblCurrentLine.Text = "";
        ShowFileInfo();
    }

    void ShowFileInfo()
    {
        var info = Texts.FileInfo(_session);
        lblFile.Text = info is null ? "Nijedan fajl nije učitan" : Path.GetFileName(_session.ProgramPath);
        toolTip.SetToolTip(lblFile, _session.ProgramPath);
        lblFileInfo.Text = info ?? "";
        btnCloseFile.Visible = _session.Program is not null;
    }

    void btnCloseFile_Click(object sender, EventArgs e) => _session.CloseFile();

    void ShowProgress()
    {
        if (_session.Job is not { } job)
            return;
        int completed = job.CompletedCount;
        progressJob.Maximum = Math.Max(1, job.TotalCount);
        toolpathView.CompletedLines = completed;
        progressJob.Value = Math.Min(completed, progressJob.Maximum);
        lblProgress.Text = Texts.Progress(_session);
        lblCurrentLine.Text = Texts.CurrentLine(_session) ?? "";
    }

    void ShowLaser()
    {
        bool on = _session.LaserOn;
        var text = on ? "Ugasi laser" : "Upali laser";
        if (btnSpindle.Text == text)
            return;
        // Lit up while on, so a burning laser is hard to miss.
        btnSpindle.Text = text;
        if (on)
            Ui.Accent(btnSpindle, Ui.Red);
        else
            Ui.Plain(btnSpindle);
    }

    void ShowRemote()
    {
        lblRemote.Text = _remote.StateText;
        lblRemote.LinkColor = _remote.State switch
        {
            RemoteState.Connected => Ui.Green,
            RemoteState.Failed => Ui.Orange,
            RemoteState.Connecting => Ui.Blue,
            _ => Ui.MutedText,
        };
        ShowPageHost();
    }

#if REMOTE_ACCESS
    /// <summary>Remote access needs the owner's own website (src/CncController.Web); the public version is built without it.</summary>
    static readonly bool RemoteAccessAvailable = true;
#else
    static readonly bool RemoteAccessAvailable = false;
#endif

    /// <summary>Connects to the website if remote access is on and set up; otherwise makes sure it is off.</summary>
    void StartRemote()
    {
        if (RemoteAccessAvailable && Settings.RemoteEnabled && Settings.RemoteUrl is { Length: > 0 } url && Settings.RemoteKey is { Length: > 0 } key)
            _remote.Start(url, key);
        else
            _remote.Stop();
        ShowRemote();
    }

    void lblRemote_Click(object? sender, EventArgs e)
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

    void ShowCompressors() => lnkCompressors.Text = Texts.Compressors(_session, DateTime.Now);

    void UpdateUi()
    {
        bool open = _session.IsOpen;
        bool jobActive = _session.JobActive;
        ShowConnection();
        ShowState();
        ShowRememberedPosition();
        ShowLaser();
        ShowCompressors();
        ShowFileInfo();
        if (!open)
        {
            positionDisplay.Work = positionDisplay.Machine = null;
            toolpathView.LaserPosition = null;
        }
        // Connected from the web: show which port, and the simulator's window as after a click here.
        if (open && _session.PortName is { } port && !Equals(cmbPort.SelectedItem, port))
        {
            if (!cmbPort.Items.Contains(port))
                cmbPort.Items.Add(port);
            cmbPort.SelectedItem = port;
        }
        if (open && MachineSession.IsSimulatorPort(_session.PortName) && _simulatorForm?.Simulator != _session.Simulator)
            ShowSimulatorForm();
        btnConnect.Text = open ? "Odspoji" : "Spoji";
        cmbPort.Enabled = cmbBaud.Enabled = btnRefreshPorts.Enabled = !open;
        btnPause.Enabled = btnResume.Enabled = btnReset.Enabled = open;
        // While a job runs only pause/resume/reset make sense; anything else would mix lines into the job.
        btnSpindle.Enabled = btnUnlock.Enabled = btnSettings.Enabled = btnSend.Enabled = txtCommand.Enabled = _session.CanSendCommands;
        ShowUnlock();
        btnSetZero.Enabled = _session.CanSetPosition;
        btnRestorePosition.Enabled = _session.CanRestorePosition;
        btnGoToZero.Enabled = jogControl.Enabled = _session.CanMove;
        btnFrame.Enabled = _session.CanFrame;
        btnOpenFile.Enabled = _session.CanLoadFile;
        btnCloseFile.Enabled = _session.CanCloseFile;
        btnStart.Enabled = _session.CanStart;
        btnResumeFrom.Enabled = _session.CanResumeFrom;
        btnStop.Enabled = jobActive;
        // The port speed may also be chosen on the page.
        if (cmbBaud.SelectedItem as string != _session.Baud.ToString())
            cmbBaud.SelectedItem = _session.Baud.ToString();
        if (chkAutoResume.Checked != _session.AutoResume)
            chkAutoResume.Checked = _session.AutoResume;
        if (chkCompressorPause.Checked != _session.CompressorPause)
            chkCompressorPause.Checked = _session.CompressorPause;
    }

    void OnJobTimer()
    {
        _session.Tick();
        ShowCompressors();
    }

    // ----- Position question -----

    void OnQuestionChanged()
    {
        _questionDialog?.CloseIfGone(_session.PendingQuestion);
        // Shown after this event returns, so the session isn't waiting inside a modal loop.
        // The page asks the question itself (yellow box at the top).
        if (_session.PendingQuestion is not null && _questionDialog is null && !PageShown)
            OnUi(AskPendingQuestion);
    }

    void AskPendingQuestion()
    {
        // A new question may come up while one is shown (restart, answer, restart again); ask until none is left.
        while (_questionDialog is null && _session.PendingQuestion is { } question)
        {
            DialogResult result;
            using (_questionDialog = new QuestionDialog(question, Text, _session.Language))
            {
                try
                {
                    result = _questionDialog.ShowDialog(this);
                }
                finally
                {
                    _questionDialog = null;
                }
            }
            if (result is DialogResult.Yes or DialogResult.No)
                _session.AnswerPositionQuestion(question, result == DialogResult.Yes);
        }
    }

    // ----- Buttons and keys -----

    void btnSetZero_Click(object sender, EventArgs e) => _session.SetZero();

    void btnRestorePosition_Click(object sender, EventArgs e)
    {
        if (_session.RememberedPosition is not { } saved)
            return;
        if (Confirm(T($"Postaviti radnu poziciju na zapamćenu {saved}?", $"Set the work position to the remembered {saved}?") + Environment.NewLine
            + T("Samo ako CNC nije pomjeran otkako je zapamćena.", "Only if the CNC wasn't moved since it was remembered.")))
            _session.RestoreRememberedPosition();
    }

    void btnGoToZero_Click(object sender, EventArgs e) => _session.GoToZero();

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        const int WM_KEYDOWN = 0x100;
        // The page handles its keys itself; handling them here too would pause or jog twice.
        if (msg.Msg != WM_KEYDOWN || PageShown)
            return base.ProcessCmdKey(ref msg, keyData);
        // Holding a key auto-repeats; each repeat would queue another step, and the machine
        // would keep going long after the key is released. One press = one action.
        bool repeat = ((long)msg.LParam & 0x40000000) != 0;

        // Emergency keys work everywhere, even while typing a command.
        switch (keyData)
        {
            case Keys.Escape:
                if (!repeat && btnReset.Enabled)
                    btnReset_Click(btnReset, EventArgs.Empty);
                return true;
            case Keys.F5:
                if (!repeat && btnResume.Enabled)
                    btnResume_Click(btnResume, EventArgs.Empty);
                return true;
            case Keys.Control | Keys.O:
                if (!repeat && btnOpenFile.Enabled)
                    btnOpenFile_Click(btnOpenFile, EventArgs.Empty);
                return true;
        }

        // Typing in the command box or a list must keep its space and arrow keys.
        if (FromHandle(msg.HWnd) is TextBoxBase or ComboBox or NumericUpDown)
            return base.ProcessCmdKey(ref msg, keyData);
        if (keyData == Keys.Space)
        {
            if (!repeat && btnPause.Enabled)
                btnPause_Click(btnPause, EventArgs.Empty);
            return true;
        }
        if (jogControl.HandleKey(keyData, repeat))
            return true;
        return base.ProcessCmdKey(ref msg, keyData);
    }

    void btnRefreshPorts_Click(object sender, EventArgs e) => RefreshPorts();

    void btnConnect_Click(object sender, EventArgs e)
    {
        if (_session.IsOpen)
        {
            if (_session.JobActive && !Confirm(T("Posao je u toku. Ako odspojiš, slanje se prekida. Odspojiti?", "A job is running. Disconnecting stops sending it. Disconnect?")))
                return;
            _session.Disconnect();
            return;
        }
        if (cmbPort.SelectedItem is not string port)
        {
            MessageBox.Show(this, T("Izaberi COM port.", "Choose a COM port."), Text);
            return;
        }
        if (_session.Connect(port) && MachineSession.IsSimulatorPort(port))
            ShowSimulatorForm();
    }

    /// <summary>Shows the simulator's window with the fault buttons.</summary>
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

    void btnPause_Click(object sender, EventArgs e) => _session.Pause();

    void btnResume_Click(object sender, EventArgs e) => _session.Resume();

    void btnSpindle_Click(object sender, EventArgs e) => _session.ToggleLaser();

    void btnUnlock_Click(object sender, EventArgs e) => _session.Unlock();

    void btnSettings_Click(object sender, EventArgs e)
    {
        if (!_session.CheckGrblSettingsAllowed())
            return;
        using var dialog = new GrblSettingsDialog(_session.Grbl);
        dialog.ShowDialog(this);
        if (dialog.CurrentSettings.Count > 0)
            _session.RememberLimits(dialog.CurrentSettings);
    }

    void btnReset_Click(object sender, EventArgs e) => _session.Reset();

    string GCodeFilter => "G-code (*.tap;*.nc;*.gcode;*.ngc;*.txt)|*.tap;*.nc;*.gcode;*.ngc;*.txt|" + T("Svi fajlovi", "All files") + " (*.*)|*.*";

    void btnOpenFile_Click(object sender, EventArgs e)
    {
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

    void btnFrame_Click(object sender, EventArgs e) => _session.Frame();

    void btnStart_Click(object sender, EventArgs e) => _session.StartJob();

    void btnResumeFrom_Click(object sender, EventArgs e)
    {
        if (_session.Program is not { } program || _session.LastStatus?.WorkPosition is not { } here)
            return;

        var (suggested, stoppedText) = _session.SuggestResume();
        int shownLines = toolpathView.CompletedLines;
        using var dialog = new ResumeDialog(program, here, _session.Estimate, suggested, stoppedText);
        dialog.StartChanged += index => toolpathView.CompletedLines = Math.Max(0, index);
        // Left of the drawing, so the preview stays visible.
        dialog.Location = grpJob.PointToScreen(Point.Empty);
        var result = dialog.ShowDialog(this);
        toolpathView.CompletedLines = shownLines;
        if (result == DialogResult.OK && dialog.Plan is { } plan)
            _session.StartJob(plan);
    }

    void btnStop_Click(object sender, EventArgs e)
    {
        if (_session.JobActive && Confirm(T("Zaustaviti posao? Pokreti koji su već u Grbl baferu će se još izvršiti.", "Stop the job? Moves already in Grbl's buffer will still run.")))
            _session.Stop();
    }

    void lnkCompressors_LinkClicked(object? sender, LinkLabelLinkClickedEventArgs e)
    {
        using var dialog = new CompressorDialog(_session.Compressors);
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _session.SetCompressors(dialog.Schedule);
    }

    void btnSend_Click(object sender, EventArgs e) => SendCommandBox();

    void txtCommand_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            SendCommandBox();
        }
    }

    void SendCommandBox()
    {
        if (_session.SendCommand(txtCommand.Text))
            txtCommand.Clear();
    }

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
        Settings.Window = new WindowPlacement(bounds.X, bounds.Y, bounds.Width, bounds.Height,
            WindowState == FormWindowState.Maximized, splitMain.SplitterDistance);
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
        if (w.SplitterDistance >= splitMain.Panel1MinSize && w.SplitterDistance < splitMain.Width - splitMain.Panel2MinSize)
            splitMain.SplitterDistance = w.SplitterDistance;
    }

    void RefreshPorts()
    {
        var current = cmbPort.SelectedItem as string ?? DefaultPort;
        cmbPort.Items.Clear();
        cmbPort.Items.AddRange(SerialPort.GetPortNames().Order().Concat(MachineSession.SimulatorPortNames).ToArray<object>());
        if (cmbPort.Items.Contains(current))
            cmbPort.SelectedItem = current;
        else if (cmbPort.Items.Count > 0)
            cmbPort.SelectedIndex = 0;
    }

    bool Confirm(string question) =>
        MessageBox.Show(this, question, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    void AppendLog(DateTime time, string text)
    {
        if (txtLog.TextLength > MaxLogLength)
            txtLog.Text = txtLog.Text[^(MaxLogLength / 2)..];
        txtLog.AppendText($"{time:HH:mm:ss}  {text}{Environment.NewLine}");
    }

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
