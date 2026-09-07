using System.Text.RegularExpressions;
using Garethp.ModsOfMistriaInstallerLib.GmlMods;
using Garethp.ModsOfMistriaInstallerLib.ModTypes;

namespace Garethp.ModsOfMistriaInstallerLib.Bindings;

/// <summary>
/// Works out the name a mod calls itself when talking to MMAPI.
///
/// This matters because it is a third name. AIM identifies a mod by its manifest as
/// <c>author.name</c>; on disk it is whatever folder the user unpacked it into; and MMAPI knows it
/// by the string it passed to <c>mmapi_mod_declare</c> - <c>"quest_track"</c>, <c>"quick_stack"</c>.
/// The settings folder is named after that third one, so without it AIM cannot connect a mod in the
/// list to the file holding its keybinds, and would list the same mod twice: once by folder name
/// carrying the real binding, once by its proper name carrying the compiled-in default.
///
/// The declaration is a literal in the mod's own source, so reading it is exact rather than a
/// guess. Where a mod does not declare one, the folder and manifest names are offered as fallbacks.
/// </summary>
public static class MmapiModIdentity
{
    // mmapi_mod_declare("quest_track", QT_VERSION) and mmapi_config_write("quest_track", ...) are
    // the two places a mod spells its MMAPI id as a literal.
    private static readonly Regex Declaration = new(
        "mmapi_(?:mod_declare|config_write|config_load|config_dir|config_path)\\s*\\(\\s*\"([A-Za-z0-9_.-]{2,64})\"",
        RegexOptions.Compiled);

    /// <summary>
    /// Every name this mod might own a settings folder under, most reliable first.
    /// </summary>
    public static List<string> NamesFor(IMod mod)
    {
        var names = new List<string>();

        try
        {
            foreach (var source in HotkeyConflictDetector.ReadGmlSources(mod).Values)
            foreach (Match match in Declaration.Matches(source))
                Add(match.Groups[1].Value);
        }
        catch (Exception exception)
        {
            Logger.Log($"Could not read the MMAPI id of {mod.GetId()}: {exception.Message}");
        }

        // Fallbacks, for mods that declare nothing AIM can read - including archive-backed ones.
        Add(Path.GetFileName(mod.GetSourcePath().TrimEnd('/', '\\')));
        Add(mod.GetId());

        // A manifest id is "author.name"; the half after the dot is often what the mod declares.
        var dot = mod.GetId().IndexOf('.');
        if (dot >= 0 && dot < mod.GetId().Length - 1) Add(mod.GetId()[(dot + 1)..]);

        Add(mod.GetName());
        Add(mod.GetName().Replace(' ', '_'));

        return names;

        void Add(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            if (!names.Contains(name, StringComparer.OrdinalIgnoreCase)) names.Add(name);
        }
    }
}
