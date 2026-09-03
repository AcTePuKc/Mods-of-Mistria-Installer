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
    /// <param name="backups">
    /// When given, a folder being replaced is moved into the backup store instead of being deleted,
    /// so the user can roll the update back.
    /// </param>
    /// <param name="previousVersion">Labels the backup with the version it holds.</param>
    /// <param name="replacePath">
    /// The mod already on disk that this download is an update to - its folder, or the .zip it was
    /// installed as. When given, and the archive holds a single mod, it is unpacked over that exact
    /// path instead of into a folder named after the download.
    ///
    /// Without this an update never replaces anything. Nexus file names carry the file id, the
    /// version and an upload stamp ("March Expanded 669 2.0.12 2026-08-21T00-48Z UeRMzf4uu.zip"),
    /// and a mod whose archive has no folder of its own is named after that file - so every release
    /// lands in a brand new folder and the old copy stays in the mod list beside it.
    /// </param>
    public static List<InstalledModFolder> Install(
        string archivePath,
        string modsLocation,
        string fallbackName,
        ArchiveConflictBehaviour conflictBehaviour = ArchiveConflictBehaviour.Fail,
        ModBackupStore? backups = null,
        string? previousVersion = null,
        string? replacePath = null)
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

        // Only a single-mod archive can be aimed at a known folder. A bundle has several mods in it
        // and no way to say which of them is the one being replaced, so it falls back to naming.
        var replaceTarget = roots.Count == 1 ? ResolveReplaceTarget(replacePath, modsLocation) : null;

        var plans = roots
            .Select(root => (
                Root: root,
                Target: replaceTarget ?? Path.Combine(modsLocation, TargetFolderName(root, fallbackName))))
            .ToList();

        if (conflictBehaviour == ArchiveConflictBehaviour.Fail)
        {
            // A folder we were told to replace is not a conflict: the caller already knows it is
            // there and has asked for it to be overwritten.
            var existing = plans
                .Where(plan => Directory.Exists(plan.Target) && plan.Target != replaceTarget)
                .ToList();
            if (existing.Count > 0)
                throw new ModArchiveConflictException(existing.Select(plan => Path.GetFileName(plan.Target)).ToList());
        }

        var installed = new List<InstalledModFolder>();

        // A mod that was installed as a .zip is being replaced by an unpacked folder, so the old
        // archive has to go or the mod list shows both. It is archived rather than deleted, and
        // before extraction rather than after, so a failed unpack restores cleanly.
        var supersededArchive = SupersededArchive(replacePath, replaceTarget);
        if (supersededArchive is not null)
            ArchiveSupersededFile(supersededArchive, backups, previousVersion);

        foreach (var (root, target) in plans)
        {
            try
            {
                installed.Add(ExtractRoot(entries, root, target, backups, previousVersion));
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

    // ── Replacing an existing install ────────────────────────────────────────────

    /// <summary>
    /// Where an update should be unpacked, given the copy already on disk.
    ///
    /// A mod installed as <c>Foo.zip</c> is replaced by a <c>Foo</c> folder: the installer reads
    /// both, and unpacking over a file is not a thing. The path is required to sit inside the mods
    /// folder - a stale or hand-edited index must not be able to aim an extraction anywhere else.
    /// </summary>
    private static string? ResolveReplaceTarget(string? replacePath, string modsLocation)
    {
        if (string.IsNullOrWhiteSpace(replacePath)) return null;

        string full, root;
        try
        {
            full = Path.GetFullPath(replacePath.TrimEnd('/', '\\'));
            root = Path.GetFullPath(modsLocation) + Path.DirectorySeparatorChar;
        }
        catch (Exception e)
        {
            Logger.Log($"Ignoring an unusable replace path ({replacePath}): {e.Message}");
            return null;
        }

        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return null;

        // Only directly inside the mods folder. A nested path would be a sub-folder of some other
        // mod, and replacing that is never what an update means.
        if (Path.GetDirectoryName(full) is not { } parent ||
            !string.Equals(parent + Path.DirectorySeparatorChar, root, StringComparison.OrdinalIgnoreCase))
            return null;

        if (Directory.Exists(full)) return full;

        if (File.Exists(full) && LooksLikeArchive(full))
        {
            var unpacked = Path.Combine(Path.GetDirectoryName(full)!, Path.GetFileNameWithoutExtension(full));
            return Sanitise(Path.GetFileName(unpacked)).Length == 0 ? null : unpacked;
        }

        return null;
    }

    /// <summary>The .zip an update is superseding, when the previous install was one.</summary>
    private static string? SupersededArchive(string? replacePath, string? replaceTarget)
    {
        if (replaceTarget is null || string.IsNullOrWhiteSpace(replacePath)) return null;

        var full = Path.GetFullPath(replacePath.TrimEnd('/', '\\'));
        return File.Exists(full) && LooksLikeArchive(full) ? full : null;
    }

    private static void ArchiveSupersededFile(string path, ModBackupStore? backups, string? previousVersion)
    {
        try
        {
            if (backups?.Archive(path, previousVersion) is not null) return;

            // No store, or the store could not take it. Keeping it aside beside the mods folder is
            // still better than deleting a mod the user has, and better than leaving a duplicate in
            // the list - the installer ignores the .aim-old suffix.
            var parked = path + ".aim-old";
            if (File.Exists(parked)) File.Delete(parked);
            File.Move(path, parked);
        }
        catch (Exception e)
        {
            Logger.Log($"Could not put aside the old archive {Path.GetFileName(path)}: {e.Message}");
        }
    }

    // ── Extraction ───────────────────────────────────────────────────────────────

    private static InstalledModFolder ExtractRoot(
        List<IArchiveEntry> entries,
        string root,
        string target,
        ModBackupStore? backups = null,
        string? previousVersion = null)
    {
        var replaced = Directory.Exists(target);

        // The old folder is kept aside rather than deleted outright: if extraction dies halfway
        // through, the user still has the mod they had before instead of a half-written one. When a
        // backup store is supplied it keeps that copy for good, so the update can be undone later.
        string? backup = null;
        var backupIsKept = false;

        if (replaced)
        {
            var kept = backups?.Archive(target, previousVersion);
            if (kept is not null)
            {
                backup = kept.Path;
                backupIsKept = true;
            }
            else
            {
                backup = target + ".aim-old";
                if (Directory.Exists(backup)) Directory.Delete(backup, true);
                Directory.Move(target, backup);
            }
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

            // A kept backup is the whole point of the store; only the throwaway copy is removed.
            if (backup is not null && !backupIsKept) Directory.Delete(backup, true);
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
