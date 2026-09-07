using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Garethp.ModsOfMistriaInstallerLib.Crash;

/// <summary>
/// AIM's copy of every crash the game has had, and the load order each one happened under.
///
/// The game keeps exactly one crash file and overwrites it, so by the time a user thinks to look
/// at it the crash they wanted is usually two crashes ago. Worse, the file says nothing about which
/// mods were installed when it was written, which is the one fact the whole diagnosis turns on -
/// AIM can read the current mod list, but the crash may predate the last three things the user did
/// to it.
///
/// So this exists: a folder of captured crashes, each one the game's own JSON with an <c>aim</c>
/// block added recording the mods in load order, the install those mods were built into, and when
/// AIM noticed. Captures are deduplicated by content, so watching the file and launching the game
/// through AIM cannot file the same crash twice, and the folder is capped so it cannot grow without
/// bound on a machine that crashes every session.
/// </summary>
public sealed class CrashArchive
{
    /// <summary>Where the game leaves its own most recent crash. Overwritten every time.</summary>
    public static string GameCrashFile => Path.Combine(GameDataFolder, "error_log.json");

    /// <summary>Fields of Mistria's data folder, which is also where the mods write their logs.</summary>
    public static string GameDataFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FieldsOfMistria");

    private const int Keep = 40;

    /// <summary>
    /// Which AIM wrote a capture. Read from the assembly rather than taken from the GUI's AppInfo,
    /// which this project cannot see - and worth recording because "which installer built that
    /// archive" is the first question about a crash nobody can reproduce.
    /// </summary>
    private static string AimVersion =>
        typeof(CrashArchive).Assembly.GetName().Version?.ToString() ?? "unknown";

    private readonly string _folder;

    public CrashArchive()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AIM", "game-crashes"))
    {
    }

    public CrashArchive(string folder) => _folder = folder;

    public string Folder => _folder;

    /// <summary>
    /// Every crash AIM has captured plus, if it is not already one of them, whatever the game
    /// currently has on disk. Newest first.
    ///
    /// The live file is included rather than only the captures because AIM may have been installed
    /// after the crash, or never have been running when it happened, and a crash it did not witness
    /// is still the crash the user is asking about.
    /// </summary>
    public IReadOnlyList<GameCrashLog> All()
    {
        var found = new List<GameCrashLog>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var path in Files())
        {
            var log = GameCrashLog.Read(path);
            if (log is null) continue;

            // Two captures of one crash differ only in when AIM noticed, and the user does not want
            // to read it twice. The load order is part of the key, though: the same error under a
            // different set of mods is a different piece of evidence and worth keeping apart.
            if (seen.Add(log.StableKey + "|" + string.Join(",", log.ModsAtLaunch ?? []))) found.Add(log);
        }

        var live = GameCrashLog.Read(GameCrashFile);
        if (live is not null && seen.Add(live.StableKey + "|")) found.Add(live);

        return found.OrderByDescending(log => log.When).ToList();
    }

    /// <summary>The most recent crash from anywhere, or null when the game has never crashed here.</summary>
    public GameCrashLog? Latest() => All().FirstOrDefault();

    /// <summary>
    /// When the game last wrote its crash file, or null if it never has.
    ///
    /// The one fact that says whether a run crashed. The game writes this file as it dies, so a
    /// file older than the run cannot be that run's - and no amount of reading its contents will
    /// say so, because the contents of last week's crash and this minute's can be identical.
    /// </summary>
    public static DateTimeOffset? CrashFileWrittenAt()
    {
        try
        {
            return File.Exists(GameCrashFile)
                ? new DateTimeOffset(File.GetLastWriteTimeUtc(GameCrashFile), TimeSpan.Zero)
                : null;
        }
        catch (Exception exception)
        {
            Logger.Log($"Could not read the crash file's timestamp: {exception.Message}");
            return null;
        }
    }

    private IEnumerable<string> Files()
    {
        if (!Directory.Exists(_folder)) return [];

        try
        {
            return Directory.GetFiles(_folder, "crash-*.json")
                .OrderByDescending(path => path, StringComparer.Ordinal);
        }
        catch (Exception exception)
        {
            Logger.Log($"Could not list captured crashes: {exception.Message}");
            return [];
        }
    }

    /// <summary>
    /// Takes a copy of the game's crash file, stamped with the mods that were installed.
    /// </summary>
    /// <param name="mods">Enabled mods in load order, as "id version" strings.</param>
    /// <param name="installedAt">When the archive those mods were built into was published.</param>
    /// <returns>The captured file's path, or null when there was nothing new to capture.</returns>
    public string? Capture(
        IReadOnlyList<string> mods,
        DateTimeOffset? installedAt,
        string? note = null,
        DateTimeOffset? onlyIfCrashedSince = null)
    {
        try
        {
            if (!File.Exists(GameCrashFile)) return null;

            var text = File.ReadAllText(GameCrashFile);
            if (string.IsNullOrWhiteSpace(text)) return null;

            var when = new DateTimeOffset(File.GetLastWriteTimeUtc(GameCrashFile), TimeSpan.Zero);

            // Nothing here belongs to the caller's run. Without this a supervised run stamps the
            // crash file it found lying there with the mod list it was testing - a different list,
            // so the content-plus-load-order fingerprint below does not recognise it as a duplicate
            // - and files last week's crash as a crash that happened just now. The check then reads
            // its own capture back as "the game crashed again", which turns the mod that actually
            // caused the crash into the mod that was ruled out.
            if (onlyIfCrashedSince is not null && when <= onlyIfCrashedSince) return null;
            var fingerprint = Fingerprint(text, mods);

            Directory.CreateDirectory(_folder);

            // Already have it. This is the normal case when both the watcher and a run through AIM
            // see the same crash, and it must be cheap and silent rather than a duplicate file.
            foreach (var existing in Files())
                if (File.Exists(existing) && Fingerprint(File.ReadAllText(existing), null) == fingerprint)
                    return null;

            JObject root;
            try { root = JToken.Parse(text) as JObject ?? new JObject { ["report"] = text }; }
            catch { root = new JObject { ["report"] = text }; }

            root["aim"] = new JObject
            {
                ["capturedAt"] = DateTimeOffset.UtcNow.ToString("o"),
                ["crashedAt"] = when.ToString("o"),
                ["installedAt"] = installedAt?.ToString("o"),
                ["installerVersion"] = AimVersion,
                ["note"] = note,
                ["mods"] = new JArray(mods)
            };

            var path = Path.Combine(_folder, $"crash-{when.UtcDateTime:yyyyMMdd-HHmmss}-{fingerprint[..8]}.json");
            File.WriteAllText(path, root.ToString(Formatting.Indented));

            Prune();
            return path;
        }
        catch (Exception exception)
        {
            Logger.Log($"Could not capture the game's crash log: {exception.Message}");
            return null;
        }
    }

    /// <summary>
    /// Identity of a captured crash: the report text and the load order, and nothing else.
    ///
    /// Deliberately not the whole file. The game's context block carries the window size and the
    /// player's settings, and re-reading the same crash after the user has alt-tabbed would
    /// otherwise look like a new one.
    /// </summary>
    private static string Fingerprint(string text, IReadOnlyList<string>? mods)
    {
        var report = text;
        var order = mods is null ? "" : string.Join(",", mods);

        try
        {
            if (JToken.Parse(text) is JObject root)
            {
                report = root.Value<string>("report") ?? text;

                if (mods is null && root["aim"]?["mods"] is JArray list)
                    order = string.Join(",", list.Select(entry => entry.ToString()));
            }
        }
        catch
        {
            // Unparseable text is its own fingerprint; nothing to recover.
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(report + " " + order)));
    }

    /// <summary>
    /// Keeps the newest <see cref="Keep"/> captures. A crash log is small, but a mod that crashes
    /// on boot can produce one per launch attempt and there is no reason to keep a hundred of them.
    /// </summary>
    private void Prune()
    {
        try
        {
            foreach (var path in Files().Skip(Keep)) File.Delete(path);
        }
        catch (Exception exception)
        {
            Logger.Log($"Could not prune the crash archive: {exception.Message}");
        }
    }
}
