using System.Text.RegularExpressions;
using Garethp.ModsOfMistriaInstallerLib.ModTypes;

namespace Garethp.ModsOfMistriaInstallerLib.GmlMods;

public sealed record ModHotkeyUsage(string Key, string ModId, string Source, bool Rebindable);

public sealed record ModHotkeyConflict(string Key, IReadOnlyList<ModHotkeyUsage> Usages);

/// <summary>
/// Finds likely keyboard conflicts in GML mods. This is intentionally a
/// warning-only, conservative scan: many mods make their bindings configurable
/// at runtime, so the result must never block installation.
/// </summary>
public static class HotkeyConflictDetector
{
    private static readonly Regex MacroKey = new(
        "#macro\\s+\\w+\\s+\\\"(F(?:[1-9]|1[0-2]))\\\"",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Any <c>#macro NAME "value"</c> declaration. Deliberately unfiltered - the caller decides
    /// which values are bindings, because the same shape also declares mod ids and version strings.
    /// </summary>
    private static readonly Regex MacroDeclaration = new(
        "#macro\\s+(\\w+)\\s+\"([^\"]{1,40})\"",
        RegexOptions.Compiled);

    private static readonly Regex DirectVirtualKey = new(
        @"\bvk_(f(?:[1-9]|1[0-2]))\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// The keys one GML source file appears to claim.
    ///
    /// This is the single definition of "uses a key". <see cref="HotkeyRebinder"/> needs exactly
    /// the same answer to work out which keys are free: if it read the file even slightly
    /// differently it would offer a key this detector considers taken, and the user would trade one
    /// clash for another.
    /// </summary>
    public static HashSet<string> KeysIn(string source)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in MacroKey.Matches(source))
            keys.Add(match.Groups[1].Value.ToUpperInvariant());
        foreach (Match match in DirectVirtualKey.Matches(source))
            keys.Add(match.Groups[1].Value.ToUpperInvariant());

        // Auxiliary Bag generates its default bindings dynamically,
        // so the key names are not present as vk_f1 ... vk_f7 literals.
        if (source.Contains("mah_default_hotkey_name", StringComparison.OrdinalIgnoreCase) &&
            source.Contains("mah_hotkey_slot_7", StringComparison.OrdinalIgnoreCase))
        {
            for (var i = 1; i <= 7; i++) keys.Add($"F{i}");
        }

        return keys;
    }

    /// <summary>
    /// Every <c>#macro NAME "value"</c> pair in one source file, as (name, value).
    ///
    /// Mods declare their default bindings this way - <c>#macro WIKI_DEFAULT_KEY "F6"</c> - and the
    /// macro name is a stable identity for the feature the binding belongs to, which is what lets
    /// AIM tell "the mod moved this setting" from "the mod removed this feature".
    /// </summary>
    public static IEnumerable<(string Macro, string Value)> DeclaredBindings(string source)
    {
        foreach (Match match in MacroDeclaration.Matches(source))
            yield return (match.Groups[1].Value, match.Groups[2].Value);
    }

    /// <summary>The gml/ files of a mod, keyed by their mod-relative path. Unreadable files are skipped.</summary>
    public static Dictionary<string, string> ReadGmlSources(IMod mod)
    {
        var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        List<string> files;
        try { files = mod.GetAllFiles(".gml"); }
        catch { return sources; }

        foreach (var path in files)
        {
            var relative = RelativePath(mod, path);
            if (!relative.StartsWith("gml/", StringComparison.OrdinalIgnoreCase)) continue;

            try { sources[relative] = mod.ReadFile(relative); }
            catch { /* an unreadable file simply contributes no bindings */ }
        }

        return sources;
    }

    public static IReadOnlyList<ModHotkeyConflict> Find(IEnumerable<IMod> mods)
    {
        var usages = new List<ModHotkeyUsage>();

        foreach (var mod in mods)
        {
            foreach (var (relative, source) in ReadGmlSources(mod))
            {
                foreach (var key in KeysIn(source))
                {
                    var rebindable = source.Contains("mmapi_hotkey_register", StringComparison.OrdinalIgnoreCase) ||
                                     source.Contains("hotkey", StringComparison.OrdinalIgnoreCase) &&
                                     source.Contains("config", StringComparison.OrdinalIgnoreCase);
                    usages.Add(new ModHotkeyUsage(key, mod.GetId(), relative, rebindable));
                }
            }
        }

        return usages
            .GroupBy(usage => usage.Key, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(usage => usage.ModId).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ModHotkeyConflict(
                group.Key,
                group.GroupBy(usage => usage.ModId, StringComparer.OrdinalIgnoreCase)
                    .Select(owner => owner.First())
                    .OrderBy(usage => usage.ModId, StringComparer.OrdinalIgnoreCase)
                    .ToList()))
            .ToList();
    }

    internal static string RelativePath(IMod mod, string path)
    {
        var normalizedBase = mod.GetBasePath().Replace('\\', '/').TrimEnd('/') + "/";
        var normalizedFull = path.Replace('\\', '/');
        if (normalizedBase.Length > 1 &&
            normalizedFull.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
            return normalizedFull[normalizedBase.Length..];
        return normalizedFull;
    }
}
