using System.Diagnostics;
using CncController.App;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace CncController.WinForms;

/// <summary>
/// The window's content: the website's machine page (ui/machine.html) in WebView2, talking to the session directly
/// through <see cref="LocalPage"/>, without the internet. Without WebView2 the program can't be used: it says what
/// to install and closes.
/// </summary>
public partial class MainForm
{
    // The page's address inside the window; served from the ui folder next to the program.
    const string PageHost = "cnc.desktop";
    const string WebView2Download = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    WebView2? _webView;
    LocalPage? _page;

    async Task ShowPageAsync()
    {
        try
        {
            CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            CloseWithoutPage(T("Programu treba Microsoft Edge WebView2 Runtime, a na ovom računaru ga nema (na Windowsu 10 i 11 je obično već tu).",
                "The program needs the Microsoft Edge WebView2 Runtime, which this computer doesn't have (Windows 10 and 11 usually have it)."));
            return;
        }
        try
        {
            await CreatePageAsync();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            CloseWithoutPage(T("Stranica programa se nije mogla pokrenuti (WebView2): ", "The program's page couldn't start (WebView2): ") + ex.Message);
        }
    }

    /// <summary>Says why there is nothing to show, offers the WebView2 download, and closes.</summary>
    void CloseWithoutPage(string reason)
    {
        _session.Log(reason);
        var answer = MessageBox.Show(this, reason + Environment.NewLine + Environment.NewLine
            + T("Otvoriti Microsoftovu stranicu za preuzimanje WebView2?", "Open Microsoft's WebView2 download page?"),
            Text, MessageBoxButtons.YesNo, MessageBoxIcon.Error);
        if (answer == DialogResult.Yes)
            OpenInBrowser(new Uri(WebView2Download));
        Close();
    }

    async Task CreatePageAsync()
    {
        var webView = new WebView2 { Dock = DockStyle.Fill };
        Controls.Add(webView);
        _webView = webView;

        // Its own data (the page's remembered step, keyboard choice) next to the program's other data.
        var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: Path.Combine(AppData.Folder, "webview"));
        await webView.EnsureCoreWebView2Async(environment);
        var core = webView.CoreWebView2;
        // F5 is "Nastavi" and Ctrl+O opens a file, as in the old window: not the browser's reload and open.
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
#if !DEBUG
        core.Settings.AreDevToolsEnabled = false;
#endif
        core.SetVirtualHostNameToFolderMapping(PageHost, Path.Combine(AppContext.BaseDirectory, "ui"), CoreWebView2HostResourceAccessKind.Deny);
        // Links to other sites open in the browser; the window shows only the machine page.
        core.NavigationStarting += (_, e) =>
        {
            // blob: is a file the page makes itself (Grbl settings saved to a file).
            if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && uri.Scheme != "blob" && uri.Host != PageHost)
            {
                e.Cancel = true;
                OpenInBrowser(uri);
            }
        };
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri))
                OpenInBrowser(uri);
        };
        // A file from the page: asked where to save it, as elsewhere in the window, instead of WebView2's Downloads.
        core.DownloadStarting += (_, e) =>
        {
            var deferral = e.GetDeferral();
            var suggested = e.ResultFilePath;
            // The dialog runs its own message loop: after WebView2's event, not inside it (see WebMessageReceived).
            OnUi(() =>
            {
                using (deferral)
                {
                    using var dialog = new SaveFileDialog
                    {
                        Title = T("Spremi fajl", "Save the file"),
                        Filter = T("Tekst", "Text") + " (*.txt)|*.txt|" + T("Svi fajlovi", "All files") + " (*.*)|*.*",
                        FileName = Path.GetFileName(suggested),
                    };
                    if (Directory.Exists(Settings.LastFolder))
                        dialog.InitialDirectory = Settings.LastFolder;
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        e.ResultFilePath = dialog.FileName;
                        e.Handled = true;
                        _session.Log(T("Spremljeno: ", "Saved: ") + dialog.FileName);
                    }
                    else
                    {
                        e.Cancel = true;
                    }
                }
            });
        };

        _page = new LocalPage(_api, OnUi, json =>
        {
            if (!webView.IsDisposed)
                core.PostWebMessageAsJson(json);
        }, recentLog: [.. _recentLog]);
        // The window's own dialogs.
        _page.AddHostMethod("OpenFile", _ => { OpenFile(); return null; });
        _page.AddHostMethod("RemoteSettings", _ => { ShowRemoteSettings(); return null; });
        _page.AddHostMethod("LanSettings", _ => { ShowLanSettings(); return null; });
        _page.AddHostMethod("Update", _ => StartUpdate());
        // Handled after WebView2's event, not inside it: a dialog opened from the page (file, remote access, local
        // network) runs its own message loop, and WebView2 kills the whole program when its event handler does that.
        core.WebMessageReceived += (_, e) =>
        {
            var json = e.WebMessageAsJson;
            OnUi(() => _page?.Receive(json));
        };
        ShowPageHost();
        core.Navigate($"https://{PageHost}/desktop.html");
        webView.Focus();
    }

    /// <summary>
    /// What only the window knows, for its bar at the bottom of the page: remote access (only with the website,
    /// <see cref="RemoteAccessAvailable"/>), the local network, the version,
    /// and a new version to install (null text: none).
    /// </summary>
    sealed record HostState(bool RemoteAvailable, string RemoteText, int RemoteTone, string LanText, int LanTone, string VersionText, string? UpdateText, int UpdateTone);

    HostState? _shownHost;

    /// <summary>Sends the bar's state to the page, only when it changed (the session changes several times a second).</summary>
    void ShowPageHost()
    {
        var (version, update, updateTone) = UpdateText();
        var host = new HostState(
            RemoteAccessAvailable,
            _remote.StateText,
            (int)(_remote.State switch
            {
                RemoteState.Connected => CncController.Remote.Tone.Green,
                RemoteState.Failed => CncController.Remote.Tone.Orange,
                RemoteState.Connecting => CncController.Remote.Tone.Blue,
                _ => CncController.Remote.Tone.Muted,
            }),
            _lan.StateText,
            (int)(_lan.State switch
            {
                LanState.Running => CncController.Remote.Tone.Green,
                LanState.Failed => CncController.Remote.Tone.Orange,
                LanState.Starting => CncController.Remote.Tone.Blue,
                _ => CncController.Remote.Tone.Muted,
            }),
            version, update, (int)updateTone);
        if (_page is null || host == _shownHost)
            return;
        _shownHost = host;
        _page.SetHost(host);
    }

    static void OpenInBrowser(Uri uri)
    {
        if (uri.Scheme is "https" or "http")
            Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
    }
}
