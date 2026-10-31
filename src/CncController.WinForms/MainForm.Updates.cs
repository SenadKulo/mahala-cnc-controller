using CncController.App;

namespace CncController.WinForms;

/// <summary>The installed program: the terms at the first start, and new versions (<see cref="AppUpdates"/>).</summary>
public partial class MainForm
{
    /// <summary>The program may run for days; it looks again this often.</summary>
    static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(12);

    /// <summary>Asks once (again after the text changes); false and closing if the user doesn't accept.</summary>
    bool AcceptTerms()
    {
        if (Settings.AcceptedTerms >= TermsDialog.Version)
            return true;
        using var dialog = new TermsDialog(_session.Language, Settings.CheckForUpdates);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            Close();
            return false;
        }
        Settings.AcceptedTerms = TermsDialog.Version;
        Settings.CheckForUpdates = dialog.CheckForUpdates;
        _session.SaveSettings();
        return true;
    }

    /// <summary>Only if allowed (<see cref="AppSettings.CheckForUpdates"/>): it is the one connection the user didn't set up.</summary>
    void StartUpdateChecks()
    {
        if (!Settings.CheckForUpdates)
            return;
        _ = _updates.CheckAsync();
        var timer = new System.Windows.Forms.Timer(components!) { Interval = (int)UpdateCheckInterval.TotalMilliseconds };
        timer.Tick += (_, _) => _ = _updates.CheckAsync();
        timer.Start();
    }

    /// <summary>The version, or the new one to install, for the bar at the bottom of the page; null text hides the link.</summary>
    (string Version, string? Text, CncController.Remote.Tone Tone) UpdateText() => _updates.State switch
    {
        UpdateState.Available => (Version(), T($"Nova verzija {_updates.NewVersion}: nadogradi", $"New version {_updates.NewVersion}: update"), CncController.Remote.Tone.Green),
        UpdateState.Downloading => (Version(), T($"Preuzimam verziju {_updates.NewVersion}... {_updates.Progress}%", $"Downloading version {_updates.NewVersion}... {_updates.Progress}%"), CncController.Remote.Tone.Blue),
        UpdateState.Failed => (Version(), T($"Nadogradnja nije uspjela ({_updates.Error}), probaj ponovo", $"The update failed ({_updates.Error}), try again"), CncController.Remote.Tone.Orange),
        _ => (Version(), null, CncController.Remote.Tone.Muted),
    };

    string Version() => T("Verzija ", "Version ") + AppUpdates.CurrentVersion;

    /// <summary>The page's "update" link: downloads, closes like the user would, and starts the new version.</summary>
    object? StartUpdate()
    {
        if (_session.JobActive)
            return new { done = false, text = T("Nadogradnja nije moguća dok posao radi.", "The update isn't possible while a job is running.") };
        _ = UpdateAsync();
        return null;
    }

    async Task UpdateAsync()
    {
        if (!await _updates.DownloadAsync())
            return;
        // A job may have started during the download.
        if (_session.JobActive)
            return;
        _session.Log(T($"Nadogradnja na verziju {_updates.NewVersion}, program se ponovo pokreće.", $"Updating to version {_updates.NewVersion}, the program restarts."));
        _session.Disconnect();
        SaveOnExit();
        _updates.ApplyAndRestart();
    }
}
