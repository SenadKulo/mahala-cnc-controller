using CncController.App;
using GrblCore.Simulation;

namespace CncController.WinForms;

/// <summary>Controls for the simulated Grbl: how fast time runs, and faults to test with.</summary>
public partial class SimulatorForm : Form
{
    static readonly TimeSpan CompressorPeriod = TimeSpan.FromMinutes(15);
    static readonly double[] Speeds = [1, 2, 5, 10, 30, 60];

    readonly GrblSimulator _simulator;
    readonly Language _language;

    public SimulatorForm(GrblSimulator simulator, Language language)
    {
        InitializeComponent();
        _simulator = simulator;
        _language = language;
        Text = T("Simulirani Grbl ", "Simulated Grbl ") + (simulator.Firmware == SimulatedFirmware.Grbl11 ? "1.1" : "0.8c");
        if (language == Language.English)
        {
            lblSpeed.Text = "Time speed:";
            chkAutoRestart.Text = "Restart every 15 min (power glitches)";
            btnRestart.Text = "Restart the Arduino (power glitch)";
            btnCorrupt.Text = "Garble the next status";
            btnUnplug.Text = "Pull the USB cable";
        }
        Ui.FlattenButtons(this);
        Ui.Accent(btnRestart, Ui.Orange);

        cmbSpeed.Items.AddRange(Speeds.Select(s => (object)$"{s}×").ToArray());
        int speed = Array.IndexOf(Speeds, simulator.TimeScale);
        cmbSpeed.SelectedIndex = speed >= 0 ? speed : 0;
        RefreshState();
    }

    public GrblSimulator Simulator => _simulator;

    string T(string bosnian, string english) => _language.T(bosnian, english);

    void cmbSpeed_SelectedIndexChanged(object? sender, EventArgs e) => _simulator.TimeScale = Speeds[cmbSpeed.SelectedIndex];

    void chkAutoRestart_CheckedChanged(object? sender, EventArgs e) =>
        _simulator.RestartInterval = chkAutoRestart.Checked ? CompressorPeriod : null;

    void btnRestart_Click(object sender, EventArgs e) => _simulator.Restart();

    void btnCorrupt_Click(object sender, EventArgs e) => _simulator.CorruptNextStatus();

    void btnUnplug_Click(object sender, EventArgs e) => _simulator.Unplug();

    void timerRefresh_Tick(object? sender, EventArgs e) => RefreshState();

    void RefreshState()
    {
        bool laser = _simulator.LaserOn;
        var state = _simulator.IsBooting ? T("pokreće se...", "starting...") : _simulator.State;
        lblState.Text = T("Stanje: ", "State: ") + state + "   ·   laser " + (laser ? T("UPALJEN", "ON") : T("ugašen", "off"));
        lblState.ForeColor = laser ? Ui.Red : SystemColors.ControlText;
        lblPosition.Text = T("Apsolutna pozicija: ", "Absolute position: ") + _simulator.MachinePosition;
        var time = _simulator.Time;
        lblTime.Text = T("Simulirano vrijeme: ", "Simulated time: ") + $"{(int)time.TotalHours}:{time:mm\\:ss}   ·   " + T("restarta: ", "restarts: ") + _simulator.Restarts;
        lblNextRestart.Text = _simulator.NextRestartIn is { } next ? T("još ", "in ") + $"{(int)next.TotalMinutes}:{next:ss}" : "";
        btnUnplug.Enabled = _simulator.IsConnected;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Only hidden; the main window shows it again on the next connect to the simulator.
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnFormClosing(e);
    }
}
