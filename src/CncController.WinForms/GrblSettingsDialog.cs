using GrblCore;

namespace CncController.WinForms;

/// <summary>Reads Grbl's settings with '$$', shows them with a description and writes back changed values.</summary>
public partial class GrblSettingsDialog : Form
{
    static readonly Color ChangedColor = Color.LightYellow;

    readonly GrblConnection _grbl;
    bool _reading;
    // Grbl writes each setting to EEPROM and may miss incoming characters meanwhile,
    // so settings are sent one at a time, each after the previous "ok".
    readonly Queue<(DataGridViewRow Row, string Command)> _toSave = new();
    (DataGridViewRow Row, string Command)? _saving;
    int _saved;

    readonly Action<string> _onLine;
    readonly Action<string, string?> _onAck;
    readonly Action<DisconnectReason, string?> _onDisconnected;
    readonly Action<GrblVersion> _onConnected;

    public GrblSettingsDialog(GrblConnection grbl)
    {
        InitializeComponent();
        Ui.FlattenButtons(this);
        Ui.Accent(btnSave, Ui.Blue);
        _grbl = grbl;

        _onLine = line => OnUi(() => OnLine(line));
        _onAck = (line, error) => OnUi(() => OnAcknowledged(line, error));
        _onDisconnected = (_, _) => OnUi(() => Interrupt("Veza prekinuta."));
        _onConnected = _ => OnUi(() => Interrupt("Grbl je resetovan."));
        _grbl.LineReceived += _onLine;
        _grbl.Acknowledged += _onAck;
        _grbl.Disconnected += _onDisconnected;
        _grbl.Connected += _onConnected;

        Shown += (_, _) => ReadSettings();
    }

    /// <summary>Settings as last read from Grbl.</summary>
    public IReadOnlyList<GrblSetting> CurrentSettings =>
        _reading ? [] : gridSettings.Rows.Cast<DataGridViewRow>().Select(r => (GrblSetting)r.Tag!).ToList();

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _grbl.LineReceived -= _onLine;
        _grbl.Acknowledged -= _onAck;
        _grbl.Disconnected -= _onDisconnected;
        _grbl.Connected -= _onConnected;
        base.OnFormClosed(e);
    }

    void ReadSettings()
    {
        if (!TrySend(GrblSetting.DumpCommand))
            return;
        gridSettings.Rows.Clear();
        _reading = true;
        lblStatus.Text = "Čitam postavke...";
        UpdateButtons();
    }

    void OnLine(string line)
    {
        if (!_reading || !GrblSetting.TryParse(line, out var setting))
            return;
        // This window is part of the old view, which stays Bosnian.
        var description = CncController.App.Texts.GrblSettingMeaning(_grbl.Version, setting.Number, CncController.App.Language.Bosnian);
        int index = gridSettings.Rows.Add($"${setting.Number}", setting.Value, description, setting.Description ?? "");
        gridSettings.Rows[index].Tag = setting;
    }

    void OnAcknowledged(string line, string? error)
    {
        if (_reading && line == GrblSetting.DumpCommand)
        {
            _reading = false;
            lblStatus.Text = error is null ? $"Učitano {gridSettings.Rows.Count} postavki." : $"Grbl je odbio čitanje: {error}";
            UpdateButtons();
            return;
        }
        if (_saving is not { } current || line != current.Command)
            return;

        _saving = null;
        if (error is not null)
        {
            _toSave.Clear();
            gridSettings.CurrentCell = current.Row.Cells[colValue.Index];
            lblStatus.Text = $"Grbl je odbio {current.Command}: {error}";
            MessageBox.Show(this, $"Grbl je odbio {current.Command}:{Environment.NewLine}{error}", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            UpdateButtons();
            return;
        }
        _saved++;
        SaveNext();
    }

    void btnSave_Click(object sender, EventArgs e)
    {
        gridSettings.EndEdit();
        foreach (DataGridViewRow row in gridSettings.Rows)
            if (IsChanged(row) && row.Tag is GrblSetting setting && setting.Normalize(CellValue(row)) is { } value)
                _toSave.Enqueue((row, setting.SetCommand(value)));
        if (_toSave.Count == 0)
        {
            lblStatus.Text = "Nema izmjena.";
            return;
        }
        var list = string.Join(Environment.NewLine, _toSave.Select(s => "  " + s.Command));
        if (MessageBox.Show(this, $"Upisati u Grbl:{Environment.NewLine}{list}", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            _toSave.Clear();
            return;
        }
        _saved = 0;
        SaveNext();
    }

    void SaveNext()
    {
        if (!_toSave.TryDequeue(out var next))
        {
            // Read back what Grbl actually stored.
            ReadSettings();
            lblStatus.Text = $"Spremljeno {_saved} postavki, čitam ponovo...";
            return;
        }
        if (!TrySend(next.Command))
        {
            _toSave.Clear();
            UpdateButtons();
            return;
        }
        _saving = next;
        lblStatus.Text = $"Upisujem {next.Command}...";
        UpdateButtons();
    }

    void btnReload_Click(object sender, EventArgs e)
    {
        if (!HasChanges() || Confirm("Izmjene koje nisu spremljene će se izgubiti. Učitati ponovo?"))
            ReadSettings();
    }

    void btnExport_Click(object sender, EventArgs e)
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Izvezi Grbl postavke",
            Filter = "Tekst (*.txt)|*.txt|Svi fajlovi (*.*)|*.*",
            FileName = $"grbl-postavke-{DateTime.Now:yyyy-MM-dd}.txt",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        // Values as Grbl has them, not unsaved edits; each line can be sent back as a command.
        var lines = gridSettings.Rows.Cast<DataGridViewRow>()
            .Select(r => (GrblSetting)r.Tag!)
            .Select(s => s.Description is null ? $"${s.Number}={s.Value}" : $"${s.Number}={s.Value} ({s.Description})");
        try
        {
            File.WriteAllLines(dialog.FileName, [$"; Grbl {_grbl.Version}, {DateTime.Now:dd.MM.yyyy HH:mm}", .. lines]);
            lblStatus.Text = "Izvezeno u " + dialog.FileName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Greška s fajlom:{Environment.NewLine}{ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    void gridSettings_CellValidating(object? sender, DataGridViewCellValidatingEventArgs e)
    {
        if (e.ColumnIndex != colValue.Index || gridSettings.Rows[e.RowIndex].Tag is not GrblSetting setting)
            return;
        if (setting.Normalize(e.FormattedValue?.ToString() ?? "") is null)
        {
            gridSettings.Rows[e.RowIndex].ErrorText = setting.IsInteger
                ? "Upiši cijeli broj (0 ili veći)."
                : "Upiši broj (0 ili veći), npr. 250.5";
            e.Cancel = true;
        }
    }

    void gridSettings_CellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        var row = gridSettings.Rows[e.RowIndex];
        row.ErrorText = "";
        if (e.ColumnIndex != colValue.Index || row.Tag is not GrblSetting setting)
            return;
        row.Cells[colValue.Index].Value = setting.Normalize(CellValue(row));
        row.DefaultCellStyle.BackColor = IsChanged(row) ? ChangedColor : Color.Empty;
    }

    bool IsChanged(DataGridViewRow row)
    {
        var setting = (GrblSetting)row.Tag!;
        // Compare as numbers: "500.0" and "500.000" are the same value.
        return setting.Normalize(CellValue(row)) != setting.Normalize(setting.Value);
    }

    string CellValue(DataGridViewRow row) => row.Cells[colValue.Index].Value?.ToString() ?? "";

    bool HasChanges() => gridSettings.Rows.Cast<DataGridViewRow>().Any(IsChanged);

    void GrblSettingsDialog_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_saving is not null)
        {
            lblStatus.Text = "Sačekaj da se upis završi.";
            e.Cancel = true;
        }
        else if (HasChanges() && !Confirm("Izmjene nisu spremljene u Grbl. Zatvoriti svejedno?"))
        {
            e.Cancel = true;
        }
    }

    void Interrupt(string reason)
    {
        if (!_reading && _saving is null && _toSave.Count == 0)
            return;
        _reading = false;
        _saving = null;
        _toSave.Clear();
        lblStatus.Text = reason + " Prekinuto.";
        UpdateButtons();
    }

    bool TrySend(string command)
    {
        if (!_grbl.IsReady)
        {
            lblStatus.Text = "Grbl nije spreman.";
            return false;
        }
        _grbl.SendLine(command);
        return true;
    }

    void UpdateButtons()
    {
        bool busy = _reading || _saving is not null;
        btnReload.Enabled = btnSave.Enabled = !busy;
        btnExport.Enabled = !busy && gridSettings.Rows.Count > 0;
        gridSettings.ReadOnly = busy;
    }

    bool Confirm(string question) =>
        MessageBox.Show(this, question, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

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
        }
    }
}
