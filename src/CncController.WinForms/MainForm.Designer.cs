namespace CncController.WinForms;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            _page?.Dispose();
            _remote.Dispose();
            _session.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    // The same sections, in the same order and with the same buttons, as the web page (Pages/Index.cshtml):
    // CNC, Posao, Log i komande on the left; Ručno upravljanje and the drawing on the right.
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        toolTip = new ToolTip(components);
        splitMain = new SplitContainer();
        toolpathView = new ToolpathView();
        jogControl = new JogControl();
        grpMachine = new GroupBox();
        lblPort = new Label();
        cmbPort = new ComboBox();
        btnRefreshPorts = new Button();
        lblBaud = new Label();
        cmbBaud = new ComboBox();
        btnConnect = new Button();
        lblConnection = new Label();
        lblState = new Label();
        positionDisplay = new PositionDisplay();
        lblRemembered = new Label();
        btnUnlock = new Button();
        btnReset = new Button();
        btnSetZero = new Button();
        btnRestorePosition = new Button();
        btnGoToZero = new Button();
        btnSpindle = new Button();
        grpJob = new GroupBox();
        btnOpenFile = new Button();
        lblFile = new Label();
        btnCloseFile = new Button();
        lblFileInfo = new Label();
        btnStart = new Button();
        btnPause = new Button();
        btnResume = new Button();
        btnStop = new Button();
        btnFrame = new Button();
        btnResumeFrom = new Button();
        progressJob = new ProgressBar();
        lblProgress = new Label();
        lblCurrentLine = new Label();
        chkAutoResume = new CheckBox();
        chkCompressorPause = new CheckBox();
        lnkCompressors = new LinkLabel();
        grpLog = new GroupBox();
        txtLog = new TextBox();
        txtCommand = new TextBox();
        btnSend = new Button();
        btnSettings = new Button();
        statusStrip = new StatusStrip();
        lblRemote = new ToolStripStatusLabel();
        lblNewView = new ToolStripStatusLabel();
        ((System.ComponentModel.ISupportInitialize)splitMain).BeginInit();
        splitMain.Panel1.SuspendLayout();
        splitMain.Panel2.SuspendLayout();
        splitMain.SuspendLayout();
        jogControl.SuspendLayout();
        grpMachine.SuspendLayout();
        grpJob.SuspendLayout();
        grpLog.SuspendLayout();
        statusStrip.SuspendLayout();
        SuspendLayout();
        //
        // toolTip
        //
        toolTip.AutoPopDelay = 15000;
        toolTip.InitialDelay = 400;
        //
        // splitMain
        //
        splitMain.Dock = DockStyle.Fill;
        splitMain.FixedPanel = FixedPanel.Panel1;
        splitMain.Location = new Point(0, 0);
        splitMain.Name = "splitMain";
        // Size before adding children: anchored controls measure their distances from the panel's current size.
        splitMain.Size = new Size(1300, 760);
        splitMain.SplitterDistance = 508;
        splitMain.SplitterWidth = 6;
        splitMain.Panel1.Controls.Add(grpMachine);
        splitMain.Panel1.Controls.Add(grpJob);
        splitMain.Panel1.Controls.Add(grpLog);
        splitMain.Panel1MinSize = 508;
        // Docking runs from the last added control, so the jog strip takes the top and the view fills the rest.
        splitMain.Panel2.Controls.Add(toolpathView);
        splitMain.Panel2.Controls.Add(jogControl);
        splitMain.Panel2MinSize = 400;
        splitMain.TabIndex = 0;
        //
        // jogControl
        //
        jogControl.Controls.Add(btnSetZero);
        jogControl.Controls.Add(btnRestorePosition);
        jogControl.Controls.Add(btnGoToZero);
        jogControl.Controls.Add(btnSpindle);
        jogControl.Dock = DockStyle.Top;
        jogControl.Height = 200;
        jogControl.Name = "jogControl";
        jogControl.TabIndex = 1;
        jogControl.Text = "Ručno upravljanje";
        //
        // btnSetZero
        //
        btnSetZero.Location = new Point(10, 158);
        btnSetZero.Name = "btnSetZero";
        btnSetZero.Size = new Size(96, 30);
        btnSetZero.Text = "Postavi nulu";
        toolTip.SetToolTip(btnSetZero, "Trenutno mjesto postaje radna nula X0 Y0 Z0. Grbl je pamti i nakon reseta.");
        btnSetZero.Click += btnSetZero_Click;
        //
        // btnRestorePosition
        //
        btnRestorePosition.Location = new Point(112, 158);
        btnRestorePosition.Name = "btnRestorePosition";
        btnRestorePosition.Size = new Size(116, 30);
        btnRestorePosition.Text = "Vrati zapamćenu";
        toolTip.SetToolTip(btnRestorePosition, "Postavi radnu poziciju na zapamćenu - samo ako CNC nije pomjeran otkako je zapamćena.");
        btnRestorePosition.Click += btnRestorePosition_Click;
        //
        // btnGoToZero
        //
        btnGoToZero.Location = new Point(234, 158);
        btnGoToZero.Name = "btnGoToZero";
        btnGoToZero.Size = new Size(92, 30);
        btnGoToZero.Text = "Idi na 0,0,0";
        toolTip.SetToolTip(btnGoToZero, "Ugasi laser i idi brzim hodom na radnu nulu.");
        btnGoToZero.Click += btnGoToZero_Click;
        //
        // btnSpindle
        //
        btnSpindle.Location = new Point(332, 158);
        btnSpindle.Name = "btnSpindle";
        btnSpindle.Size = new Size(100, 30);
        btnSpindle.Text = "Upali laser";
        toolTip.SetToolTip(btnSpindle, "M3 pali laser, M5 ga gasi.");
        btnSpindle.Click += btnSpindle_Click;
        //
        // toolpathView
        //
        toolpathView.Dock = DockStyle.Fill;
        toolpathView.Location = new Point(0, 0);
        toolpathView.Name = "toolpathView";
        toolpathView.Size = new Size(786, 760);
        toolpathView.TabIndex = 0;
        //
        // grpMachine
        //
        grpMachine.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        grpMachine.Controls.Add(lblPort);
        grpMachine.Controls.Add(cmbPort);
        grpMachine.Controls.Add(btnRefreshPorts);
        grpMachine.Controls.Add(lblBaud);
        grpMachine.Controls.Add(cmbBaud);
        grpMachine.Controls.Add(btnConnect);
        grpMachine.Controls.Add(lblConnection);
        grpMachine.Controls.Add(lblState);
        grpMachine.Controls.Add(positionDisplay);
        grpMachine.Controls.Add(lblRemembered);
        grpMachine.Controls.Add(btnUnlock);
        grpMachine.Controls.Add(btnReset);
        grpMachine.Location = new Point(8, 6);
        grpMachine.Name = "grpMachine";
        grpMachine.Size = new Size(492, 166);
        grpMachine.TabIndex = 0;
        grpMachine.TabStop = false;
        grpMachine.Text = "CNC";
        //
        // lblPort
        //
        lblPort.AutoSize = true;
        lblPort.Location = new Point(10, 27);
        lblPort.Name = "lblPort";
        lblPort.Text = "Port:";
        //
        // cmbPort
        //
        cmbPort.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbPort.Location = new Point(44, 23);
        cmbPort.Name = "cmbPort";
        cmbPort.Size = new Size(76, 23);
        cmbPort.TabIndex = 1;
        //
        // btnRefreshPorts
        //
        btnRefreshPorts.Location = new Point(124, 22);
        btnRefreshPorts.Name = "btnRefreshPorts";
        btnRefreshPorts.Size = new Size(28, 25);
        btnRefreshPorts.TabIndex = 2;
        btnRefreshPorts.Text = "↻";
        toolTip.SetToolTip(btnRefreshPorts, "Ponovo potraži COM portove (npr. nakon uključivanja USB-a).");
        btnRefreshPorts.Click += btnRefreshPorts_Click;
        //
        // lblBaud
        //
        lblBaud.AutoSize = true;
        lblBaud.Location = new Point(162, 27);
        lblBaud.Name = "lblBaud";
        lblBaud.Text = "Baud:";
        //
        // cmbBaud
        //
        cmbBaud.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbBaud.Items.AddRange(new object[] { "9600", "19200", "38400", "57600", "115200" });
        cmbBaud.Location = new Point(200, 23);
        cmbBaud.Name = "cmbBaud";
        cmbBaud.Size = new Size(70, 23);
        cmbBaud.TabIndex = 3;
        //
        // btnConnect
        //
        btnConnect.Location = new Point(278, 22);
        btnConnect.Name = "btnConnect";
        btnConnect.Size = new Size(84, 25);
        btnConnect.TabIndex = 4;
        btnConnect.Text = "Spoji";
        btnConnect.Click += btnConnect_Click;
        //
        // lblConnection
        //
        lblConnection.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        lblConnection.AutoEllipsis = true;
        lblConnection.Location = new Point(370, 27);
        lblConnection.Name = "lblConnection";
        lblConnection.Size = new Size(112, 18);
        lblConnection.Text = "Nije spojeno";
        //
        // lblState
        //
        lblState.BackColor = Color.Gray;
        lblState.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        lblState.ForeColor = Color.White;
        lblState.Location = new Point(10, 56);
        lblState.Name = "lblState";
        lblState.Size = new Size(96, 62);
        lblState.Text = "NIJE SPOJENO";
        lblState.TextAlign = ContentAlignment.MiddleCenter;
        //
        // positionDisplay
        //
        positionDisplay.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        positionDisplay.Location = new Point(112, 56);
        positionDisplay.Name = "positionDisplay";
        positionDisplay.Size = new Size(370, 62);
        positionDisplay.TabStop = false;
        toolTip.SetToolTip(positionDisplay, "Radna pozicija (od nule koju si postavio). Sitno ispod: apsolutna pozicija - Grbl-ov unutrašnji brojač, nije bitna za rad.");
        //
        // lblRemembered
        //
        lblRemembered.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        lblRemembered.AutoEllipsis = true;
        lblRemembered.ForeColor = Color.FromArgb(110, 110, 110);
        lblRemembered.Location = new Point(10, 133);
        lblRemembered.Name = "lblRemembered";
        lblRemembered.Size = new Size(208, 18);
        lblRemembered.Text = "Zapamćeno: -";
        //
        // btnUnlock
        //
        btnUnlock.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnUnlock.Location = new Point(224, 126);
        btnUnlock.Name = "btnUnlock";
        btnUnlock.Size = new Size(118, 32);
        btnUnlock.TabIndex = 5;
        btnUnlock.Text = "Otključaj ($X)";
        btnUnlock.Visible = false;
        toolTip.SetToolTip(btnUnlock, "Grbl je u alarmu i ne prima komande. Provjeri CNC, pa skini alarm ($X).");
        btnUnlock.Click += btnUnlock_Click;
        //
        // btnReset
        //
        btnReset.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnReset.Location = new Point(348, 126);
        btnReset.Name = "btnReset";
        btnReset.Size = new Size(134, 32);
        btnReset.TabIndex = 6;
        btnReset.Text = "Hitni stop  [Esc]";
        toolTip.SetToolTip(btnReset, "CNC staje odmah i laser se gasi (Grbl reset, Ctrl-X). Prekinuti posao se može nastaviti sa \"Nastavi od...\". Pauza ne gasi laser - za to je ovo dugme.");
        btnReset.Click += btnReset_Click;
        //
        // grpJob
        //
        grpJob.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        grpJob.Controls.Add(btnOpenFile);
        grpJob.Controls.Add(lblFile);
        grpJob.Controls.Add(btnCloseFile);
        grpJob.Controls.Add(lblFileInfo);
        grpJob.Controls.Add(btnStart);
        grpJob.Controls.Add(btnPause);
        grpJob.Controls.Add(btnResume);
        grpJob.Controls.Add(btnStop);
        grpJob.Controls.Add(btnFrame);
        grpJob.Controls.Add(btnResumeFrom);
        grpJob.Controls.Add(progressJob);
        grpJob.Controls.Add(lblProgress);
        grpJob.Controls.Add(lblCurrentLine);
        grpJob.Controls.Add(chkAutoResume);
        grpJob.Controls.Add(chkCompressorPause);
        grpJob.Controls.Add(lnkCompressors);
        grpJob.Location = new Point(8, 178);
        grpJob.Name = "grpJob";
        grpJob.Size = new Size(492, 214);
        grpJob.TabIndex = 1;
        grpJob.TabStop = false;
        grpJob.Text = "Posao";
        //
        // btnOpenFile
        //
        btnOpenFile.Location = new Point(10, 22);
        btnOpenFile.Name = "btnOpenFile";
        btnOpenFile.Size = new Size(112, 34);
        btnOpenFile.TabIndex = 0;
        btnOpenFile.Text = "Otvori fajl...";
        toolTip.SetToolTip(btnOpenFile, "Učitaj G-code (Ctrl+O). Originalni ArtCAM fajl se prvo konvertuje za laser.");
        btnOpenFile.Click += btnOpenFile_Click;
        //
        // lblFile
        //
        lblFile.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        lblFile.AutoEllipsis = true;
        lblFile.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        lblFile.Location = new Point(130, 21);
        lblFile.Name = "lblFile";
        lblFile.Size = new Size(282, 18);
        lblFile.Text = "Nijedan fajl nije učitan";
        //
        // btnCloseFile
        //
        btnCloseFile.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnCloseFile.Location = new Point(418, 20);
        btnCloseFile.Name = "btnCloseFile";
        btnCloseFile.Size = new Size(64, 24);
        btnCloseFile.TabStop = false;
        btnCloseFile.Text = "Zatvori";
        btnCloseFile.Visible = false;
        toolTip.SetToolTip(btnCloseFile, "Zatvori fajl (crtež i podaci o poslu nestaju; zapamćeni prekinuti posao ostaje).");
        btnCloseFile.Click += btnCloseFile_Click;
        //
        // lblFileInfo
        //
        lblFileInfo.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        lblFileInfo.AutoEllipsis = true;
        lblFileInfo.ForeColor = Color.FromArgb(110, 110, 110);
        lblFileInfo.Location = new Point(130, 39);
        lblFileInfo.Name = "lblFileInfo";
        lblFileInfo.Size = new Size(352, 18);
        lblFileInfo.Text = "";
        //
        // btnStart
        //
        btnStart.Location = new Point(10, 64);
        btnStart.Name = "btnStart";
        btnStart.Size = new Size(112, 34);
        btnStart.TabIndex = 1;
        btnStart.Text = "▶  Start";
        toolTip.SetToolTip(btnStart, "Pokreni posao od početka, sa trenutne pozicije.");
        btnStart.Click += btnStart_Click;
        //
        // btnPause
        //
        btnPause.Location = new Point(130, 64);
        btnPause.Name = "btnPause";
        btnPause.Size = new Size(112, 34);
        btnPause.TabIndex = 2;
        btnPause.Text = "Pauza  [Space]";
        toolTip.SetToolTip(btnPause, "Zaustavi kretanje (feed hold, !). Laser ostaje upaljen - za hitno gašenje koristi Hitni stop.");
        btnPause.Click += btnPause_Click;
        //
        // btnResume
        //
        btnResume.Location = new Point(250, 64);
        btnResume.Name = "btnResume";
        btnResume.Size = new Size(112, 34);
        btnResume.TabIndex = 3;
        btnResume.Text = "Nastavi  [F5]";
        toolTip.SetToolTip(btnResume, "Nastavi nakon pauze (cycle start, ~).");
        btnResume.Click += btnResume_Click;
        //
        // btnStop
        //
        btnStop.Location = new Point(370, 64);
        btnStop.Name = "btnStop";
        btnStop.Size = new Size(112, 34);
        btnStop.TabIndex = 4;
        btnStop.Text = "■  Stop";
        toolTip.SetToolTip(btnStop, "Prestani slati posao. Pokreti koji su već u Grbl-u se završe.");
        btnStop.Click += btnStop_Click;
        //
        // btnFrame
        //
        btnFrame.Location = new Point(10, 104);
        btnFrame.Name = "btnFrame";
        btnFrame.Size = new Size(112, 30);
        btnFrame.TabIndex = 5;
        btnFrame.Text = "Okvir";
        toolTip.SetToolTip(btnFrame, "Obiđi rubove crteža ugašenim laserom, da provjeriš da li stane na materijal.");
        btnFrame.Click += btnFrame_Click;
        //
        // btnResumeFrom
        //
        btnResumeFrom.Location = new Point(130, 104);
        btnResumeFrom.Name = "btnResumeFrom";
        btnResumeFrom.Size = new Size(112, 30);
        btnResumeFrom.TabIndex = 6;
        btnResumeFrom.Text = "Nastavi od...";
        toolTip.SetToolTip(btnResumeFrom, "Nastavi prekinuti posao od izabrane linije (nakon reseta, nestanka struje ili USB-a).");
        btnResumeFrom.Click += btnResumeFrom_Click;
        //
        // progressJob
        //
        progressJob.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        progressJob.Location = new Point(10, 142);
        progressJob.Name = "progressJob";
        progressJob.Size = new Size(472, 10);
        progressJob.TabIndex = 7;
        //
        // lblProgress
        //
        lblProgress.Location = new Point(10, 158);
        lblProgress.Name = "lblProgress";
        lblProgress.Size = new Size(232, 18);
        lblProgress.Text = "";
        //
        // lblCurrentLine
        //
        lblCurrentLine.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        lblCurrentLine.AutoEllipsis = true;
        lblCurrentLine.Font = new Font("Consolas", 9F);
        lblCurrentLine.ForeColor = Color.FromArgb(110, 110, 110);
        lblCurrentLine.Location = new Point(246, 158);
        lblCurrentLine.Name = "lblCurrentLine";
        lblCurrentLine.Size = new Size(236, 18);
        lblCurrentLine.Text = "";
        lblCurrentLine.TextAlign = ContentAlignment.TopRight;
        //
        // chkAutoResume
        //
        chkAutoResume.AutoSize = true;
        chkAutoResume.Location = new Point(10, 186);
        chkAutoResume.Name = "chkAutoResume";
        chkAutoResume.TabIndex = 8;
        chkAutoResume.Text = "Sam nastavi nakon restarta";
        toolTip.SetToolTip(chkAutoResume, "Ako se Grbl sam restartuje usred posla (smetnje u napajanju, USB), program vrati zapamćenu poziciju i nastavi posao, najviše 10 puta po poslu. Ne nastavlja ako je Grbl u alarmu.");
        //
        // chkCompressorPause
        //
        chkCompressorPause.AutoSize = true;
        chkCompressorPause.Location = new Point(200, 186);
        chkCompressorPause.Name = "chkCompressorPause";
        chkCompressorPause.TabIndex = 9;
        chkCompressorPause.Text = "Planirana pauza";
        toolTip.SetToolTip(chkCompressorPause, "Pauza sa ugašenim laserom u poznatim razmacima (npr. dok se pale kompresori koji mogu restartovati Arduino): oko 10 s prije početka program ugasi laser između dvije linije, sačeka da CNC stane i nastavi 10 s nakon. Raspored se uči iz restarta ili se upiše klikom na link desno.");
        //
        // lnkCompressors
        //
        lnkCompressors.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        lnkCompressors.Location = new Point(340, 187);
        lnkCompressors.Name = "lnkCompressors";
        lnkCompressors.Size = new Size(142, 18);
        lnkCompressors.TabIndex = 10;
        lnkCompressors.TabStop = true;
        lnkCompressors.TextAlign = ContentAlignment.TopRight;
        toolTip.SetToolTip(lnkCompressors, "Raspored pauze: kada počinje i koliko često. Klikni za izmjenu.");
        lnkCompressors.LinkClicked += lnkCompressors_LinkClicked;
        //
        // grpLog
        //
        grpLog.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        grpLog.Controls.Add(txtLog);
        grpLog.Controls.Add(txtCommand);
        grpLog.Controls.Add(btnSend);
        grpLog.Controls.Add(btnSettings);
        grpLog.Location = new Point(8, 398);
        grpLog.Name = "grpLog";
        grpLog.Size = new Size(492, 354);
        grpLog.TabIndex = 2;
        grpLog.TabStop = false;
        grpLog.Text = "Log i komande";
        //
        // txtLog
        //
        txtLog.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        txtLog.BackColor = Color.White;
        txtLog.Font = new Font("Consolas", 9F);
        txtLog.Location = new Point(10, 22);
        txtLog.Multiline = true;
        txtLog.Name = "txtLog";
        txtLog.ReadOnly = true;
        txtLog.ScrollBars = ScrollBars.Vertical;
        txtLog.Size = new Size(472, 288);
        txtLog.TabIndex = 0;
        //
        // txtCommand
        //
        txtCommand.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        txtCommand.Font = new Font("Consolas", 10F);
        txtCommand.Location = new Point(10, 320);
        txtCommand.Name = "txtCommand";
        txtCommand.PlaceholderText = "G-code ili Grbl komanda, npr. G0 X10";
        txtCommand.Size = new Size(260, 23);
        txtCommand.TabIndex = 1;
        txtCommand.KeyDown += txtCommand_KeyDown;
        //
        // btnSend
        //
        btnSend.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        btnSend.Location = new Point(276, 319);
        btnSend.Name = "btnSend";
        btnSend.Size = new Size(82, 25);
        btnSend.TabIndex = 2;
        btnSend.Text = "Pošalji";
        btnSend.Click += btnSend_Click;
        //
        // btnSettings
        //
        btnSettings.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        btnSettings.Location = new Point(364, 319);
        btnSettings.Name = "btnSettings";
        btnSettings.Size = new Size(118, 25);
        btnSettings.TabIndex = 3;
        btnSettings.Text = "Grbl postavke...";
        toolTip.SetToolTip(btnSettings, "Pregled i izmjena Grbl postavki ($$). Samo kad CNC miruje.");
        btnSettings.Click += btnSettings_Click;
        //
        // statusStrip
        //
        statusStrip.Items.AddRange(new ToolStripItem[] { lblRemote, lblNewView });
        statusStrip.Location = new Point(0, 760);
        statusStrip.Name = "statusStrip";
        statusStrip.Size = new Size(1300, 22);
        statusStrip.SizingGrip = false;
        statusStrip.TabIndex = 1;
        //
        // lblRemote
        //
        lblRemote.IsLink = true;
        lblRemote.LinkBehavior = LinkBehavior.HoverUnderline;
        lblRemote.Name = "lblRemote";
        lblRemote.Text = "Daljinski pristup: isključen";
        lblRemote.ToolTipText = "Pristup CNC-u preko web stranice (mobitel, drugi računar). Klikni za podešavanje.";
        lblRemote.Click += lblRemote_Click;
        //
        // lblNewView
        //
        lblNewView.IsLink = true;
        lblNewView.LinkBehavior = LinkBehavior.HoverUnderline;
        lblNewView.Name = "lblNewView";
        lblNewView.Spring = true;
        lblNewView.Text = "Novi prikaz";
        lblNewView.TextAlign = ContentAlignment.MiddleRight;
        lblNewView.ToolTipText = "Prikaz kao na web stranici (isti na desktopu i mobitelu).";
        lblNewView.Click += lblNewView_Click;
        //
        // MainForm
        //
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1300, 782);
        Controls.Add(splitMain);
        Controls.Add(statusStrip);
        MinimumSize = new Size(980, 742);
        Name = "MainForm";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "The Mahala Workshop - CNC Controller";
        FormClosing += MainForm_FormClosing;
        jogControl.ResumeLayout(false);
        grpMachine.ResumeLayout(false);
        grpMachine.PerformLayout();
        grpJob.ResumeLayout(false);
        grpJob.PerformLayout();
        grpLog.ResumeLayout(false);
        grpLog.PerformLayout();
        splitMain.Panel1.ResumeLayout(false);
        splitMain.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)splitMain).EndInit();
        splitMain.ResumeLayout(false);
        statusStrip.ResumeLayout(false);
        statusStrip.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion

    private ToolTip toolTip;
    private SplitContainer splitMain;
    private ToolpathView toolpathView;
    private JogControl jogControl;
    private GroupBox grpMachine;
    private Label lblPort;
    private ComboBox cmbPort;
    private Button btnRefreshPorts;
    private Label lblBaud;
    private ComboBox cmbBaud;
    private Button btnConnect;
    private Label lblConnection;
    private Label lblState;
    private PositionDisplay positionDisplay;
    private Label lblRemembered;
    private Button btnUnlock;
    private Button btnReset;
    private Button btnSetZero;
    private Button btnRestorePosition;
    private Button btnGoToZero;
    private Button btnSpindle;
    private GroupBox grpJob;
    private Button btnOpenFile;
    private Label lblFile;
    private Button btnCloseFile;
    private Label lblFileInfo;
    private Button btnStart;
    private Button btnPause;
    private Button btnResume;
    private Button btnStop;
    private Button btnFrame;
    private Button btnResumeFrom;
    private ProgressBar progressJob;
    private Label lblProgress;
    private Label lblCurrentLine;
    private CheckBox chkAutoResume;
    private CheckBox chkCompressorPause;
    private LinkLabel lnkCompressors;
    private GroupBox grpLog;
    private TextBox txtLog;
    private TextBox txtCommand;
    private Button btnSend;
    private Button btnSettings;
    private StatusStrip statusStrip;
    private ToolStripStatusLabel lblRemote;
    private ToolStripStatusLabel lblNewView;
}
