using System.Diagnostics;
using CncController.App;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace CncController.WinForms;

/// <summary>
/// The window's new view: the website's machine page (ui/machine.html) in WebView2, talking to the session directly
/// through <see cref="LocalPage"/>, without the internet. The old controls stay underneath as a fallback
/// (<see cref="AppSettings.ClassicView"/>), and are used by themselves when WebView2 is missing.
/// </summary>
public partial class MainForm
{
    // The page's address inside the window; served from the ui folder next to the program.
    const string PageHost = "cnc.desktop";
    const string WebView2Download = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    WebView2? _webView;
    LocalPage? _page;

    /// <summary>The page is shown instead of the old controls.</summary>
    bool PageShown => _webView is { Visible: true };

    /// <summary>Shows the page, creating it the first time; stays on the old view if WebView2 is missing.</summary>
    async Task ShowPageAsync()
    {
        if (_webView is null)
        {
            try
            {
                CoreWebView2Environment.GetAvailableBrowserVersionString();
            }
            catch (WebView2RuntimeNotFoundException)
            {
                _session.Log($"Novi prikaz nije moguć: na računaru nema WebView2 (Microsoft Edge WebView2 Runtime, {WebView2Download}). Ostaje stari prikaz.");
                ShowClassic(remember: false);
                return;
            }
            try
            {
                await CreatePageAsync();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _session.Log("Novi prikaz se nije mogao pokrenuti, ostaje stari: " + ex.Message);
                _webView?.Dispose();
                _webView = null;
                ShowClassic(remember: false);
                return;
            }
        }
        _webView!.Visible = true;
        splitMain.Visible = statusStrip.Visible = false;
        _webView.Focus();
        Remember(classic: false);
    }

    async Task CreatePageAsync()
    {
        var webView = new WebView2 { Dock = DockStyle.Fill, Visible = false };
        Controls.Add(webView);
        webView.BringToFront();
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
            if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && uri.Host != PageHost)
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

        _page = new LocalPage(_api, OnUi, json =>
        {
            if (!webView.IsDisposed)
                core.PostWebMessageAsJson(json);
        });
        // The window's own dialogs, as in the old view.
        _page.AddHostMethod("OpenFile", _ => { btnOpenFile_Click(btnOpenFile, EventArgs.Empty); return null; });
        _page.AddHostMethod("RemoteSettings", _ => { lblRemote_Click(lblRemote, EventArgs.Empty); return null; });
        _page.AddHostMethod("LanSettings", _ => { ShowLanSettings(); return null; });
        _page.AddHostMethod("Update", _ => StartUpdate());
        _page.AddHostMethod("ClassicView", _ => { ShowClassic(remember: true); return null; });
        // Handled after WebView2's event, not inside it: a dialog opened from the page (file, remote access, local
        // network) runs its own message loop, and WebView2 kills the whole program when its event handler does that.
        core.WebMessageReceived += (_, e) =>
        {
            var json = e.WebMessageAsJson;
            OnUi(() => _page?.Receive(json));
        };
        ShowPageHost();
        core.Navigate($"https://{PageHost}/desktop.html");
    }

    /// <summary>Back to the old controls (from the page's "Stari prikaz", or when the page can't run).</summary>
    void ShowClassic(bool remember)
    {
        if (_webView is not null)
            _webView.Visible = false;
        splitMain.Visible = statusStrip.Visible = true;
        if (remember)
            Remember(classic: true);
        // A question the page was showing is now asked in the old window's way.
        if (_session.PendingQuestion is not null && _questionDialog is null)
            OnUi(AskPendingQuestion);
    }

    void Remember(bool classic)
    {
        if (Settings.ClassicView == classic)
            return;
        Settings.ClassicView = classic;
        _session.SaveSettings();
    }

    void lblNewView_Click(object? sender, EventArgs e) => _ = ShowPageAsync();

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
