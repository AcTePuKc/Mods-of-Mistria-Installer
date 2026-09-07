using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Garethp.ModsOfMistriaInstallerLib.Bindings;

/// <summary>A binding the user chose that a mod has since changed out from under them.</summary>
public sealed record BindingDrift(ModBindingEntry Current, string Remembered)
{
    public string ModName => Current.ModName;
    public string Field => Current.FieldLabel;
    public string Now => Current.Value;
}

/// <summary>
/// Remembers the keys and controller buttons the user chose, so a mod cannot quietly take them
/// back.
///
/// Mod settings live outside the mods folder, so replacing a mod folder during an update does not
/// lose a binding. What loses one is the mod rewriting its own settings: a
/// <c>__config_version</c> bump whose migration writes defaults, or MMAPI rejecting a value it no
/// longer recognises and falling back. The user's answer is then gone with no notice.
///
/// The rule this implements is the one that makes it safe: a remembered binding is only ever
/// offered back when the setting it belongs to <em>still exists</em>. If the update removed the
/// feature, the setting disappears from the mod's config, the vault drops it, and nothing is
/// re-applied to a mod that no longer has anywhere to put it.
///
/// It lives beside the profiles in the mods folder, so it travels with the mod set it describes.
/// </summary>
public sealed class BindingVault
{
    public const string FileName = "aim_bindings.json";

    private readonly string _path;

    // Keyed by ModBindingEntry.FeatureKey - mod, settings file, field. Never by value: the whole
    // point is to notice when the value changes.
    private readonly Dictionary<string, VaultEntry> _entries = new(StringComparer.Ordinal);

    private sealed record VaultEntry(string Value, string ModName, string Field, DateTimeOffset ChosenAt);

    public BindingVault(string modsLocation)
    {
        _path = Path.Combine(modsLocation, FileName);
        Load();
    }

    public int Count => _entries.Count;

    public string? Remembered(string featureKey) =>
        _entries.TryGetValue(featureKey, out var entry) ? entry.Value : null;

    /// <summary>
    /// Records what a setting is currently on, for settings the user can actually change.
    ///
    /// Compiled-in defaults are deliberately not recorded. A mod's default is the author's choice,
    /// not the user's, and restoring one over a deliberate change in a new version would be AIM
    /// overriding the mod for no reason.
    /// </summary>
    public void Remember(IReadOnlyList<ModBindingEntry> entries)
    {
        var changed = false;

        foreach (var entry in entries.Where(entry => entry.Source == BindingSource.Configured))
        {
            if (_entries.TryGetValue(entry.FeatureKey, out var existing) && existing.Value == entry.Value)
                continue;

            _entries[entry.FeatureKey] =
                new VaultEntry(entry.Value, entry.ModName, entry.Field, DateTimeOffset.UtcNow);
            changed = true;
        }

        if (changed) Save();
    }

    /// <summary>
    /// Settings that have moved off what the user chose, and could be put back.
    ///
    /// Only settings still present in the scan are considered. A setting that has vanished - the
    /// feature was removed - is forgotten instead, which is the difference between restoring a
    /// binding and resurrecting one.
    /// </summary>
    public List<BindingDrift> FindDrift(IReadOnlyList<ModBindingEntry> entries)
    {
        var drifted = new List<BindingDrift>();
        var present = new HashSet<string>(StringComparer.Ordinal);

        // Only mods the scan actually covered may have their memories dropped. The caller passes
        // the *enabled* mods, so anything else in the vault belongs to a mod that is merely
        // switched off - forgetting those would delete the user's keybinds for every mod they
        // temporarily unticked, which is precisely what this class exists to prevent.
        var scanned = new HashSet<string>(entries.Select(entry => entry.ModId), StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries.Where(entry => entry.Source == BindingSource.Configured))
        {
            present.Add(entry.FeatureKey);

            if (!_entries.TryGetValue(entry.FeatureKey, out var remembered)) continue;
            if (remembered.Value == entry.Value) continue;

            drifted.Add(new BindingDrift(entry, remembered.Value));
        }

        Forget(_entries.Keys
            .Where(key => !present.Contains(key) && scanned.Contains(ModIdOf(key)))
            .ToList());

        return drifted;
    }

    /// <summary>The mod half of a feature key, which is everything before the first separator.</summary>
    private static string ModIdOf(string featureKey)
    {
        var separator = featureKey.IndexOf('|');
        return separator < 0 ? featureKey : featureKey[..separator];
    }

    /// <summary>
    /// Drops remembered bindings whose setting is gone.
    ///
    /// Called with the keys of settings that were looked for and not found. A mod that dropped a
    /// feature takes that field with it, so the remembered value has nowhere to go; keeping it
    /// would only grow the file and, worse, would let AIM re-apply a key to a feature that no
    /// longer exists.
    /// </summary>
    private void Forget(IReadOnlyList<string> keys)
    {
        if (keys.Count == 0) return;

        foreach (var key in keys) _entries.Remove(key);
        Save();
    }

    /// <summary>
    /// Puts one remembered binding back into the mod's settings file.
    /// </summary>
    /// <returns>False when the file could not be written.</returns>
    public bool Restore(BindingDrift drift)
    {
        if (drift.Current.File is null) return false;
        if (!ModDataStore.WriteField(drift.Current.File, drift.Current.Field, drift.Remembered))
            return false;

        // The vault already holds this value; re-stamping the time would make it look like a fresh
        // choice, which matters only for readability of the file but costs nothing to get right.
        Logger.Log($"Restored {drift.ModName} {drift.Field} to {drift.Remembered}.");
        return true;
    }

    // ── Persistence ──────────────────────────────────────────────────────────────

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            if (ReadRoot(_path)?["bindings"] is not JObject bindings) return;

            foreach (var property in bindings.Properties())
            {
                // One unreadable entry costs that entry. Losing every remembered keybind because a
                // single stamp is malformed would defeat the point of remembering them.
                try
                {
                    if (property.Value is not JObject value) continue;

                    var binding = value.Value<string>("value");
                    if (string.IsNullOrWhiteSpace(binding)) continue;

                    _entries[property.Name] = new VaultEntry(
                        binding,
                        value.Value<string>("mod") ?? "",
                        value.Value<string>("field") ?? "",
                        ReadTimestamp(value, "chosenAt"));
                }
                catch (Exception exception)
                {
                    Logger.Log($"Skipped an unreadable entry in {FileName}: {exception.Message}");
                }
            }
        }
        catch (Exception exception)
        {
            // Losing the remembered bindings is a nuisance; refusing to load the mod list because
            // of it would be worse.
            Logger.Log($"Could not read {FileName}: {exception.Message}");
            _entries.Clear();
        }
    }

    /// <summary>
    /// Parses the file with timestamps left as text, so <see cref="ReadTimestamp"/> sees exactly
    /// what was written rather than a Date token Newtonsoft has already reinterpreted.
    /// </summary>
    private static JObject? ReadRoot(string path)
    {
        using var reader = new JsonTextReader(new StringReader(File.ReadAllText(path)))
        {
            DateParseHandling = DateParseHandling.None
        };

        return JToken.ReadFrom(reader) as JObject;
    }

    /// <summary>
    /// Reads an ISO-8601 stamp as text and parses it here.
    ///
    /// Not <c>Value&lt;DateTimeOffset?&gt;</c>: that throws when Newtonsoft has already turned the
    /// string into a Date token.
    /// </summary>
    private static DateTimeOffset ReadTimestamp(JObject entry, string field)
    {
        var raw = entry.Value<string>(field);

        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var value)
            ? value
            : DateTimeOffset.UtcNow;
    }

    private void Save()
    {
        try
        {
            var bindings = new JObject();
            foreach (var (key, entry) in _entries)
            {
                bindings[key] = new JObject
                {
                    ["value"] = entry.Value,
                    ["mod"] = entry.ModName,
                    ["field"] = entry.Field,
                    ["chosenAt"] = entry.ChosenAt.ToString("o")
                };
            }

            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            File.WriteAllText(_path, new JObject { ["bindings"] = bindings }.ToString(Formatting.Indented));
        }
        catch (Exception exception)
        {
            Logger.Log($"Could not write {FileName}: {exception.Message}");
        }
    }
}
