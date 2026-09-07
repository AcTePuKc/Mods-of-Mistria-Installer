namespace Garethp.ModsOfMistriaInstallerLib.Crash;

/// <summary>
/// Notices the game crashing while AIM is open, and files it before it can be overwritten.
///
/// The supervised run in <see cref="GameRunRecorder"/> only sees the runs AIM started, and most
/// runs are started from Steam, from a desktop shortcut, or from the Play button. Those crashes are
/// the common case and they are exactly the ones that get lost: the game writes one crash file and
/// the next crash replaces it, so a player who crashes twice before opening AIM has already lost
/// the first report - which, given the second was probably a consequence of the first, is often the
/// one that mattered.
///
/// The watcher costs nothing when nothing crashes. It holds no handle on the game, cannot delay it,
/// and if the game's data folder does not exist yet it simply does not start; a user who has never
/// run the game has no crashes to lose.
/// </summary>
public sealed class CrashWatcher : IDisposable
{
    private readonly CrashArchive _archive;
    private readonly Func<(IReadOnlyList<string> Mods, DateTimeOffset? InstalledAt)> _snapshot;
    private FileSystemWatcher? _watcher;

    /// <summary>Raised on a background thread once a new crash has been filed.</summary>
    public event EventHandler<GameCrashLog>? CrashCaptured;

    /// <param name="snapshot">
    /// Asked for the load order at the moment of capture rather than at construction, because the
    /// user may have changed it - and a crash filed against the wrong mod list is worse than one
    /// filed against none.
    /// </param>
    public CrashWatcher(
        CrashArchive archive,
        Func<(IReadOnlyList<string> Mods, DateTimeOffset? InstalledAt)> snapshot)
    {
        _archive = archive;
        _snapshot = snapshot;
    }

    public void Start()
    {
        if (_watcher is not null) return;

        try
        {
            if (!Directory.Exists(CrashArchive.GameDataFolder)) return;

            _watcher = new FileSystemWatcher(CrashArchive.GameDataFolder)
            {
                Filter = Path.GetFileName(CrashArchive.GameCrashFile),
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            // Both, because a rewrite of an existing file raises Changed and a first crash on a
            // clean machine raises Created, and which one arrives is not worth reasoning about.
            _watcher.Created += OnCrashFile;
            _watcher.Changed += OnCrashFile;
        }
        catch (Exception exception)
        {
            // A machine that refuses file watching is a machine where the crash file is read on
            // demand instead. That is a smaller feature, not a broken AIM.
            Logger.Log($"Could not watch for game crashes: {exception.Message}");
            _watcher = null;
        }
    }

    public void Stop()
    {
        if (_watcher is null) return;

        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _watcher = null;
    }

    private async void OnCrashFile(object sender, FileSystemEventArgs args)
    {
        // Nothing may escape: this is an async void raised on a watcher thread, where an exception
        // is caught by nobody and takes AIM down with it.
        try
        {
            // The game is still writing when the first event arrives, and a half-written JSON
            // document parses as nothing. Waiting is free; the capture deduplicates, so the two or
            // three events one write produces cost one read between them.
            await Task.Delay(TimeSpan.FromMilliseconds(750));

            var (mods, installedAt) = _snapshot();
            var captured = _archive.Capture(mods, installedAt, note: "seen while AIM was open");
            if (captured is null) return;

            var log = GameCrashLog.Read(captured);
            if (log is not null) CrashCaptured?.Invoke(this, log);
        }
        catch (Exception exception)
        {
            Logger.Log($"Could not capture a crash as it happened: {exception.Message}");
        }
    }

    public void Dispose() => Stop();
}
