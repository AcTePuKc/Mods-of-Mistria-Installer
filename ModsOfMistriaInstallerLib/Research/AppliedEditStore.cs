using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Garethp.ModsOfMistriaInstallerLib.Research;

/// <summary>One change AIM made to the inside of a mod, and the restore point taken before it.</summary>
/// <param name="BackupPath">
/// The snapshot in <c>.aim-backups</c> holding the mod exactly as it was. This is the same kind of
/// restore point an update takes, so it appears in the row's version dropdown and can be put back
/// the same way.
/// </param>
public sealed record AppliedEdit(
    string ModId,
    string Summary,
    IReadOnlyList<string> Files,
    string? BackupPath,
    DateTimeOffset AppliedAt)
{
    public string Describe() =>
        $"{Summary} ({AppliedAt.LocalDateTime.ToString("g", CultureInfo.CurrentCulture)})";
}

/// <summary>
/// Remembers which mods AIM has edited.
///
/// AIM changing a file inside somebody else's mod is a serious thing to do, and the danger is not
/// that it goes wrong at the time - it is that it is silently forgotten. Six weeks later the mod
/// misbehaves, the user opens an issue with the author, and neither of them knows the files on disk
/// are no longer the ones that were downloaded. So every edit is recorded here and shown on the
/// mod's row, and nothing is edited without a full copy of the mod being taken first.
///
/// It lives beside the profiles, the Nexus index and the dismissed issues in the mods folder, for
/// the same reason those do: a mod folder is replaced wholesale by an update, and a record kept
/// inside one would vanish exactly when it became most misleading to have lost it. An update also
/// discards the edit itself, which is why <see cref="Forget"/> exists.
/// </summary>
public sealed class AppliedEditStore
{
    public const string FileName = "aim_applied_edits.json";

    private readonly string _path;
    private readonly Dictionary<string, List<AppliedEdit>> _edits = new(StringComparer.OrdinalIgnoreCase);

    public AppliedEditStore(string modsLocation)
    {
        _path = Path.Combine(modsLocation, FileName);
        Load();
    }

    /// <summary>True when AIM has changed anything inside this mod. Drives the row's marker.</summary>
    public bool WasEdited(string modId) => Edits(modId).Count > 0;

    /// <summary>What AIM changed in this mod, newest first.</summary>
    public IReadOnlyList<AppliedEdit> Edits(string modId) =>
        modId.Length > 0 && _edits.TryGetValue(modId, out var list) ? list : [];

    /// <summary>A one-line description of every edit, for the marker's tooltip.</summary>
    public string DescribeEdits(string modId) =>
        string.Join("\n", Edits(modId).Select(edit => "• " + edit.Describe()));

    public void Record(AppliedEdit edit)
    {
        if (string.IsNullOrEmpty(edit.ModId)) return;

        if (!_edits.TryGetValue(edit.ModId, out var list))
            _edits[edit.ModId] = list = [];

        list.Insert(0, edit);
        Save();
    }

    /// <summary>
    /// Drops the record for a mod. Called when the mod is updated, removed, or restored from a
    /// backup - in all three cases the edited files are gone and the marker would be a lie.
    /// </summary>
    public void Forget(string modId)
    {
        if (_edits.Remove(modId)) Save();
    }

    // ── Persistence ──────────────────────────────────────────────────────────────

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;

            using var reader = new JsonTextReader(new StringReader(File.ReadAllText(_path)))
            {
                DateParseHandling = DateParseHandling.None
            };

            if (JToken.ReadFrom(reader) is not JObject root || root["mods"] is not JObject mods) return;

            foreach (var entry in mods.Properties())
            {
                // One unreadable entry loses that entry, not the file. The whole point of this
                // record is to survive; discarding all of it over one bad row would defeat it.
                try
                {
                    if (entry.Value is not JArray list) continue;

                    var edits = list.OfType<JObject>().Select(item => new AppliedEdit(
                            entry.Name,
                            item.Value<string>("summary") ?? "AIM edited this mod",
                            (item["files"] as JArray ?? []).Select(file => file.ToString()).ToList(),
                            item.Value<string>("backup"),
                            ReadTimestamp(item)))
                        .ToList();

                    if (edits.Count > 0) _edits[entry.Name] = edits;
                }
                catch (Exception exception)
                {
                    Logger.Log($"Skipped an unreadable entry in {FileName}: {exception.Message}");
                }
            }
        }
        catch (Exception exception)
        {
            Logger.Log($"Could not read {FileName}: {exception.Message}");
            _edits.Clear();
        }
    }

    private static DateTimeOffset ReadTimestamp(JObject entry) =>
        DateTimeOffset.TryParse(entry.Value<string>("appliedAt"), CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var value)
            ? value
            : DateTimeOffset.UtcNow;

    private void Save()
    {
        try
        {
            var mods = new JObject();

            foreach (var (modId, edits) in _edits)
            {
                var list = new JArray();

                foreach (var edit in edits)
                {
                    var item = new JObject
                    {
                        ["summary"] = edit.Summary,
                        ["appliedAt"] = edit.AppliedAt.ToString("o"),
                        ["files"] = new JArray(edit.Files)
                    };

                    if (!string.IsNullOrWhiteSpace(edit.BackupPath)) item["backup"] = edit.BackupPath;
                    list.Add(item);
                }

                mods[modId] = list;
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
}
