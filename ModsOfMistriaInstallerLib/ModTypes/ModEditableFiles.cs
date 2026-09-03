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

    public static string? FindConfig(IMod? mod)
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
