using CncController.App;

namespace CncController.WinForms;

/// <summary>The page for phones in the local network (<see cref="LanServer"/>): turning it on, and its settings.</summary>
public partial class MainForm
{
    /// <summary>Starts the page in the local network if it is on (making its code the first time), otherwise stops it.</summary>
    async Task StartLanAsync()
    {
        if (!Settings.LanEnabled)
        {
            await _lan.StopAsync();
            return;
        }
        if (Settings.LanCode is not { Length: 6 })
        {
            Settings.LanCode = LanServer.NewCode();
            _session.SaveSettings();
        }
        await _lan.StartAsync(Settings.LanPort, Settings.LanCode);
        // The address only: the log also goes to the website, the code stays on this computer.
        _session.Log(_lan.State == LanState.Running
            ? T("Stranica u lokalnoj mreži: ", "Page in the local network: ") + (_lan.Url ?? "-")
            : _lan.StateText);
    }

    void ShowLanSettings()
    {
        using var dialog = new LanDialog(Settings, _lan.StateText);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        bool changed = dialog.LanEnabled != Settings.LanEnabled || dialog.Port != Settings.LanPort || dialog.Code != Settings.LanCode;
        if (!changed)
            return;
        Settings.LanEnabled = dialog.LanEnabled;
        Settings.LanPort = dialog.Port;
        Settings.LanCode = dialog.Code;
        _session.SaveSettings();
        if (!dialog.LanEnabled)
            _session.Log(T("Pristup u lokalnoj mreži isključen.", "Access in the local network off."));
        _ = StartLanAsync();
    }

    /// <summary>Closes the page's connections; the program is closing.</summary>
    void StopLan() => Task.Run(_lan.StopAsync).Wait(TimeSpan.FromSeconds(3));
}
