using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Tomlyn;
using Tomlyn.Model;

namespace Garethp.ModsOfMistriaInstallerLib.ModTypes;

/// <summary>One mod already in the mods folder, as cheaply as it can be known.</summary>
/// <param name="SourcePath">The folder or archive it is installed as.</param>
public record InstalledModIdentity(string Id, string Name, string SourcePath);

/// <summary>
/// What makes two copies on disk the same mod.
///
/// The author and the name, exactly as <see cref="FolderMod"/>, <see cref="ZipMod"/> and
/// <see cref="RarMod"/> have always computed it - shared here so that the check that refuses a
/// second copy of a mod cannot drift away from the id the mod list shows.
/// </summary>
public static class ModIdentity
{
    private static readonly string[] ManifestNames = ["manifest.toml", "manifest.json"];

    public static string For(string author, string name) =>
        Regex.Replace($"{author.ToLower()}.{name.ToLower()}".Replace(" ", "_"), "[^a-zA-Z0-9_\\.]", "");

    public static string For(ModManifest manifest) => For(manifest.Author, manifest.Name);

    /// <summary>
    /// Reads a manifest's text into a manifest. <paramref name="fileName"/> decides which format it
    /// is read as, the same way the mod types do.
    /// </summary>
    public static ModManifest? ReadManifest(string fileName, string content)
    {
        try
        {
            return fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? ModManifest.FromJson(JObject.Parse(content))
                : ModManifest.FromToml(TomlSerializer.Deserialize<TomlTable>(content)!);
        }
        catch (Exception e)
        {
            Logger.Log($"Could not read a {fileName}: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Every mod already installed, by identity alone.
    ///
    /// Deliberately not <see cref="MistriaLocator.GetMods"/>: that reads and validates each mod's
    /// whole content, and the only question here is "is this mod already here, and where". This
    /// reads one manifest per install and nothing else.
    /// </summary>
    public static List<InstalledModIdentity> Installed(string modsLocation)
    {
        var found = new List<InstalledModIdentity>();
        if (!Directory.Exists(modsLocation)) return found;

        try
        {
            foreach (var folder in Directory.GetDirectories(modsLocation))
            {
                // AIM's own bookkeeping - the backup store, and the folder a cross-volume import is
                // assembled in - is not a list of installed mods.
                if (Path.GetFileName(folder).StartsWith('.')) continue;

                var location = FolderMod.GetModLocation(folder);
                if (location is null) continue;

                var name = ManifestNames.FirstOrDefault(
                    manifest => File.Exists(Path.Combine(location, manifest)));
                if (name is null) continue;

                var manifest = ReadManifest(name, File.ReadAllText(Path.Combine(location, name)));
                if (manifest is not null)
                    found.Add(new InstalledModIdentity(For(manifest), manifest.Name, folder));
            }

            foreach (var archive in Directory.GetFiles(modsLocation)
                         .Where(file => Nexus.ModArchiveInstaller.LooksLikeArchive(file)))
            {
                var manifest = InArchive(archive);
                if (manifest is not null)
                    found.Add(new InstalledModIdentity(For(manifest), manifest.Name, archive));
            }
        }
        catch (Exception e)
        {
            Logger.Log($"Could not read what is already installed: {e.Message}");
        }

        return found;
    }

    /// <summary>
    /// Manifests already read out of an archive, keyed by path, size and timestamp so a replaced
    /// file is looked at again but an unchanged one is not.
    ///
    /// Opening an archive to read one small file is the expensive part of knowing what is
    /// installed, and a watched folder is swept every time the browser touches it. Without this,
    /// every sweep re-opens every archive in the mods folder.
    /// </summary>
    private static readonly Dictionary<string, ModManifest?> ArchiveManifests = new(StringComparer.OrdinalIgnoreCase);

    private static string? StampFor(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The manifest inside a mod archive, or null when it has none AIM can read.</summary>
    public static ModManifest? InArchive(string archivePath)
    {
        var stamp = StampFor(archivePath);

        if (stamp is not null)
            lock (ArchiveManifests)
                if (ArchiveManifests.TryGetValue(stamp, out var remembered))
                    return remembered;

        var read = ReadArchiveManifest(archivePath);

        if (stamp is not null)
            lock (ArchiveManifests)
            {
                // A mods folder grows without limit; this should not. Nothing here is worth keeping
                // across a long session, so it is emptied rather than pruned carefully.
                if (ArchiveManifests.Count > 512) ArchiveManifests.Clear();
                ArchiveManifests[stamp] = read;
            }

        return read;
    }

    private static ModManifest? ReadArchiveManifest(string archivePath)
    {
        try
        {
            using var archive = SharpCompress.Archives.ArchiveFactory.OpenArchive(archivePath);

            // The outermost manifest: a manifest nested inside another mod's folder belongs to that
            // mod, exactly as the installer treats it.
            var entry = archive.Entries
                .Where(item => !item.IsDirectory && item.Key is not null)
                .Where(item => ManifestNames.Contains(
                    item.Key!.Replace('\\', '/').Split('/').Last(), StringComparer.OrdinalIgnoreCase))
                .MinBy(item => item.Key!.Length);

            if (entry is null) return null;

            using var stream = entry.OpenEntryStream();
            using var reader = new StreamReader(stream);
            return ReadManifest(entry.Key!.Replace('\\', '/').Split('/').Last(), reader.ReadToEnd());
        }
        catch (Exception e)
        {
            Logger.Log($"Could not read the manifest in {Path.GetFileName(archivePath)}: {e.Message}");
            return null;
        }
    }
}
