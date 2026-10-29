using CncController.App;

namespace CncController.WinForms;

/// <summary>Turns remote access on or off and sets the website's address and this desktop's key.</summary>
public partial class RemoteDialog : Form
{
    readonly Language _language;

    public bool RemoteEnabled => chkEnabled.Checked;
    public string Url => txtUrl.Text.Trim().TrimEnd('/');
    public string Key => txtKey.Text.Trim();

    public RemoteDialog(AppSettings settings, string status)
    {
        InitializeComponent();
        _language = settings.Language;
        if (_language == Language.English)
        {
            Text = "Remote access";
            lblInfo.Text = "The program connects to a website, so the CNC can be watched and stopped from a phone. The program works the same when the website or the internet isn't there.";
            chkEnabled.Text = "Turn on remote access";
            lblUrl.Text = "Website address:";
            lblKey.Text = "Key:";
            chkShowKey.Text = "Show the key";
            btnOk.Text = "OK";
            btnCancel.Text = "Cancel";
        }
        Ui.FlattenButtons(this);
        Ui.Accent(btnOk, Ui.Blue);
        chkEnabled.Checked = settings.RemoteEnabled;
        txtUrl.Text = settings.RemoteUrl ?? "";
        txtKey.Text = settings.RemoteKey ?? "";
        lblStatus.Text = status;
    }

    void chkShowKey_CheckedChanged(object? sender, EventArgs e) => txtKey.UseSystemPasswordChar = !chkShowKey.Checked;

    void btnOk_Click(object sender, EventArgs e)
    {
        if (chkEnabled.Checked)
        {
            if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
            {
                Warn(_language.T("Upiši adresu stranice, npr. https://cnc.primjer.ba", "Enter the website's address, e.g. https://cnc.example.com"));
                return;
            }
            // Over the internet the key and everything the page shows must be encrypted; plain http only for testing on this computer.
            if (uri.Scheme == "http" && !uri.IsLoopback)
            {
                Warn(_language.T("Preko interneta adresa mora počinjati sa https://.", "Over the internet the address must start with https://."));
                return;
            }
            if (Key.Length == 0)
            {
                Warn(_language.T("Upiši ključ (isti kao Remote:DesktopKey na sajtu).", "Enter the key (the same as Remote:DesktopKey on the website)."));
                return;
            }
        }
        DialogResult = DialogResult.OK;
    }

    void Warn(string text) => MessageBox.Show(this, text, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
