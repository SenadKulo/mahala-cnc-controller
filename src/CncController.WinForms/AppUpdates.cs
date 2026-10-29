using Velopack;

namespace CncController.WinForms;

public enum UpdateState
{
    /// <summary>Not installed (run from Visual Studio), nothing found yet, or the check failed quietly (no internet).</summary>
    None,
    Available,
    Downloading,
    Failed,
}

/// <summary>
/// New versions of the installed program (Velopack): a quiet check now and then, and on a click the download,
/// after which the program restarts as the new version. Use it on the UI thread; <see cref="Changed"/> comes there.
/// </summary>
sealed class AppUpdates
{
    readonly UpdateManager? _manager;
    UpdateInfo? _update;

    public AppUpdates(string url)
    {
        try
        {
            var manager = new UpdateManager(url);
            // Run from Visual Studio, it isn't installed and can't update itself.
            _manager = manager.IsInstalled ? manager : null;
        }
        catch (Exception ex) when (ex is ArgumentException or UriFormatException or InvalidOperationException)
        {
            _manager = null;
        }
    }

    /// <summary>This program's version, e.g. "1.0.0" (without the commit the build adds after "+").</summary>
    public static string CurrentVersion => Application.ProductVersion.Split('+')[0];

    public UpdateState State { get; private set; }

    /// <summary>The version found, while <see cref="State"/> is Available or Downloading.</summary>
    public string? NewVersion => _update?.TargetFullRelease.Version.ToString();

    /// <summary>Percent downloaded, while downloading.</summary>
    public int Progress { get; private set; }

    public string? Error { get; private set; }

    public event Action? Changed;

    /// <summary>Asks the update address for a newer version; without internet it stays quiet and tries next time.</summary>
    public async Task CheckAsync()
    {
        if (_manager is null || State is UpdateState.Downloading)
            return;
        try
        {
            _update = await _manager.CheckForUpdatesAsync();
            State = _update is null ? UpdateState.None : UpdateState.Available;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or InvalidOperationException)
        {
            // Offline, or no releases uploaded yet: not worth bothering anyone.
            State = _update is null ? UpdateState.None : UpdateState.Available;
        }
        Changed?.Invoke();
    }

    /// <summary>Downloads the new version; true when it is ready for <see cref="ApplyAndRestart"/>.</summary>
    public async Task<bool> DownloadAsync()
    {
        if (_manager is null || _update is not { } update || State == UpdateState.Downloading)
            return false;
        State = UpdateState.Downloading;
        Progress = 0;
        Changed?.Invoke();
        try
        {
            var ui = SynchronizationContext.Current;
            await _manager.DownloadUpdatesAsync(update, percent => ui?.Post(_ =>
            {
                Progress = percent;
                Changed?.Invoke();
            }, null));
            // Ready; if a job holds it back, the next click applies it.
            State = UpdateState.Available;
            Progress = 100;
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or InvalidOperationException
            or UnauthorizedAccessException)
        {
            State = UpdateState.Failed;
            Error = ex.Message;
            Changed?.Invoke();
            return false;
        }
    }

    /// <summary>Closes the program, installs the downloaded version and starts it.</summary>
    public void ApplyAndRestart()
    {
        if (_manager is not null && _update is { } update)
            _manager.ApplyUpdatesAndRestart(update);
    }
}
