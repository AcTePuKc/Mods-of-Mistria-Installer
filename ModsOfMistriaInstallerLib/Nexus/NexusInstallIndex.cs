using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Garethp.ModsOfMistriaInstallerLib.Nexus;

/// <summary>What AIM knows about where a mod came from on Nexus.</summary>
public record NexusInstallRecord(
    string Game,
    int ModId,
    int FileId,
    string FileName,
    string? Version,
    DateTimeOffset InstalledAt,
    bool Frozen = false)
{
    public string PageUrl => $"https://www.nexusmods.com/{Game}/mods/{ModId}";

    public string FilesPageUrl => $"{PageUrl}?tab=files";
}

/// <summary>
/// Remembers which Nexus mod and file each installed mod folder came from, so AIM can check it for
/// updates later, open its page, or leave it alone when the user freezes it.
///
/// It lives beside the profiles in the mods folder (<c>aim_nexus.json</c>) rather than inside each
/// mod folder, because updating a mod replaces that folder wholesale - provenance written inside it
/// would be destroyed by the very operation that needs to record it.
/// </summary>
public class NexusInstallIndex
{
    public const string FileName = "aim_nexus.json";

    private readonly string _path;
    private JObject _data;

    public NexusInstallIndex(string modsLocation)
    {
        _path = Path.Combine(modsLocation, FileName);
        _data = Load();
    }

    /// <summary>
    /// The key for a mod. Mods arrive as a folder or as a .zip/.rar, and the same mod may switch
    /// between the two, so the extension is dropped and case is ignored.
    /// </summary>
    public static string KeyFor(string sourcePath)
    {
        var leaf = Path.GetFileName(sourcePath.TrimEnd('/', '\\'));
        if (leaf.Length == 0) return "";

        return (ModArchiveInstaller.LooksLikeArchive(leaf)
            ? Path.GetFileNameWithoutExtension(leaf)
            : leaf).ToLowerInvariant();
    }

    public NexusInstallRecord? Get(string sourcePath)
    {
        var key = KeyFor(sourcePath);
        if (key.Length == 0 || Mods[key] is not JObject entry) return null;

        var game = entry.Value<string>("game");
        var modId = entry.Value<int?>("modId");
        var fileId = entry.Value<int?>("fileId");
        if (game is null || modId is null || fileId is null) return null;

        return new NexusInstallRecord(
            game,
            modId.Value,
            fileId.Value,
            entry.Value<string>("fileName") ?? "",
            entry.Value<string>("version"),
            ReadTimestamp(entry, "installedAt"),
            entry.Value<bool?>("frozen") ?? false);
    }

    public void Record(string sourcePath, NexusInstallRecord record)
    {
        var key = KeyFor(sourcePath);
        if (key.Length == 0) return;

        // A re-download must not clear a freeze the user set: freezing is about the mod, not about
        // the particular copy of it that happens to be installed.
        var frozen = record.Frozen || (Mods[key] as JObject)?.Value<bool?>("frozen") == true;

        Mods[key] = new JObject
        {
            ["game"] = record.Game,
            ["modId"] = record.ModId,
            ["fileId"] = record.FileId,
            ["fileName"] = record.FileName,
            ["version"] = record.Version,
            // Written as a round-trip string rather than a date: Json.NET parses a bare timestamp
            // back into a DateTime, which cannot be cast to DateTimeOffset on the way out.
            ["installedAt"] = record.InstalledAt.ToString("O"),
            ["frozen"] = frozen
        };

        Save();
    }

    /// <summary>
    /// Reads the freeze flag straight off the entry rather than through <see cref="Get"/>: a mod
    /// the user installed by hand has a freeze but no Nexus ids, and Get requires the ids.
    /// </summary>
    private static DateTimeOffset ReadTimestamp(JObject entry, string field)
    {
        var raw = entry.Value<string>(field);

        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var value)
            ? value
            : DateTimeOffset.MinValue;
    }

    public bool IsFrozen(string sourcePath)
    {
        var key = KeyFor(sourcePath);
        return key.Length > 0 && (Mods[key] as JObject)?.Value<bool?>("frozen") == true;
    }

    public void SetFrozen(string sourcePath, bool frozen)
    {
        var key = KeyFor(sourcePath);
        if (key.Length == 0) return;

        if (Mods[key] is not JObject entry)
        {
            // A mod AIM did not install can still be frozen - that is how the user says "leave this
            // one alone" for a mod they patched by hand.
            entry = new JObject();
            Mods[key] = entry;
        }

        entry["frozen"] = frozen;
        Save();
    }

    public void Forget(string sourcePath)
    {
        var key = KeyFor(sourcePath);
        if (key.Length == 0) return;

        Mods.Remove(key);
        Save();
    }

    /// <summary>
    /// Pulls a Nexus mod id out of a URL in a mod's manifest, so mods installed by hand can still
    /// be checked for updates and opened on their page. Matches the normal page form,
    /// https://www.nexusmods.com/fieldsofmistria/mods/175, with or without a query or file tab.
    /// </summary>
    public static bool TryReadNexusUrl(string? url, out string game, out int modId)
    {
        game = "";
        modId = 0;
        if (string.IsNullOrWhiteSpace(url)) return false;

        var match = Regex.Match(url,
            @"^https?://(?:www\.)?nexusmods\.com/(?<game>[a-z0-9\-]+)/mods/(?<mod>\d+)",
            RegexOptions.IgnoreCase);

        if (!match.Success) return false;

        game = match.Groups["game"].Value.ToLowerInvariant();
        return int.TryParse(match.Groups["mod"].Value, out modId) && modId > 0;
    }

    // ── Storage ──────────────────────────────────────────────────────────────────

    private JObject Mods
    {
        get
        {
            if (_data["mods"] is not JObject mods)
            {
                mods = new JObject();
                _data["mods"] = mods;
            }

            return mods;
        }
    }

    private JObject Load()
    {
        try
        {
            if (File.Exists(_path)) return JObject.Parse(File.ReadAllText(_path));
        }
        catch (Exception e)
        {
            Logger.Log($"Could not read {FileName}, starting fresh: {e.Message}");
        }

        return new JObject();
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(_path, _data.ToString());
        }
        catch (Exception e)
        {
            Logger.Log($"Could not save {FileName}: {e.Message}");
        }
    }
}
