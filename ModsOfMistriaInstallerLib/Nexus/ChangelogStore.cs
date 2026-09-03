using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Garethp.ModsOfMistriaInstallerLib.Nexus;

/// <summary>
/// Keeps mods' release notes on disk so reading them costs nothing after the first time.
///
/// Without this, showing a changelog on hover would be one Nexus call per mouse-over, and a mod
/// list of 150 would burn a day's rate limit in an afternoon. Notes also barely change: a version's
/// text is written once and never edited, so the only reason to look again is a new release.
///
/// It lives beside the other AIM state in the mods folder, and is a cache in the honest sense -
/// deleting the file loses nothing but the next fetch.
/// </summary>
public sealed class ChangelogStore
{
    public const string FileName = "aim_changelogs.json";

    /// <summary>
    /// How long a fetch stays good when the mod has not moved.
    ///
    /// Authors do sometimes add notes to a release after publishing it, so an entry does not live
    /// for ever - but a week is long enough that a normal session never refetches.
    /// </summary>
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);

    private readonly string _path;
    private readonly Dictionary<int, CacheEntry> _byModId = [];

    private sealed record CacheEntry(
        string FetchedForVersion,
        DateTimeOffset FetchedAt,
        List<ModChangelogEntry> Entries);

    public ChangelogStore(string modsLocation)
    {
        _path = Path.Combine(modsLocation, FileName);
        Load();
    }

    /// <summary>
    /// The cached notes for a mod, or null when there is nothing usable.
    /// </summary>
    /// <param name="currentVersion">
    /// The version AIM currently has installed. A cache taken for a different version is stale by
    /// definition - the mod has been updated since, and the new release's notes are the point.
    /// </param>
    public List<ModChangelogEntry>? Get(int modId, string? currentVersion)
    {
        if (!_byModId.TryGetValue(modId, out var entry)) return null;

        if (!string.Equals(entry.FetchedForVersion, currentVersion ?? "", StringComparison.OrdinalIgnoreCase))
            return null;

        return DateTimeOffset.UtcNow - entry.FetchedAt > MaxAge ? null : entry.Entries;
    }

    public void Put(int modId, string? currentVersion, List<ModChangelogEntry> entries)
    {
        _byModId[modId] = new CacheEntry(currentVersion ?? "", DateTimeOffset.UtcNow, entries);
        Save();
    }

    // ── Persistence ──────────────────────────────────────────────────────────────

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            if (ReadRoot(_path)?["mods"] is not JObject mods) return;

            foreach (var property in mods.Properties())
            {
                // One unreadable entry costs that entry, not the file.
                try
                {
                    if (!int.TryParse(property.Name, out var modId)) continue;
                    if (property.Value is not JObject value) continue;

                    var entries = new List<ModChangelogEntry>();
                    if (value["entries"] is JArray list)
                    {
                        foreach (var item in list.OfType<JObject>())
                        {
                            var version = item.Value<string>("version");
                            if (string.IsNullOrWhiteSpace(version)) continue;

                            var lines = (item["lines"] as JArray ?? [])
                                .Select(line => line.ToString())
                                .Where(line => line.Length > 0)
                                .ToList();

                            if (lines.Count > 0) entries.Add(new ModChangelogEntry(version, lines));
                        }
                    }

                    _byModId[modId] = new CacheEntry(
                        value.Value<string>("fetchedForVersion") ?? "",
                        ReadTimestamp(value, "fetchedAt"),
                        entries);
                }
                catch (Exception exception)
                {
                    Logger.Log($"Skipped an unreadable entry in {FileName}: {exception.Message}");
                }
            }
        }
        catch (Exception exception)
        {
            // A cache that cannot be read is simply a cold cache.
            Logger.Log($"Could not read {FileName}: {exception.Message}");
            _byModId.Clear();
        }
    }

    private void Save()
    {
        try
        {
            var mods = new JObject();
            foreach (var (modId, entry) in _byModId)
            {
                var list = new JArray();
                foreach (var changelog in entry.Entries)
                    list.Add(new JObject
                    {
                        ["version"] = changelog.Version,
                        ["lines"] = new JArray(changelog.Lines)
                    });

                mods[modId.ToString(CultureInfo.InvariantCulture)] = new JObject
                {
                    ["fetchedForVersion"] = entry.FetchedForVersion,
                    ["fetchedAt"] = entry.FetchedAt.ToString("o"),
                    ["entries"] = list
                };
            }

            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            File.WriteAllText(_path, new JObject { ["mods"] = mods }.ToString(Formatting.Indented));
        }
        catch (Exception exception)
        {
            Logger.Log($"Could not write {FileName}: {exception.Message}");
        }
    }

    /// <summary>Parses with timestamps left as text, so they are read exactly as written.</summary>
    private static JObject? ReadRoot(string path)
    {
        using var reader = new JsonTextReader(new StringReader(File.ReadAllText(path)))
        {
            DateParseHandling = DateParseHandling.None
        };

        return JToken.ReadFrom(reader) as JObject;
    }

    private static DateTimeOffset ReadTimestamp(JObject entry, string field)
    {
        var raw = entry.Value<string>(field);

        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var value)
            ? value
            : DateTimeOffset.MinValue;
    }
}
