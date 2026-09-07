using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Garethp.ModsOfMistriaInstallerLib.Bindings;

/// <summary>One JSON file a mod keeps its settings in.</summary>
public sealed record ModConfigFile(string ModId, string Path, JObject Content)
{
    /// <summary>The file's own name, which distinguishes a mod's several config files.</summary>
    public string FileName => System.IO.Path.GetFileName(Path);
}

/// <summary>
/// Reads and writes the settings MMAPI mods keep for themselves.
///
/// This is the important thing to know about mod settings: they do <em>not</em> live in the mod's
/// folder. <c>mmapi_config_write</c> puts them under the game's own config directory, in
/// <c>mod_data/&lt;mod_id&gt;/</c>. So replacing a mod folder during an update never touches the
/// user's chosen keybind - what loses a binding is the mod itself rewriting its config after a
/// version bump, or MMAPI rejecting a value and falling back to the default.
///
/// A mod usually keeps one file named after itself, but several keep more beside it - Quick Stack
/// writes its keys to <c>bindings.json</c>, Mistria Quest Helper to <c>options.json</c> - so every
/// JSON file directly inside the mod's folder is read. Subdirectories are skipped: that is where
/// per-save data lives, which is not settings.
/// </summary>
public sealed class ModDataStore
{
    public const string ModDataFolderName = "mod_data";

    private readonly string _root;

    /// <param name="gameConfigDirectory">
    /// One of <see cref="MistriaLocator.GetGameConfigDirectories"/> - the branch directory that
    /// holds <c>saves</c>, e.g. <c>%LOCALAPPDATA%\FieldsOfMistria\beta</c>.
    /// </param>
    public ModDataStore(string gameConfigDirectory)
    {
        _root = Path.Combine(gameConfigDirectory, ModDataFolderName);
    }

    /// <summary>
    /// The store for the game branch the user actually plays, or null when no config directory
    /// exists yet - which is the normal state before the game has been run once.
    /// </summary>
    public static ModDataStore? Locate()
    {
        foreach (var directory in MistriaLocator.GetGameConfigDirectories())
        {
            var store = new ModDataStore(directory);
            if (store.Exists) return store;
        }

        return null;
    }

    public string Root => _root;

    public bool Exists => Directory.Exists(_root);

    /// <summary>Every settings file of every mod that has written one. Unreadable files are skipped.</summary>
    public List<ModConfigFile> ReadAll()
    {
        var files = new List<ModConfigFile>();
        if (!Exists) return files;

        List<string> modFolders;
        try { modFolders = Directory.EnumerateDirectories(_root).ToList(); }
        catch (Exception exception)
        {
            Logger.Log($"Could not list {_root}: {exception.Message}");
            return files;
        }

        foreach (var folder in modFolders)
        {
            var modId = Path.GetFileName(folder);

            IEnumerable<string> jsonFiles;
            try { jsonFiles = Directory.EnumerateFiles(folder, "*.json"); }
            catch (Exception exception)
            {
                Logger.Log($"Could not list the settings of {modId}: {exception.Message}");
                continue;
            }

            foreach (var path in jsonFiles)
            {
                var content = TryRead(path);
                if (content is not null) files.Add(new ModConfigFile(modId, path, content));
            }
        }

        return files;
    }

    /// <summary>
    /// The settings file to hand a text editor for one mod, or null when it has never written one.
    ///
    /// Preference order is the file named after the mod, then the shortest name - which picks
    /// <c>config.json</c> over <c>config.backup.json</c>, and the mod's main settings over the
    /// <c>bindings.json</c> or <c>options.json</c> some mods keep beside it.
    /// </summary>
    public string? FindConfigFile(string modId)
    {
        if (!Exists || string.IsNullOrWhiteSpace(modId)) return null;

        var folder = Path.Combine(_root, modId);
        if (!Directory.Exists(folder)) return null;

        try
        {
            var files = Directory.EnumerateFiles(folder, "*.json").ToList();
            if (files.Count == 0) return null;

            var named = files.FirstOrDefault(path =>
                string.Equals(Path.GetFileNameWithoutExtension(path), modId, StringComparison.OrdinalIgnoreCase));

            return named ?? files
                .OrderBy(path => Path.GetFileName(path).Length)
                .ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .First();
        }
        catch (Exception exception)
        {
            Logger.Log($"Could not look for {modId}'s settings in {folder}: {exception.Message}");
            return null;
        }
    }

    private static JObject? TryRead(string path)
    {
        try
        {
            return JObject.Parse(File.ReadAllText(path));
        }
        catch (Exception exception)
        {
            // A settings file the game is midway through writing, or one holding an array rather
            // than an object. Neither is AIM's to repair.
            Logger.Log($"Could not read {path}: {exception.Message}");
            return null;
        }
    }

    /// <summary>
    /// Changes one value in a settings file and leaves everything else exactly as it was.
    ///
    /// The whole document is rewritten from the parsed copy rather than patched textually, so the
    /// mod's own keys, its <c>__config_version</c> stamp and any last-good copy MMAPI keeps all
    /// survive. Only the named field moves.
    /// </summary>
    /// <returns>False when the file could not be written; the caller reports why.</returns>
    public static bool WriteField(ModConfigFile file, string field, string value)
    {
        try
        {
            // Re-read rather than trusting the copy in memory: the game may have rewritten the file
            // since it was listed, and clobbering a setting the user changed in-game would be the
            // exact failure this feature exists to prevent.
            var current = TryRead(file.Path) ?? file.Content;
            current[field] = value;

            File.WriteAllText(file.Path, current.ToString(Formatting.Indented));
            return true;
        }
        catch (Exception exception)
        {
            Logger.Log($"Could not write {field} to {file.Path}: {exception.Message}");
            return false;
        }
    }
}
