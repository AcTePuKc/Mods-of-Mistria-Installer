using Garethp.ModsOfMistriaInstallerLib.ModTypes;
using Garethp.ModsOfMistriaInstallerLib.Lang;
using Garethp.ModsOfMistriaInstallerLib.Nexus;
using SharpCompress.Archives;

namespace Garethp.ModsOfMistriaInstallerLib;

/// <summary>One mod moved out of a watched folder and into the mods folder.</summary>
public record ImportedMod(string Name, string From, string To);

/// <summary>
/// A mod left in the watched folder because the user already has it.
///
/// Not <c>SkippedMod</c>: that name is already an install-result type in this namespace, and it
/// means something else - a mod the installer refused to build, rather than a download it declined
/// to move.
/// </summary>
/// <param name="Name">The mod's own name, as its manifest gives it.</param>
/// <param name="From">Where the download still is.</param>
/// <param name="InstalledAs">The copy already in the mods folder.</param>
public record SkippedImport(string Name, string From, string InstalledAs);

/// <summary>What one sweep of the watched folders did.</summary>
public record DropFolderImport(List<ImportedMod> Imported, List<SkippedImport> AlreadyInstalled)
{
    public static DropFolderImport Nothing => new([], []);
}

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
    private static string Text(string key) =>
        Resources.ResourceManager.GetString(key, Resources.Culture) ?? key;

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
    public static DropFolderImport Import(IEnumerable<string> dropFolders, string modsLocation)
    {
        var imported = new List<ImportedMod>();
        var skipped = new List<SkippedImport>();
        if (!Directory.Exists(modsLocation)) return DropFolderImport.Nothing;

        var modsFull = Path.GetFullPath(modsLocation);
        RemoveAbandonedStaging(modsFull);

        // Who is already here, so a mod the user downloaded twice is recognised as the mod they
        // have rather than filed beside it as "Some Mod (2)". Read at most once per sweep, and not
        // at all when there is nothing to bring in - a watcher fires every time the browser touches
        // the downloads folder, and this opens every archive in the mods folder to read its
        // manifest. It is then kept up to date as mods come in, so two copies of one mod sitting in
        // the same watched folder do not both land.
        List<InstalledModIdentity>? installed = null;

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
                    installed ??= ModIdentity.Installed(modsFull);

                    // Two copies of one mod in the mods folder is not a thing the user can have
                    // meant. They fight over every file they both contain, the conflict report
                    // blames the mod for arguing with itself, and disabling one of them does not
                    // obviously fix it. The download is left where it is rather than deleted, and
                    // the caller is told, so the user can update or remove the copy they have.
                    if (AlreadyInstalled(candidate, installed) is { } existing)
                    {
                        Logger.Log(
                            $"Not bringing in {Path.GetFileName(candidate)}: {existing.Name} is already " +
                            $"installed as {Path.GetFileName(existing.SourcePath)}.");
                        skipped.Add(new SkippedImport(existing.Name, candidate, existing.SourcePath));
                        continue;
                    }

                    if (Move(candidate, modsFull) is not { } moved) continue;

                    imported.Add(moved);

                    // So a second copy later in the same sweep is caught as well.
                    if (IdentityOf(moved.To) is { } arrived) installed.Add(arrived);
                }
                catch (Exception exception)
                {
                    Logger.Log($"Could not bring in {Path.GetFileName(candidate)}: {exception.Message}");
                }
            }
        }

        return new DropFolderImport(imported, skipped);
    }

    /// <summary>The copy of this mod the user already has, or null when it is new to them.</summary>
    private static InstalledModIdentity? AlreadyInstalled(
        string candidate, List<InstalledModIdentity> installed)
    {
        // A mod whose manifest cannot be read is let through: not being able to tell is not a
        // reason to refuse a download the user went and fetched.
        if (IdentityOf(candidate) is not { } identity) return null;

        return installed.FirstOrDefault(
            mod => mod.Id.Equals(identity.Id, StringComparison.OrdinalIgnoreCase));
    }

    private static InstalledModIdentity? IdentityOf(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var inArchive = ModIdentity.InArchive(path);
                return inArchive is null
                    ? null
                    : new InstalledModIdentity(ModIdentity.For(inArchive), inArchive.Name, path);
            }

            if (FolderMod.GetModLocation(path) is not { } location) return null;

            var name = ManifestNames.FirstOrDefault(
                manifest => File.Exists(Path.Combine(location, manifest)));
            if (name is null) return null;

            var read = ModIdentity.ReadManifest(name, File.ReadAllText(Path.Combine(location, name)));
            return read is null ? null : new InstalledModIdentity(ModIdentity.For(read), read.Name, path);
        }
        catch (Exception exception)
        {
            Logger.Log($"Could not tell which mod {Path.GetFileName(path)} is: {exception.Message}");
            return null;
        }
    }

    /// <summary>
    /// Test-only entry point for the cross-volume copy path.
    ///
    /// A test cannot portably put its source folder on a second volume - a temp folder and the
    /// mods folder are on the same drive on every machine that runs this - so the copy step is
    /// handed in instead, which also makes a failing copy something a test can arrange.
    /// </summary>
    internal static void MoveFolderForTesting(
        string source, string destination, string modsLocation, Action<string, string> copy) =>
        MoveFolder(source, destination, modsLocation, copy);

    /// <summary>The name a cross-volume import assembles itself under, for tests to look for.</summary>
    internal static string StagingPrefixForTesting => StagingPrefix;

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

        if (Directory.Exists(source)) MoveFolder(source, destination, modsLocation);
        else File.Move(source, destination);

        Logger.Log($"Brought {Path.GetFileName(destination)} in from {Path.GetDirectoryName(source)}");
        return new ImportedMod(Path.GetFileName(destination), source, destination);
    }

    /// <summary>
    /// Moves a mod folder into the mods folder, copying when the two are on different volumes.
    ///
    /// The copy is the interesting case. A downloads folder on another drive is completely
    /// ordinary, and <see cref="Directory.Move(string,string)"/> cannot cross volumes - but copying
    /// straight onto the destination means the mod exists, under its real name, in the folder AIM
    /// scans, while it is still half written. A watcher sweep or a list reload landing in that
    /// window finds a mod with some of its files missing, and the user gets a broken install with
    /// no error attached to it.
    ///
    /// So the copy goes to a dot-prefixed staging name that the mod scan skips, and is renamed into
    /// place only once every file is there. The rename is within the mods folder, so it is a real
    /// rename rather than a second copy, and there is no moment where a partly written mod is
    /// visible under a name AIM would install.
    /// </summary>
    private static void MoveFolder(
        string source,
        string destination,
        string modsLocation,
        Action<string, string>? copyForTesting = null)
    {
        if (copyForTesting is null)
        {
            try
            {
                Directory.Move(source, destination);
                return;
            }
            catch (IOException)
            {
                // Different volumes, or a name the move could not take. Either way, copy instead.
            }
        }

        var copy = copyForTesting ?? CopyDirectory;
        var staging = StagingNameIn(modsLocation, Path.GetFileName(destination));

        try
        {
            copy(source, staging);
            Directory.Move(staging, destination);
        }
        catch
        {
            // A half-copied mod is not a mod, and leaving one behind would show the user an
            // install that is missing files. The source has not been touched yet, so clearing this
            // away leaves them exactly where they started.
            TryRemoveFolder(staging);
            throw;
        }

        // Only now is the copy complete and published, so losing the original is safe. A source
        // that will not delete is not a failed import - the mod is installed and whole - so it is
        // reported rather than rolled back.
        try
        {
            Directory.Delete(source, true);
        }
        catch (Exception exception)
        {
            Logger.Log(
                $"Imported {Path.GetFileName(destination)}, but the original at {source} could not be " +
                $"removed and may be brought in again: {exception.Message}");
        }
    }

    /// <summary>
    /// Names the folder a cross-volume copy is assembled in. Dot-prefixed so the mod scan skips it,
    /// for the same reason <see cref="ModBackupStore.DirectoryName"/> is.
    /// </summary>
    private const string StagingPrefix = ".aim-importing-";

    private static string StagingNameIn(string modsLocation, string name)
    {
        for (var attempt = 0; attempt < 1000; attempt++)
        {
            var candidate = Path.Combine(modsLocation,
                attempt == 0 ? $"{StagingPrefix}{name}" : $"{StagingPrefix}{name} ({attempt})");

            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }

        throw new IOException(string.Format(Text("GUIModDropStagingFull"), name));
    }

    /// <summary>
    /// Clears away staging folders a previous run left behind - AIM closed mid-copy, or the machine
    /// went down. They are invisible to the mod scan, so nothing breaks while they sit there, but
    /// they are dead weight in a folder the user does look at. Only ones that have stopped changing
    /// are removed, so a copy running in another AIM window is left alone.
    /// </summary>
    private static void RemoveAbandonedStaging(string modsLocation)
    {
        try
        {
            foreach (var folder in Directory.GetDirectories(modsLocation, $"{StagingPrefix}*"))
            {
                // Per folder, so one that another window removed between the listing and the walk
                // does not abandon the tidy-up of the rest. Skipping on error is the right recovery:
                // treating a failed read as "very old" would delete a copy that is still running.
                try
                {
                    // The newest write anywhere inside, not the folder's own timestamp. A folder's
                    // own timestamp only moves when its direct children change, so a copy spending
                    // ten minutes filling an images/ subfolder would look abandoned and be deleted
                    // out from under the window doing it.
                    if (DateTime.UtcNow - NewestWriteIn(folder) < TimeSpan.FromMinutes(5)) continue;
                    TryRemoveFolder(folder);
                }
                catch (Exception exception)
                {
                    Logger.Log($"Could not tidy up the interrupted import at {folder}: {exception.Message}");
                }
            }
        }
        catch (Exception exception)
        {
            Logger.Log($"Could not tidy up interrupted imports: {exception.Message}");
        }
    }

    private static void TryRemoveFolder(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        catch (Exception exception)
        {
            Logger.Log($"Could not clear away the incomplete copy at {path}: {exception.Message}");
        }
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

        throw new IOException(string.Format(Text("GUIModDropTooManyCopies"), name));
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
