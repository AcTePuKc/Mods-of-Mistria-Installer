using System.Text.RegularExpressions;
using Garethp.ModsOfMistriaInstallerLib.ModTypes;
using Garethp.ModsOfMistriaInstallerLib.Nexus;

namespace Garethp.ModsOfMistriaInstallerLib.GmlMods;

/// <summary>Why a mod's shortcut cannot be changed from inside AIM, when it cannot.</summary>
public enum RebindBlocker
{
    None,

    /// <summary>The mod is still a .zip or .rar, so there is no source file to rewrite.</summary>
    NotAFolder,

    /// <summary>
    /// The key is not written as a <c>#macro</c> string. It may be a raw <c>vk_f1</c> constant, or
    /// built at runtime, and rewriting either is guesswork.
    /// </summary>
    NotADeclaredBinding,

    /// <summary>Every F-key is already spoken for by the selected mods.</summary>
    NoFreeKeys,

    /// <summary>The files are there but could not be read or written.</summary>
    NotWritable
}

public sealed record RebindCapability(RebindBlocker Blocker, IReadOnlyList<string> Bindings)
{
    public bool CanRebind => Blocker == RebindBlocker.None;
}

/// <summary>
/// Changes which key a GML mod binds to, by editing the mod's own source.
///
/// This is deliberately the narrowest useful thing. It rewrites only bindings written as
/// <c>#macro SOMETHING "F1"</c> - a declaration whose whole purpose is to name a key, where
/// substituting the string cannot mean anything else. Raw <c>vk_f1</c> constants are left alone:
/// the same token can appear in a comparison, a lookup table or a comment, and a mod that stops
/// compiling because AIM rewrote its code is far worse than a shortcut clash the user could have
/// solved in the game's own settings.
///
/// Everything it touches is backed up first, and the change only reaches the game on the next
/// install, because GML is compiled into the rebuilt archive rather than read at runtime.
/// </summary>
public static class HotkeyRebinder
{
    /// <summary>The keys the detector understands, and therefore the ones it can verify are free.</summary>
    public static readonly IReadOnlyList<string> KnownKeys =
        Enumerable.Range(1, 12).Select(number => $"F{number}").ToList();

    private static Regex MacroFor(string key) => new(
        $"(#macro\\s+\\w+\\s+\")({Regex.Escape(key)})(\")",
        RegexOptions.IgnoreCase);

    /// <summary>
    /// Keys no selected mod is currently using, so offering one cannot trade one clash for another.
    ///
    /// "Using" is decided by <see cref="HotkeyConflictDetector.KeysIn"/>, deliberately - the two
    /// have to agree exactly, or this offers a key the report will immediately flag.
    /// </summary>
    public static IReadOnlyList<string> FreeKeys(IReadOnlyList<IMod> selected)
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var mod in selected)
        {
            try
            {
                foreach (var source in HotkeyConflictDetector.ReadGmlSources(mod).Values)
                    taken.UnionWith(HotkeyConflictDetector.KeysIn(source));
            }
            catch (Exception exception)
            {
                // A mod folder can vanish between the list load and this scan. Treating it as
                // contributing no keys is the safe reading: worst case AIM offers one key fewer.
                Logger.Log($"Could not scan {mod.GetId()} for used shortcuts: {exception.Message}");
            }
        }

        return KnownKeys.Where(key => !taken.Contains(key)).ToList();
    }

    /// <summary>Whether AIM can move <paramref name="mod"/> off <paramref name="key"/>, and if not why.</summary>
    public static RebindCapability Inspect(IMod mod, string key)
    {
        if (!Directory.Exists(mod.GetSourcePath()))
            return new RebindCapability(RebindBlocker.NotAFolder, []);

        Dictionary<string, string> sources;
        try { sources = HotkeyConflictDetector.ReadGmlSources(mod); }
        catch (Exception exception)
        {
            Logger.Log($"Could not read {mod.GetId()} to check its shortcuts: {exception.Message}");
            return new RebindCapability(RebindBlocker.NotWritable, []);
        }

        var pattern = MacroFor(key);
        var bindings = sources
            .Where(entry => pattern.IsMatch(entry.Value))
            .Select(entry => entry.Key)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return bindings.Count == 0
            ? new RebindCapability(RebindBlocker.NotADeclaredBinding, [])
            : new RebindCapability(RebindBlocker.None, bindings);
    }

    /// <summary>
    /// Rewrites every <c>#macro</c> binding of <paramref name="fromKey"/> in the mod to
    /// <paramref name="toKey"/>, after archiving the mod so the edit can be undone from
    /// "Restore the previous version".
    /// </summary>
    /// <returns>The number of files changed, or zero when nothing matched.</returns>
    public static int Rebind(IMod mod, string fromKey, string toKey, ModBackupStore? backups)
    {
        if (fromKey.Equals(toKey, StringComparison.OrdinalIgnoreCase)) return 0;

        var root = mod.GetSourcePath();
        if (!Directory.Exists(root)) return 0;

        var capability = Inspect(mod, fromKey);
        if (!capability.CanRebind) return 0;

        // Archive before the first write, not after: a half-applied edit is exactly the case the
        // backup exists for.
        backups?.Archive(root, mod.GetVersion());

        var pattern = MacroFor(fromKey);
        var changed = 0;

        foreach (var relative in capability.Bindings)
        {
            var absolute = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(absolute)) continue;

            var source = File.ReadAllText(absolute);
            var rewritten = pattern.Replace(source, match => $"{match.Groups[1].Value}{toKey}{match.Groups[3].Value}");
            if (rewritten == source) continue;

            File.WriteAllText(absolute, rewritten);
            changed++;
        }

        if (changed > 0)
            Logger.Log($"Rebound {mod.GetId()} from {fromKey} to {toKey} in {changed} file(s).");

        return changed;
    }

}
