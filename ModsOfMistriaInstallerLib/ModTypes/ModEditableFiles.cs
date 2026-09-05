using Garethp.ModsOfMistriaInstallerLib.Bindings;

namespace Garethp.ModsOfMistriaInstallerLib.ModTypes;

/// <summary>
/// Finds the two files a user most often wants to open by hand: the mod's manifest, and whatever
/// the mod calls its config.
///
/// Only folder-backed mods qualify. A mod that is still a .zip or .rar has no file on disk to hand
/// to a text editor, and editing a copy the archive would overwrite is worse than not offering it
/// at all - so those rows get the menu item greyed out rather than a silently useless one.
/// </summary>
public static class ModEditableFiles
{
    private static readonly string[] ManifestNames = ["manifest.json", "manifest.toml"];

    /// <summary>
    /// Config file names seen across Fields of Mistria mods, in the order they are preferred. There
    /// is no standard, so this is a best-effort list with a wildcard pass behind it.
    /// </summary>
    private static readonly string[] ConfigNames =
    [
        "config.json",
        "config.toml",
        "config.ini",
        "settings.json",
        "settings.toml",
        "mod_config.json",
        "user_config.json"
    ];

    private static readonly string[] ConfigExtensions = [".json", ".toml", ".ini", ".cfg"];

    /// <summary>The folder the mod actually lives in, or null when it is archive-backed.</summary>
    public static string? RootFolder(IMod? mod)
    {
        if (mod is null) return null;

        var location = mod.GetLocation();
        if (!string.IsNullOrEmpty(location) && Directory.Exists(location)) return location;

        var source = mod.GetSourcePath();
        return !string.IsNullOrEmpty(source) && Directory.Exists(source) ? source : null;
    }

    public static string? FindManifest(IMod? mod)
    {
        var root = RootFolder(mod);
        if (root is null) return null;

        return ManifestNames
            .Select(name => Path.Combine(root, name))
            .FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// The mod's settings file, wherever the mod actually keeps it.
    ///
    /// Two places, and the second is the one that matters for most modern mods. A mod that ships a
    /// config file has it in its own folder, which is what this looked for. But an MMAPI mod that
    /// uses <c>mmapi_config_write</c> - which is how a mod with in-game settings is written now -
    /// has no config file in its folder at all: MMAPI keeps it under the game's own config
    /// directory in <c>mod_data/&lt;mod id&gt;/</c>, because a settings file inside the mod folder
    /// would be destroyed by every update. Looking only in the mod folder therefore greyed out
    /// "Edit config" for exactly the mods most likely to have settings worth editing.
    ///
    /// The mod-folder copy still wins when both exist: that one is the mod's own shipped defaults,
    /// and it is what the author's documentation talks about.
    ///
    /// Null does not always mean "no settings". MMAPI writes the file the first time the mod runs,
    /// so a mod installed but not yet played has nothing on disk to edit - which is why the caller
    /// says so rather than only greying the item out.
    /// </summary>
    public static string? FindConfig(IMod? mod) =>
        FindConfigInModFolder(mod) ?? FindConfigInGameData(mod);

    /// <summary>Where MMAPI keeps this mod's settings, or null if it has never written them.</summary>
    public static string? FindConfigInGameData(IMod? mod)
    {
        var modId = mod?.GetId();
        if (string.IsNullOrWhiteSpace(modId)) return null;

        try
        {
            return ModDataStore.Locate()?.FindConfigFile(modId);
        }
        catch (Exception exception)
        {
            Logger.Log($"Could not look for {modId}'s settings in the game's config folder: {exception.Message}");
            return null;
        }
    }

    public static string? FindConfigInModFolder(IMod? mod)
    {
        var root = RootFolder(mod);
        if (root is null) return null;

        var known = ConfigNames
            .Select(name => Path.Combine(root, name))
            .FirstOrDefault(File.Exists);
        if (known is not null) return known;

        try
        {
            // Mods that roll their own name almost always still say "config" in it. Take the
            // shortest match so "config.json" wins over "config.backup.json" when both exist.
            return Directory
                .EnumerateFiles(root)
                .Where(path => ConfigExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                .Where(path => Path.GetFileName(path).Contains("config", StringComparison.OrdinalIgnoreCase)
                               || Path.GetFileName(path).Contains("settings", StringComparison.OrdinalIgnoreCase))
                .Where(path => !ManifestNames.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
                .OrderBy(path => Path.GetFileName(path).Length)
                .ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch (Exception exception)
        {
            // EnumerateFiles is lazy, so a folder that disappears or turns out to be unreadable
            // throws from inside the query rather than at the Directory.Exists check above. This
            // decides whether one menu item is greyed out; it must never take the mod list down
            // with it.
            Logger.Log($"Could not look for a config file in {root}: {exception.Message}");
            return null;
        }
    }
}
