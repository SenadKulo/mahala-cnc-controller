using System.Text.Json;
using System.Text.Json.Serialization;
using GrblCore;

namespace CncController.App;

/// <summary>Where the program keeps its data: %AppData%\CncController.</summary>
public static class AppData
{
    public static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CncController");

    public static readonly string SettingsFile = Path.Combine(Folder, "settings.json");
    public static readonly string PositionFile = Path.Combine(Folder, "position.json");

    /// <summary>Folder for the daily log files.</summary>
    public static readonly string LogFolder = Path.Combine(Folder, "logs");

    /// <summary>Folder for G-code files uploaded from the web page.</summary>
    public static readonly string UploadFolder = Path.Combine(Folder, "web");
}

/// <summary>Normal (not maximized) window bounds in screen pixels, plus the width of the left column.</summary>
public sealed record WindowPlacement(int X, int Y, int Width, int Height, bool Maximized, int SplitterDistance);

/// <summary>Language of everything the program shows: the page, the log, questions and notifications.</summary>
public enum Language
{
    Bosnian,
    English,
}

public static class Languages
{
    /// <summary>The code the page uses: "bs" or "en".</summary>
    public static string Code(this Language language) => language == Language.English ? "en" : "bs";

    public static Language? FromCode(string? code) => code switch
    {
        "bs" => Language.Bosnian,
        "en" => Language.English,
        _ => null,
    };

    /// <summary>For a first start: Bosnian on a Bosnian, Croatian, Serbian or Montenegrin Windows, English elsewhere.</summary>
    public static Language FromCulture(System.Globalization.CultureInfo culture) =>
        culture.TwoLetterISOLanguageName is "bs" or "hr" or "sr" or "cnr" ? Language.Bosnian : Language.English;
}

/// <summary>User settings, stored in %AppData%\CncController\settings.json.</summary>
public sealed class AppSettings
{
    /// <summary>Language the program speaks, chosen on the page (BS/EN). A first start follows Windows.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<Language>))]
    public Language Language { get; set; }

    /// <summary>Feed for the first ArtCAM toolpath (engraving), mm/min.</summary>
    public int FirstToolpathFeed { get; set; } = 400;

    /// <summary>Feed for every following toolpath (cutting), mm/min.</summary>
    public int OtherToolpathsFeed { get; set; } = 80;

    /// <summary>Last chosen manual jog step, mm.</summary>
    public double JogStep { get; set; } = 1;

    /// <summary>Folder the last G-code file was opened from.</summary>
    public string? LastFolder { get; set; }

    /// <summary>Main window size and position when it was last closed; null the first time.</summary>
    public WindowPlacement? Window { get; set; }

    /// <summary>
    /// Grbl's $5, $4, $8 and $9, for estimating job time. Defaults are the machine's values
    /// from October 2026; reading the settings in the Grbl settings window updates them.
    /// </summary>
    public double SeekRate { get; set; } = 500;
    public double DefaultFeed { get; set; } = 250;
    public double Acceleration { get; set; } = 20;
    public double JunctionDeviation { get; set; } = 0.05;

    [JsonIgnore]
    public MachineLimits Limits => new(SeekRate, DefaultFeed, Acceleration, JunctionDeviation);

    /// <summary>
    /// Laser power S sent with M3 and before a job on Grbl 0.9+, whose S is 0 after a reset (0.8 ignores it).
    /// Full power is Grbl 1.1's $30, 1000 by default.
    /// </summary>
    public int LaserPower { get; set; } = 1000;

    /// <summary>Continue a job by itself after Grbl restarted on its own (compressors). Off until the user turns it on.</summary>
    public bool AutoResume { get; set; }

    /// <summary>Stop with the laser off while the compressors switch on.</summary>
    public bool CompressorPause { get; set; }

    /// <summary>A moment the compressors switched on (learned from a restart or typed in); null while unknown.</summary>
    public DateTime? CompressorKnownStart { get; set; }

    public double CompressorPeriodMinutes { get; set; } = CompressorSchedule.DefaultPeriod.TotalMinutes;

    [JsonIgnore]
    public CompressorSchedule? Compressors
    {
        get => CompressorKnownStart is { } start && CompressorPeriodMinutes > 0
            ? new CompressorSchedule(start, TimeSpan.FromMinutes(CompressorPeriodMinutes))
            : null;
        set
        {
            CompressorKnownStart = value?.KnownStart;
            if (value is not null)
                CompressorPeriodMinutes = value.Period.TotalMinutes;
        }
    }

    /// <summary>The window shows its old controls instead of the machine page (WebView2); chosen in the window.</summary>
    public bool ClassicView { get; set; }

    /// <summary>Connect to the website for remote access. Off until the user turns it on.</summary>
    public bool RemoteEnabled { get; set; }

    /// <summary>Address of the website, e.g. https://cnc.example.com.</summary>
    public string? RemoteUrl { get; set; }

    /// <summary>Secret the website knows this desktop by (its Remote:DesktopKey). Stays on this computer, never in git.</summary>
    public string? RemoteKey { get; set; }

    /// <summary>Serve the page to phones and computers in the local network (<see cref="LanServer"/>). Off until the user turns it on.</summary>
    public bool LanEnabled { get; set; }

    /// <summary>Port of the page in the local network.</summary>
    public int LanPort { get; set; } = LanServer.DefaultPort;

    /// <summary>The 6-digit code a phone types once to reach the page in the local network; made when it is first turned on.</summary>
    public string? LanCode { get; set; }

    /// <summary>Version of the terms of use and laser safety warning the user accepted; 0 = never (shown at the first start).</summary>
    public int AcceptedTerms { get; set; }

    /// <summary>Where the installed program looks for new versions (the files installer/build.ps1 makes, uploaded there).</summary>
    public string UpdateUrl { get; set; } = DefaultUpdateUrl;

    public const string DefaultUpdateUrl = "https://mahalaworkshop.com/cnc/releases";

    /// <summary>File the settings were loaded from and are saved to; null keeps them in memory only (tests).</summary>
    [JsonIgnore]
    public string? FilePath { get; private set; }

    public static AppSettings Load(string path)
    {
        AppSettings settings;
        try
        {
            settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // First start or damaged file - defaults are fine, in the language of Windows.
            settings = new AppSettings { Language = Languages.FromCulture(System.Globalization.CultureInfo.CurrentUICulture) };
        }
        settings.FilePath = path;
        return settings;
    }

    public void Save()
    {
        if (FilePath is null)
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
