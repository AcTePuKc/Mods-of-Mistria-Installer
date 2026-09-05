using Garethp.ModsOfMistriaInstallerLib.GmlMods;
using Garethp.ModsOfMistriaInstallerLib.ModTypes;
using Newtonsoft.Json.Linq;

namespace Garethp.ModsOfMistriaInstallerLib.Bindings;

/// <summary>Where a binding AIM found actually came from.</summary>
public enum BindingSource
{
    /// <summary>The user's own choice, read from the mod's settings file.</summary>
    Configured,

    /// <summary>
    /// The default compiled into the mod's source, used because the mod has never written a
    /// settings file. What the mod will use until the user changes it in-game.
    /// </summary>
    ModDefault
}

/// <summary>One key or button, and what it belongs to.</summary>
public sealed record ModBindingEntry(
    string ModId,
    string ModName,
    string Field,
    string Value,
    BindingSource Source)
{
    /// <summary>The settings file this came from, or null for a compiled-in default.</summary>
    public ModConfigFile? File { get; init; }

    /// <summary>Null when the stored value is not a name MMAPI will accept.</summary>
    public MmapiBinding? Binding { get; init; }

    /// <summary>Identity across sessions and updates: the mod's setting, not its current value.</summary>
    public string FeatureKey => $"{ModId}|{File?.FileName ?? "gml"}|{Field}";

    /// <summary>A readable name for the setting, e.g. "gamepad_hotkey" as "Gamepad hotkey".</summary>
    public string FieldLabel
    {
        get
        {
            var words = Field.Replace('_', ' ').Trim();
            return words.Length == 0 ? Field : char.ToUpperInvariant(words[0]) + words[1..];
        }
    }

    public bool IsEditable => File is not null;
}

/// <summary>
/// Finds every key and controller button the installed mods have bound.
///
/// It reads the mod's settings file first, because that holds the binding the user actually chose.
/// Only when a mod has never written one does it fall back to the default declared in the mod's
/// source - which is what the game will use until the user changes it, so it belongs in the list,
/// but marked as a default rather than a decision.
///
/// This distinction matters more than it sounds. Scanning only the source, as the shortcut check
/// used to, reports clashes between two mods' <em>defaults</em> - including for a pair the user
/// separated in-game months ago.
/// </summary>
public static class BindingScanner
{
    /// <summary>
    /// Field names that can only mean a binding. These are taken at their word even when the value
    /// is one the game would reject, because a broken keybind is exactly what the user needs told.
    /// </summary>
    private static readonly string[] StrongMarkers = ["keybind", "hotkey", "binding", "shortcut"];

    /// <summary>
    /// Field names that <em>might</em> mean a binding. A mod with <c>{"marker_colour": "B"}</c>
    /// would otherwise have its colour listed as a keybind - "B" being a perfectly good key name -
    /// so these only count when the value really does parse as one.
    /// </summary>
    private static readonly string[] WeakMarkers = ["_key", "key_", "_pad", "button"];

    private static bool LooksLikeBindingField(string field, bool valueParses)
    {
        if (StrongMarkers.Any(marker => field.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            return true;

        if (!valueParses) return false;

        return field.Equals("key", StringComparison.OrdinalIgnoreCase) ||
               WeakMarkers.Any(marker => field.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Every binding across the given mods, configured values winning over compiled-in defaults.
    /// </summary>
    /// <param name="mods">The mods to look at, normally the enabled ones.</param>
    /// <param name="store">The game's settings store, or null when the game has never been run.</param>
    public static List<ModBindingEntry> Scan(IReadOnlyList<IMod> mods, ModDataStore? store)
    {
        var configured = store?.ReadAll() ?? [];
        var entries = new List<ModBindingEntry>();

        // A mod's settings folder is named after the id it gives MMAPI, which is usually neither
        // its manifest id nor its folder name. Getting this mapping wrong lists the same mod twice
        // - once under a raw id carrying the real binding, once under its proper name carrying the
        // default - and then reports it as clashing with itself.
        var byName = new Dictionary<string, IMod>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in mods)
        foreach (var name in MmapiModIdentity.NamesFor(mod))
            byName.TryAdd(name, mod);

        // Tracked by instance, not by name: a mod is "configured" once any of its aliases matched,
        // whichever one that was.
        var modsWithConfig = new HashSet<IMod>();

        foreach (var file in configured)
        {
            var owner = byName.GetValueOrDefault(file.ModId);
            var name = owner?.GetName() ?? file.ModId;

            foreach (var property in file.Content.Properties())
            {
                if (property.Value.Type != JTokenType.String) continue;

                var value = property.Value.Value<string>() ?? "";

                // Strict, unlike the compiled-in defaults below, and deliberately so. A configured
                // value is read back through MMAPI's own case-sensitive parser, so "f7" really is
                // rejected there and the mod really does fall back - the user needs telling. A
                // mod's default is read by the mod's own converter, which may well accept "f7", so
                // judging it by MMAPI's rules would raise a warning about nothing.
                var binding = MmapiBindingVocabulary.TryParse(value);
                if (!LooksLikeBindingField(property.Name, binding is not null)) continue;

                entries.Add(new ModBindingEntry(
                    owner?.GetId() ?? file.ModId, name, property.Name, value, BindingSource.Configured)
                {
                    File = file,
                    Binding = binding
                });

                if (owner is not null) modsWithConfig.Add(owner);
            }
        }

        foreach (var mod in mods)
        {
            if (modsWithConfig.Contains(mod)) continue;
            entries.AddRange(DefaultsOf(mod));
        }

        return entries
            .OrderBy(entry => entry.ModName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Field, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// The bindings a mod declares in its own source, for mods that have not written settings yet.
    ///
    /// Only <c>#macro NAME "BINDING"</c> declarations count, and only where the value is a name
    /// MMAPI accepts - which conveniently excludes the version strings and mod ids that share the
    /// same declaration shape.
    /// </summary>
    private static IEnumerable<ModBindingEntry> DefaultsOf(IMod mod)
    {
        Dictionary<string, string> sources;
        try { sources = HotkeyConflictDetector.ReadGmlSources(mod); }
        catch (Exception exception)
        {
            Logger.Log($"Could not read the defaults of {mod.GetId()}: {exception.Message}");
            yield break;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in sources.Values)
        foreach (var (macro, value) in HotkeyConflictDetector.DeclaredBindings(source))
        {
            var binding = MmapiBindingVocabulary.TryParse(value)
                          ?? MmapiBindingVocabulary.TryParse(MmapiBindingVocabulary.Normalize(value) ?? "");
            if (binding is null) continue;
            if (!seen.Add(macro)) continue;

            yield return new ModBindingEntry(
                mod.GetId(), mod.GetName(), macro, binding.ToString(), BindingSource.ModDefault)
            {
                Binding = binding
            };
        }
    }

    /// <summary>
    /// Groups the entries that fight over the same input, so the manager can colour them and say
    /// who else is on that key.
    /// </summary>
    public static Dictionary<ModBindingEntry, List<ModBindingEntry>> FindOverlaps(
        IReadOnlyList<ModBindingEntry> entries)
    {
        var overlaps = new Dictionary<ModBindingEntry, List<ModBindingEntry>>();

        for (var i = 0; i < entries.Count; i++)
        {
            var left = entries[i];
            if (left.Binding is null) continue;

            for (var j = i + 1; j < entries.Count; j++)
            {
                var right = entries[j];
                if (right.Binding is null) continue;

                // A mod using one key for two of its own settings is its own business, and often
                // deliberate - the same key opening and closing a panel, for instance.
                if (left.ModId.Equals(right.ModId, StringComparison.OrdinalIgnoreCase)) continue;
                if (left.Binding.OverlapWith(right.Binding) == BindingOverlap.None) continue;

                Pair(left, right);
                Pair(right, left);
            }
        }

        return overlaps;

        void Pair(ModBindingEntry key, ModBindingEntry other)
        {
            if (!overlaps.TryGetValue(key, out var others))
                overlaps[key] = others = [];
            others.Add(other);
        }
    }
}
