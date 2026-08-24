namespace Garethp.ModsOfMistriaInstallerLib;

/// <summary>
/// Watches the mods folder so a mod dropped in by hand shows up without restarting AIM.
///
/// Copying a mod in is not one filesystem event but a burst of them - the folder appears, then its
/// files arrive one by one - so the callback fires once the folder has been quiet for a moment
/// rather than on every event. AIM's own bookkeeping files are ignored, otherwise saving a profile
/// would look like a change to the mod list and start a reload loop.
/// </summary>
public sealed class ModsFolderWatcher : IDisposable
{
    private static readonly string[] IgnoredNames =
        ["momi_profiles.json", "aim_profiles.json", "nexus.json", Nexus.NexusInstallIndex.FileName];

    private readonly string _modsLocation;
    private readonly Action _onSettled;
    private readonly TimeSpan _settleTime;

    private FileSystemWatcher? _watcher;
    private Timer? _settleTimer;
    private bool _disposed;

    public ModsFolderWatcher(string modsLocation, Action onSettled, TimeSpan? settleTime = null)
    {
        _modsLocation = modsLocation;
        _onSettled = onSettled;
        _settleTime = settleTime ?? TimeSpan.FromSeconds(2);
    }

    public bool Start()
    {
        if (!Directory.Exists(_modsLocation)) return false;

        try
        {
            _watcher = new FileSystemWatcher(_modsLocation)
            {
                // Subdirectories matter: an extracted mod folder is created before the manifest
                // inside it exists, and it is the manifest that makes it a mod.
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite
            };

            _watcher.Created += OnChanged;
            _watcher.Deleted += OnChanged;
            _watcher.Renamed += OnChanged;
            _watcher.Changed += OnChanged;

            // A watcher that dies (the folder was removed, the buffer overflowed) should not take
            // the application with it; the user can still reload by hand.
            _watcher.Error += (_, e) => Logger.Log($"Stopped watching the mods folder: {e.GetException().Message}");

            _watcher.EnableRaisingEvents = true;
            return true;
        }
        catch (Exception exception)
        {
            Logger.Log($"Could not watch the mods folder for changes: {exception.Message}");
            _watcher?.Dispose();
            _watcher = null;
            return false;
        }
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        if (_disposed) return;

        var name = Path.GetFileName(e.Name ?? "");
        if (IgnoredNames.Contains(name, StringComparer.OrdinalIgnoreCase)) return;

        // Ignore anything inside the version backups AIM keeps for itself.
        var relative = (e.Name ?? "").Replace('\\', '/');
        if (relative.StartsWith(".aim", StringComparison.OrdinalIgnoreCase) ||
            relative.Contains("/.aim", StringComparison.OrdinalIgnoreCase)) return;

        RestartSettleTimer();
    }

    private void RestartSettleTimer()
    {
        _settleTimer ??= new Timer(_ =>
        {
            if (_disposed) return;

            try
            {
                _onSettled();
            }
            catch (Exception exception)
            {
                Logger.Log($"Mods folder reload failed: {exception.Message}");
            }
        });

        _settleTimer.Change(_settleTime, Timeout.InfiniteTimeSpan);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }

        _settleTimer?.Dispose();
        _settleTimer = null;
    }
}
