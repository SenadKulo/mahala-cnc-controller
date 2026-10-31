using CncController.App;

namespace CncController.WinForms;

/// <summary>
/// The terms of use and the laser safety warning, accepted once before the program is used
/// (<see cref="AppSettings.AcceptedTerms"/>); a changed text gets a new <see cref="Version"/> and is asked again.
/// It also tells what the program sends over the internet, and lets the update check be turned off
/// (SignPath Foundation's terms for signed programs).
/// </summary>
public partial class TermsDialog : Form
{
    /// <summary>2: the privacy part and the update check option.</summary>
    public const int Version = 2;

    public TermsDialog(Language language, bool checkForUpdates)
    {
        InitializeComponent();
        Ui.FlattenButtons(this);
        Ui.Accent(btnAccept, Ui.Blue);
        if (language == Language.English)
        {
            Text = "Terms of use and laser safety";
            chkAccept.Text = "I have read this and accept it";
            chkUpdates.Text = "Check for new versions (mahalaworkshop.com)";
            btnAccept.Text = "Accept";
            btnDecline.Text = "Cancel";
        }
        chkUpdates.Checked = checkForUpdates;
        txtTerms.Text = string.Join(Environment.NewLine, language == Language.English ? English : Bosnian);
        txtTerms.Select(0, 0);
    }

    /// <summary>The user's choice for <see cref="AppSettings.CheckForUpdates"/>.</summary>
    public bool CheckForUpdates => chkUpdates.Checked;

    void chkAccept_CheckedChanged(object? sender, EventArgs e) => btnAccept.Enabled = chkAccept.Checked;

    static readonly string[] Bosnian =
    [
        "The Mahala Workshop - CNC Controller",
        "",
        "USLOVI KORIŠTENJA",
        "",
        "Program je besplatan. Koristiš ga na vlastitu odgovornost, bez ikakve garancije. Autor ne odgovara za štetu na CNC-u, materijalu, prostoru ili osobama.",
        "",
        "Program šalje komande tvom Grbl kontroleru i ne zna da li je CNC ispravno sastavljen i podešen. Prije prvog posla provjeri Grbl postavke ($$) i probaj posao sa ugašenim laserom (Okvir).",
        "",
        "SIGURNOST LASERA",
        "",
        "• Laserski zrak i njegov odsjaj oštećuju oči i kožu, i trajno. Dok laser radi, svi u prostoriji nose zaštitne naočale za talasnu dužinu i snagu tvog lasera.",
        "• Laser može zapaliti materijal. Drži aparat za gašenje požara na dohvat ruke. Ne ostavljaj laser da radi bez nadzora; ako ga ipak ostaviš (npr. preko noći), to je na tvoju odgovornost.",
        "• Dim i pare su štetni, a od nekih materijala otrovni. Radi uz odsis ili ventilaciju prema van. PVC i vinil nikad ne reži ni graviraj laserom.",
        "• „Hitni stop“ u programu i na mobitelu šalje Grbl reset. On ne zamjenjuje fizički prekidač za nuždu na samom CNC-u, koji mora postojati i biti na dohvat ruke.",
        "• Pristup sa mobitela (preko sajta ili lokalne mreže) može kasniti ili se prekinuti. Ne oslanjaj se na njega za zaustavljanje u nuždi.",
        "",
        "PRIVATNOST",
        "",
        "Program ne šalje tvoje podatke, fajlove ni poslove nikome. Sam se spaja samo na mahalaworkshop.com, da provjeri ima li nove verzije (pri pokretanju i svakih 12 sati); tada šalje ime programa, verziju i vrstu Windowsa, a server vidi tvoju IP adresu kao kod svakog web zahtjeva. To možeš isključiti ispod („Provjeravaj nove verzije“) ili u settings.json (\"CheckForUpdates\": false).",
        "Stranica u lokalnoj mreži radi samo kad je ti uključiš i samo unutar tvoje mreže.",
        "",
        "Klikom na „Prihvatam“ potvrđuješ da si ovo pročitao i da preuzimaš odgovornost za rad sa CNC-om i laserom.",
    ];

    static readonly string[] English =
    [
        "The Mahala Workshop - CNC Controller",
        "",
        "TERMS OF USE",
        "",
        "The program is free. You use it at your own risk, without any warranty. The author is not liable for damage to the CNC, the material, the premises or people.",
        "",
        "The program sends commands to your Grbl controller and cannot know whether the CNC is built and set up correctly. Before the first job, check Grbl's settings ($$) and try the job with the laser off (Frame).",
        "",
        "LASER SAFETY",
        "",
        "• The laser beam and its reflections damage eyes and skin, permanently. While the laser runs, everyone in the room wears safety glasses for your laser's wavelength and power.",
        "• The laser can set the material on fire. Keep a fire extinguisher within reach. Don't leave the laser running unattended; if you do anyway (e.g. overnight), it is at your own risk.",
        "• Smoke and fumes are harmful, from some materials toxic. Work with extraction or ventilation to the outside. Never cut or engrave PVC or vinyl with a laser.",
        "• \"Emergency stop\" in the program and on the phone sends a Grbl reset. It does not replace a physical emergency stop switch on the CNC itself, which must exist and be within reach.",
        "• Access from a phone (through the website or the local network) can lag or drop. Don't rely on it to stop in an emergency.",
        "",
        "PRIVACY",
        "",
        "The program doesn't send your data, files or jobs to anyone. The only connection it makes on its own is to mahalaworkshop.com, to check for a new version (at start and every 12 hours); it sends the program's name, version and type of Windows, and the server sees your IP address as with any web request. You can turn this off below (\"Check for new versions\") or in settings.json (\"CheckForUpdates\": false).",
        "The page in the local network runs only when you turn it on, and only inside your network.",
        "",
        "By clicking \"Accept\" you confirm that you have read this and take responsibility for working with the CNC and the laser.",
    ];
}
