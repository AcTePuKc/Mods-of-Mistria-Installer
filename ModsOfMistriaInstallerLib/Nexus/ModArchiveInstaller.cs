using System.Text.RegularExpressions;
using SharpCompress.Archives;

namespace Garethp.ModsOfMistriaInstallerLib.Nexus;

public class ModArchiveException(string message, Exception? inner = null) : Exception(message, inner);

public record InstalledModFolder(string Name, string Path, bool ReplacedExisting);

public enum ArchiveConflictBehaviour
{
    /// <summary>Stop and report which folders already exist, so the caller can ask the user.</summary>
    Fail,

    /// <summary>Replace the existing folder wholesale - the normal choice when updating a mod.</summary>
    Replace
}

/// <summary>
/// Unpacks a downloaded mod archive into the mods folder.
///
/// Extraction is anchored on the <c>manifest.toml</c>/<c>manifest.json</c> inside the archive rather
/// than on the archive's own layout. That is what fixes the most common install mistake described in
/// the README: an archive whose contents sit one folder deeper than they should ("nested folders")
/// lands correctly here, because everything is written relative to the manifest's directory.
///
/// An archive that carries several manifests - a bundle of related mods - installs each of them into
/// its own folder.
/// </summary>
public static class ModArchiveInstaller
{
    private static readonly string[] ManifestNames = ["manifest.toml", "manifest.json"];

    public static bool LooksLikeArchive(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".zip" or ".rar" or ".7z";

    /// <summary>
    /// Extracts every mod in <paramref name="archivePath"/> into <paramref name="modsLocation"/>.
    /// <paramref name="fallbackName"/> names the folder when the mod sits at the archive root and
    /// so has no directory name of its own - the file name from Nexus is a good choice.
    /// </summary>
    public static List<InstalledModFolder> Install(
        string archivePath,
        string modsLocation,
        string fallbackName,
        ArchiveConflictBehaviour conflictBehaviour = ArchiveConflictBehaviour.Fail)
    {
        if (!File.Exists(archivePath)) throw new ModArchiveException("The downloaded file is missing.");
        if (!Directory.Exists(modsLocation)) throw new ModArchiveException("The mods folder could not be found.");

        using var archive = OpenArchive(archivePath);

        var entries = archive.Entries.Where(entry => !entry.IsDirectory && entry.Key is not null).ToList();

        var manifestRoots = entries
            .Where(entry => ManifestNames.Contains(FileName(entry.Key!), StringComparer.OrdinalIgnoreCase))
            .Select(entry => DirectoryOf(entry.Key!))
            .Distinct()
            .OrderBy(root => root.Length)
            .ToList();

        if (manifestRoots.Count == 0)
            throw new ModArchiveException(
                "That download does not contain a manifest.toml, so it is not a mod this installer can " +
                "handle. It may be an older mod, or a file that has to be installed by hand.");

        // A manifest nested under another mod's folder belongs to that mod (an example or a
        // bundled dependency copy); only the outermost roots are separate installs.
        var roots = manifestRoots
            .Where(root => !manifestRoots.Any(other => other.Length < root.Length && root.StartsWith(other, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var plans = roots
            .Select(root => (Root: root, Target: Path.Combine(modsLocation, TargetFolderName(root, fallbackName))))
            .ToList();

        if (conflictBehaviour == ArchiveConflictBehaviour.Fail)
        {
            var existing = plans.Where(plan => Directory.Exists(plan.Target)).ToList();
            if (existing.Count > 0)
                throw new ModArchiveConflictException(existing.Select(plan => Path.GetFileName(plan.Target)).ToList());
        }

        var installed = new List<InstalledModFolder>();

        foreach (var (root, target) in plans)
        {
            try
            {
                installed.Add(ExtractRoot(entries, root, target));
            }
            catch
            {
                // A bundle installs as a unit. Leaving one of three mods behind after a failure
                // would give the user a mod list that does not match anything they downloaded.
                foreach (var earlier in installed) TryRemove(earlier.Path);
                throw;
            }
        }

        return installed;
    }

    // ── Extraction ───────────────────────────────────────────────────────────────

    private static InstalledModFolder ExtractRoot(List<IArchiveEntry> entries, string root, string target)
    {
        var replaced = Directory.Exists(target);

        // The old folder is kept aside rather than deleted outright: if extraction dies halfway
        // through, the user still has the mod they had before instead of a half-written one.
        var backup = replaced ? target + ".aim-old" : null;
        if (backup is not null)
        {
            if (Directory.Exists(backup)) Directory.Delete(backup, true);
            Directory.Move(target, backup);
        }

        try
        {
            Directory.CreateDirectory(target);

            foreach (var entry in entries)
            {
                var key = Normalise(entry.Key!);
                if (root.Length > 0 && !key.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;

                var relative = key[root.Length..].TrimStart('/');
                if (relative.Length == 0) continue;

                var destination = SafeCombine(target, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

                using var input = entry.OpenEntryStream();
                using var output = File.Create(destination);
                input.CopyTo(output);
            }

            if (backup is not null) Directory.Delete(backup, true);
            return new InstalledModFolder(Path.GetFileName(target), target, replaced);
        }
        catch (Exception e)
        {
            TryRestore(target, backup);
            throw new ModArchiveException($"Could not unpack the mod: {e.Message}", e);
        }
    }

    private static void TryRemove(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        catch (Exception e)
        {
            Logger.Log($"Could not roll back {Path.GetFileName(path)}: {e.Message}");
        }
    }

    private static void TryRestore(string target, string? backup)
    {
        try
        {
            if (Directory.Exists(target)) Directory.Delete(target, true);
            if (backup is not null && Directory.Exists(backup)) Directory.Move(backup, target);
        }
        catch (Exception e)
        {
            Logger.Log($"Could not restore the previous version of {Path.GetFileName(target)}: {e.Message}");
        }
    }

    private static IArchive OpenArchive(string archivePath)
    {
        try
        {
            return ArchiveFactory.OpenArchive(archivePath);
        }
        catch (Exception e)
        {
            throw new ModArchiveException(
                "The downloaded file is not an archive this installer can read (zip, rar and 7z are supported).", e);
        }
    }

    // ── Paths ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Joins an archive-relative path onto the target folder, refusing anything that would
    /// escape it. Archives can contain "../" entries, and a mod download is not a thing to
    /// trust with arbitrary writes.
    /// </summary>
    private static string SafeCombine(string target, string relative)
    {
        var combined = Path.GetFullPath(Path.Combine(target, relative.Replace('/', Path.DirectorySeparatorChar)));
        var root = Path.GetFullPath(target) + Path.DirectorySeparatorChar;

        if (!combined.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new ModArchiveException($"The archive contains an unsafe path: {relative}");

        return combined;
    }

    private static string TargetFolderName(string root, string fallbackName)
    {
        var name = root.TrimEnd('/').Split('/').LastOrDefault();
        if (string.IsNullOrWhiteSpace(name)) name = StripNexusSuffix(Path.GetFileNameWithoutExtension(fallbackName));
        if (string.IsNullOrWhiteSpace(name)) name = "Mod";

        return Sanitise(name);
    }

    /// <summary>
    /// Nexus builds download file names as "Mod Name-78-2-1-1751991240.zip": the mod id, the
    /// version and a timestamp. Those numbers change with every release, so a folder named after
    /// the raw file name would make each update install alongside the previous version instead of
    /// replacing it. Only the trailing numeric run is removed, so a mod whose own name ends in a
    /// number keeps it.
    /// </summary>
    private static string StripNexusSuffix(string fileName)
    {
        var stripped = Regex.Replace(fileName, @"(?:-\d+){2,}$", "");
        return string.IsNullOrWhiteSpace(stripped) ? fileName : stripped;
    }

    /// <summary>
    /// Removes characters Windows will not accept in a folder name.
    /// </summary>
    private static string Sanitise(string name)
    {
        var cleaned = new string(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray())
            .Trim()
            .TrimEnd('.');

        return cleaned.Length == 0 ? "Mod" : cleaned;
    }

    private static string Normalise(string key) => key.Replace('\\', '/').TrimStart('/');

    private static string DirectoryOf(string key)
    {
        var normalised = Normalise(key);
        var index = normalised.LastIndexOf('/');
        return index < 0 ? "" : normalised[..(index + 1)];
    }

    private static string FileName(string key) => Normalise(key).Split('/').Last();
}

/// <summary>Thrown when installing would overwrite folders that are already in the mods folder.</summary>
public class ModArchiveConflictException(List<string> folders)
    : ModArchiveException($"Already installed: {string.Join(", ", folders)}")
{
    public List<string> Folders { get; } = folders;
}
