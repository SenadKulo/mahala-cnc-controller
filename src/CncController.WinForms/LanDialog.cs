using CncController.App;
using QRCoder;

namespace CncController.WinForms;

/// <summary>
/// Turns the page in the local network on or off (<see cref="LanServer"/>), and shows what a phone needs:
/// the address, the code, and a QR code that opens the page with the code already in it.
/// </summary>
public partial class LanDialog : Form
{
    readonly Language _language;

    public bool LanEnabled => chkEnabled.Checked;
    public int Port => (int)numPort.Value;
    public string Code => txtCode.Text;

    /// <param name="status">The server's state now (<see cref="LanServer.StateText"/>).</param>
    public LanDialog(AppSettings settings, string status)
    {
        InitializeComponent();
        _language = settings.Language;
        if (_language == Language.English)
        {
            Text = "Local network";
            lblInfo.Text = "A phone or another computer on the same network (Wi-Fi) opens the program's page directly, without the website or the internet. The first time, it enters the code below or scans the QR code.";
            chkEnabled.Text = "Turn on access in the local network";
            lblCode.Text = "Code:";
            btnNewCode.Text = "New code";
            lblAddress.Text = "Address:";
            btnFirewall.Text = "Allow in firewall";
            btnOk.Text = "OK";
            btnCancel.Text = "Cancel";
        }
        Ui.FlattenButtons(this);
        Ui.Accent(btnOk, Ui.Blue);
        chkEnabled.Checked = settings.LanEnabled;
        numPort.Value = Math.Clamp(settings.LanPort, (int)numPort.Minimum, (int)numPort.Maximum);
        txtCode.Text = settings.LanCode ?? LanServer.NewCode();
        _status = status;
        ShowAddress();
        ShowFirewall();
    }

    readonly string _status;

    void Inputs_Changed(object? sender, EventArgs e)
    {
        ShowAddress();
        if (sender == numPort)
            ShowFirewall();
    }

    /// <summary>Whether Windows lets the phone in: a firewall rule for the port, and no network treated as public.</summary>
    void ShowFirewall()
    {
        var allowed = Firewall.IsPortAllowed(Port);
        lblFirewall.Text = allowed switch
        {
            true => _language.T($"Firewall: port {Port} je dozvoljen.", $"Firewall: port {Port} is allowed."),
            false => _language.T($"Firewall: port {Port} nije dozvoljen, pa mobitel ne može doći do stranice.",
                $"Firewall: port {Port} isn't allowed, so the phone can't reach the page."),
            _ => _language.T("Firewall: ne mogu provjeriti da li je port dozvoljen.", "Firewall: can't check whether the port is allowed."),
        };
        lblFirewall.ForeColor = allowed == true ? Ui.Green : Ui.Orange;
        btnFirewall.Visible = allowed != true;

        // Another program's firewall filters instead of Windows': the Windows rule above doesn't help there.
        if (Firewall.OtherFirewalls() is [var other, ..])
        {
            lblFirewall.Text = _language.T(
                $"Firewall je ovdje {other}: u njemu dozvoli dolazne veze na port {Port} (TCP), Windows pravilo tu ne vrijedi.",
                $"The firewall here is {other}: allow incoming connections on port {Port} (TCP) in it, a Windows rule doesn't count there.");
            lblFirewall.ForeColor = Ui.Orange;
            btnFirewall.Visible = false;
        }

        var hint = Firewall.PublicNetworks() is [var network, ..]
            ? _language.T(
                $"Mreža „{network}“ je u Windowsu označena kao javna, pa Windows ne pušta mobitel. Ako je to kućna mreža: Postavke → Mreža i internet → WiFi → Privatna mreža.",
                $"Windows treats the network \"{network}\" as public, so it doesn't let the phone in. If it is your home network: Settings → Network & internet → Wi-Fi → Private network.")
            : _language.T("Mobitel mora biti na istoj mreži kao ovaj računar.", "The phone must be on the same network as this computer.");
        lblStatus.Text = _status + Environment.NewLine + hint;
    }

    /// <summary>Adds the firewall rule; Windows asks for an administrator's yes first.</summary>
    void btnFirewall_Click(object? sender, EventArgs e)
    {
        UseWaitCursor = true;
        bool added = Firewall.AllowPort(Port);
        UseWaitCursor = false;
        ShowFirewall();
        if (!added)
            MessageBox.Show(this, _language.T("Pravilo u firewallu nije dodano.", "The firewall rule was not added."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    /// <summary>A new code: every device has to enter it again (the old one stops working when this is saved).</summary>
    void btnNewCode_Click(object? sender, EventArgs e)
    {
        txtCode.Text = LanServer.NewCode();
        ShowAddress();
    }

    void ShowAddress()
    {
        var address = LanServer.Addresses().FirstOrDefault();
        var url = address is null ? null : $"http://{address}:{Port}";
        txtAddress.Text = url ?? _language.T("računar nije spojen na mrežu", "the computer isn't on a network");
        picQr.Image?.Dispose();
        picQr.Image = null;
        if (url is null || !LanEnabled)
            return;
        // Scanning it opens the page with the code, so nothing has to be typed.
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode($"{url}/?code={Code}", QRCodeGenerator.ECCLevel.M);
        using var stream = new MemoryStream(new PngByteQRCode(data).GetGraphic(8));
        using var png = Image.FromStream(stream);
        picQr.Image = new Bitmap(png);
    }
}
