using Garethp.ModsOfMistriaInstallerLib.Nexus;
using SharpCompress.Archives;

namespace Garethp.ModsOfMistriaInstallerLib;

/// <summary>One mod moved out of a watched folder and into the mods folder.</summary>
public record ImportedMod(string Name, string From, string To);

/// <summary>
/// Moves mods the user downloaded by hand into the mods folder.
///
/// Not every mod arrives through "Mod Manager Download": a free Nexus account cannot use it at all,
/// mods live on itch and in Discord servers, and plenty of people simply prefer the download button.
/// Those all land in the browser's downloads folder, and the manual step that follows - find it,
/// work out whether it needs unpacking, move it, alt-tab back, reload the list - is where mods get
/// half-installed. Pointing AIM at that folder removes the step.
///
/// What counts as a mod is decided by looking inside: an archive must contain a manifest, and a
/// folder must be one or directly contain one. A downloads folder is full of things that are not
/// mods, and moving any of them would be worse than doing nothing.
/// </summary>
public static class ModDropFolders
{
    private static readonly string[] ManifestNames = ["manifest.toml", "manifest.json"];

    /// <summary>
    /// Extensions browsers give a download that has not finished. Moving one of these would take a
    /// half-written file and leave the browser writing into a path that no longer exists.
    /// </summary>
    private static readonly string[] PartialExtensions =
        [".crdownload", ".part", ".partial", ".download", ".tmp", ".opdownload", ".!ut"];

    /// <summary>How recently written a file may be and still be considered finished.</summary>
    private static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Moves every mod in <paramref name="dropFolders"/> into <paramref name="modsLocation"/>.
    ///
    /// Only the top level of each folder is considered. Recursing would let one stray mod inside an
    /// unrelated project folder pull that whole tree apart, and a downloads folder is exactly where
    /// such things accumulate.
    /// </summary>
    public static List<ImportedMod> Import(IEnumerable<string> dropFolders, string modsLocation)
    {
        var imported = new List<ImportedMod>();
        if (!Directory.Exists(modsLocation)) return imported;

        var modsFull = Path.GetFullPath(modsLocation);

        foreach (var folder in dropFolders.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) continue;

            // Watching the mods folder itself, or anything containing it, would have AIM shuffling
            // mods around inside their own home.
            var dropFull = Path.GetFullPath(folder);
            if (modsFull.StartsWith(dropFull, StringComparison.OrdinalIgnoreCase) ||
                dropFull.StartsWith(modsFull, StringComparison.OrdinalIgnoreCase))
            {
                Logger.Log($"Not watching {folder}: it overlaps the mods folder.");
                continue;
            }

            foreach (var candidate in Candidates(dropFull))
            {
                try
                {
                    if (Move(candidate, modsFull) is { } moved) imported.Add(moved);
                }
                catch (Exception exception)
                {
                    Logger.Log($"Could not bring in {Path.GetFileName(candidate)}: {exception.Message}");
                }
            }
        }

        return imported;
    }

    /// <summary>Whether a path is a mod AIM would be willing to move.</summary>
    public static bool IsMod(string path)
    {
        try
        {
            if (Directory.Exists(path)) return FolderHoldsMod(path);
            return File.Exists(path) && ModArchiveInstaller.LooksLikeArchive(path) && ArchiveHoldsMod(path);
        }
        catch (Exception exception)
        {
            Logger.Log($"Could not tell whether {Path.GetFileName(path)} is a mod: {exception.Message}");
            return false;
        }
    }

    // ── Finding ──────────────────────────────────────────────────────────────────

    private static List<string> Candidates(string dropFolder)
    {
        var found = new List<string>();

        try
        {
            foreach (var file in Directory.GetFiles(dropFolder))
            {
                if (!ModArchiveInstaller.LooksLikeArchive(file)) continue;
                if (PartialExtensions.Any(extension =>
                        file.EndsWith(extension, StringComparison.OrdinalIgnoreCase))) continue;
                if (!HasSettled(file) || !CanBeMoved(file)) continue;

                // Opening an archive to look for a manifest is the expensive part, and a downloads
                // folder full of things that are not mods would pay it on every sweep. The answer
                // is remembered against the file's size and timestamp, so a replaced file is
                // looked at again but an unchanged one is not.
                if (!ArchiveHoldsModCached(file)) continue;

                found.Add(file);
            }

            foreach (var directory in Directory.GetDirectories(dropFolder))
            {
                // A dot folder in a downloads directory is bookkeeping, not a mod.
                if (Path.GetFileName(directory).StartsWith('.')) continue;

                // Cheap test first. HasSettled walks the whole tree, and a downloads folder is
                // exactly where a huge unrelated directory is waiting to be walked.
                if (!FolderHoldsMod(directory) || !HasSettled(directory)) continue;

                found.Add(directory);
            }
        }
        catch (Exception exception)
        {
            Logger.Log($"Could not read the watched folder {dropFolder}: {exception.Message}");
        }

        return found;
    }

    /// <summary>
    /// A folder is a mod when it holds a manifest, or when everything under it is one mod's worth
    /// of folders that do - the "extracted the zip and got one folder inside" shape.
    /// </summary>
    private static bool FolderHoldsMod(string folder)
    {
        if (ManifestNames.Any(name => File.Exists(Path.Combine(folder, name)))) return true;

        // One level down only. Deeper and this stops being a mod download and starts being a
        // directory that happens to contain mods, which is not something to move wholesale.
        return Directory.GetDirectories(folder)
            .Any(child => ManifestNames.Any(name => File.Exists(Path.Combine(child, name))));
    }

    /// <summary>
    /// Archives already looked inside and found not to be mods, keyed by path, size and timestamp
    /// so that replacing a file makes AIM look again.
    /// </summary>
    private static readonly HashSet<string> NotMods = new(StringComparer.OrdinalIgnoreCase);

    private static bool ArchiveHoldsModCached(string archivePath)
    {
        string stamp;
        try
        {
            var info = new FileInfo(archivePath);
            stamp = $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        }
        catch
        {
            return false;
        }

        lock (NotMods)
            if (NotMods.Contains(stamp)) return false;

        if (ArchiveHoldsMod(archivePath)) return true;

        lock (NotMods)
        {
            // A downloads folder grows without limit; this set should not. Nothing here is worth
            // keeping across a long session, so it is emptied rather than pruned carefully.
            if (NotMods.Count > 512) NotMods.Clear();
            NotMods.Add(stamp);
        }

        return false;
    }

    private static bool ArchiveHoldsMod(string archivePath)
    {
        try
        {
            using var archive = ArchiveFactory.OpenArchive(archivePath);

            return archive.Entries.Any(entry =>
                !entry.IsDirectory &&
                entry.Key is not null &&
                ManifestNames.Contains(
                    entry.Key.Replace('\\', '/').Split('/').Last(), StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception exception)
        {
            // An archive that cannot be opened is either not one or is still being written. Either
            // way it is not something to move yet.
            Logger.Log($"Skipping {Path.GetFileName(archivePath)}: {exception.Message}");
            return false;
        }
    }

    // ── Moving ───────────────────────────────────────────────────────────────────

    private static ImportedMod? Move(string source, string modsLocation)
    {
        var destination = FreeNameIn(modsLocation, Path.GetFileName(source.TrimEnd('/', '\\')));

        if (Directory.Exists(source))
        {
            // Directory.Move cannot cross volumes, and a downloads folder on another drive is
            // completely ordinary, so a copy is the fallback rather than an error.
            try
            {
                Directory.Move(source, destination);
            }
            catch (IOException)
            {
                CopyDirectory(source, destination);
                Directory.Delete(source, true);
            }
        }
        else
        {
            File.Move(source, destination);
        }

        Logger.Log($"Brought {Path.GetFileName(destination)} in from {Path.GetDirectoryName(source)}");
        return new ImportedMod(Path.GetFileName(destination), source, destination);
    }

    /// <summary>
    /// A name in the mods folder that is not taken. Nothing is overwritten: the user asked for the
    /// download to be filed, not for whatever is already installed under that name to be replaced -
    /// updating is what the Nexus update path is for.
    /// </summary>
    private static string FreeNameIn(string modsLocation, string name)
    {
        var candidate = Path.Combine(modsLocation, name);
        if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;

        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);

        for (var attempt = 2; attempt < 1000; attempt++)
        {
            candidate = Path.Combine(modsLocation, $"{stem} ({attempt}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }

        throw new IOException($"There are already too many copies of {name} in the mods folder.");
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);

        foreach (var child in Directory.GetDirectories(source))
            CopyDirectory(child, Path.Combine(destination, Path.GetFileName(child)));
    }

    // ── Is it finished? ──────────────────────────────────────────────────────────

    private static bool HasSettled(string path)
    {
        try
        {
            var written = Directory.Exists(path)
                ? NewestWriteIn(path)
                : File.GetLastWriteTimeUtc(path);

            return DateTime.UtcNow - written > SettleTime;
        }
        catch
        {
            return false;
        }
    }

    private static DateTime NewestWriteIn(string folder)
    {
        var newest = Directory.GetLastWriteTimeUtc(folder);

        foreach (var entry in Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories))
        {
            var written = File.GetLastWriteTimeUtc(entry);
            if (written > newest) newest = written;
        }

        return newest;
    }

    /// <summary>
    /// Whether the file can be taken exclusively. A download still in progress is held open by the
    /// browser, and the timestamp check alone does not catch a stalled one.
    /// </summary>
    private static bool CanBeMoved(string file)
    {
        try
        {
            using var stream = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.None);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
