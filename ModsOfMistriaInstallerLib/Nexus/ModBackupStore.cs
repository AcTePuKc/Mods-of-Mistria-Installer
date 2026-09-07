using System.Globalization;

namespace Garethp.ModsOfMistriaInstallerLib.Nexus;

public record ModBackup(string Path, string ModName, string? Version, DateTimeOffset CreatedAt)
{
    public string Describe() =>
        string.IsNullOrWhiteSpace(Version)
            ? CreatedAt.LocalDateTime.ToString("g", CultureInfo.CurrentCulture)
            : $"{Version} ({CreatedAt.LocalDateTime.ToString("g", CultureInfo.CurrentCulture)})";
}

/// <summary>
/// Keeps the previous copies of a mod so an update can be undone.
///
/// Backups live in <c>.aim-backups</c> inside the mods folder: beside the mods rather than off in a
/// temp directory, so they survive a reboot and travel with a copied mods folder, and prefixed with
/// a dot so the installer's own scan skips them. Only the most recent few are kept - mod folders
/// can be hundreds of megabytes, and nobody rolls back six versions.
/// </summary>
public class ModBackupStore
{
    public const string DirectoryName = ".aim-backups";

    private const int DefaultKeep = 3;

    private readonly string _root;

    public ModBackupStore(string modsLocation)
    {
        _root = Path.Combine(modsLocation, DirectoryName);
    }

    /// <summary>
    /// Moves the current copy of a mod into the backup store. Returns null when there was nothing
    /// to back up, or when the move failed - a backup is a convenience, and failing to make one
    /// must not stop an update the user asked for.
    /// </summary>
    public ModBackup? Archive(string modFolderPath, string? version, int keep = DefaultKeep)
    {
        var isFile = File.Exists(modFolderPath);
        if (!isFile && !Directory.Exists(modFolderPath)) return null;

        // A mod installed as Foo.zip is the same mod as one installed as a Foo folder, so both are
        // filed under the same name - otherwise updating a zipped mod into a folder would lose the
        // rollback path back to the archive it replaced.
        var leaf = Path.GetFileName(modFolderPath.TrimEnd('/', '\\'));
        var modName = isFile ? Path.GetFileNameWithoutExtension(leaf) : leaf;
        var createdAt = DateTimeOffset.UtcNow;
        var destination = Path.Combine(FolderFor(modName), StampFor(createdAt, version));

        try
        {
            Directory.CreateDirectory(FolderFor(modName));
            if (Directory.Exists(destination)) Directory.Delete(destination, true);

            if (isFile)
            {
                // The archive is kept inside a stamped folder like any other backup, so listing and
                // pruning need no special case; only Restore has to notice it is a file.
                Directory.CreateDirectory(destination);
                File.Move(modFolderPath, Path.Combine(destination, leaf));
            }
            else
            {
                Directory.Move(modFolderPath, destination);
            }
        }
        catch (Exception e)
        {
            Logger.Log($"Could not back up {modName} before updating it: {e.Message}");

            // The stamped folder was created before the move that failed. Left behind it shows in
            // the backup list as a restore point holding nothing.
            try { if (Directory.Exists(destination)) Directory.Delete(destination, true); } catch { }

            return null;
        }

        Prune(modName, keep);
        return new ModBackup(destination, modName, version, createdAt);
    }

    /// <summary>
    /// Copies the current state of a mod into the backup store, leaving the mod where it is.
    ///
    /// <see cref="Archive"/> moves, because an update is about to overwrite the folder and the old
    /// copy has nowhere else to be. An edit is different: AIM is about to change a file *inside* a
    /// mod that stays installed, so the restore point has to be a copy. It is filed exactly like
    /// every other backup and shows up in the same version dropdown, which is the point - "undo
    /// what AIM changed" should not be a different gesture from "go back a version".
    /// </summary>
    /// <param name="label">
    /// What the restore point is, shown in the dropdown in place of a version number - for example
    /// "2.1.0 before AIM's fix".
    /// </param>
    /// <returns>The restore point, or null when the copy could not be made.</returns>
    public ModBackup? Snapshot(string modFolderPath, string? label, int keep = DefaultKeep)
    {
        var isFile = File.Exists(modFolderPath);
        if (!isFile && !Directory.Exists(modFolderPath)) return null;

        var leaf = Path.GetFileName(modFolderPath.TrimEnd('/', '\\'));
        var modName = isFile ? Path.GetFileNameWithoutExtension(leaf) : leaf;
        var createdAt = DateTimeOffset.UtcNow;
        var destination = Path.Combine(FolderFor(modName), StampFor(createdAt, label));

        try
        {
            Directory.CreateDirectory(FolderFor(modName));
            if (Directory.Exists(destination)) Directory.Delete(destination, true);
            Directory.CreateDirectory(destination);

            if (isFile) File.Copy(modFolderPath, Path.Combine(destination, leaf));
            else CopyInto(modFolderPath, destination);
        }
        catch (Exception e)
        {
            Logger.Log($"Could not snapshot {modName} before editing it: {e.Message}");

            // A half-copied restore point is worse than none: restoring it would silently install a
            // truncated mod. Remove it rather than leave it in the dropdown.
            try { if (Directory.Exists(destination)) Directory.Delete(destination, true); } catch { }

            return null;
        }

        Prune(modName, keep);
        return new ModBackup(destination, modName, label, createdAt);
    }

    private static void CopyInto(string source, string destination)
    {
        foreach (var folder in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, folder)));

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)), true);
    }

    /// <summary>
    /// The name backups are filed under for a mod. Mods arrive as a folder or as an archive, and
    /// both are the same mod, so the extension is dropped - otherwise a mod installed as Foo.zip
    /// would never find the backups taken when it was a folder.
    /// </summary>
    public static string ModNameFor(string sourcePath)
    {
        var leaf = Path.GetFileName(sourcePath.TrimEnd('/', '\\'));
        return ModArchiveInstaller.LooksLikeArchive(leaf) ? Path.GetFileNameWithoutExtension(leaf) : leaf;
    }

    /// <summary>Backups for one mod, newest first.</summary>
    public List<ModBackup> List(string modName)
    {
        var folder = FolderFor(modName);
        if (!Directory.Exists(folder)) return [];

        return Directory.GetDirectories(folder)
            .Select(path => Read(path, modName))
            .Where(backup => backup is not null)
            .Select(backup => backup!)
            .OrderByDescending(backup => backup.CreatedAt)
            .ToList();
    }

    public bool HasBackups(string modName) => List(modName).Count > 0;

    /// <summary>
    /// Puts a backup back. The copy being replaced is archived first, so restoring is itself
    /// undoable and a mistaken rollback does not cost the user the version they were on.
    /// </summary>
    public void Restore(ModBackup backup, string modFolderPath)
    {
        if (!Directory.Exists(backup.Path))
            throw new IOException($"The backup of {backup.ModName} is missing.");

        // Move the chosen backup out of the store first. Archiving the copy it replaces prunes the
        // oldest backups, and restoring the oldest one would otherwise delete it mid-restore.
        var staging = Path.Combine(_root, ".restoring");
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        Directory.CreateDirectory(_root);
        Directory.Move(backup.Path, staging);

        try
        {
            if (Directory.Exists(modFolderPath) || File.Exists(modFolderPath))
                Archive(modFolderPath, "replaced");

            // A backup holding a single archive is a mod that was installed as a .zip. It goes back
            // beside the other mods under its own file name rather than as a folder, because that
            // is the shape the installer scanned it as.
            var archived = SingleArchiveIn(staging);
            if (archived is not null)
            {
                var beside = Path.GetDirectoryName(Path.GetFullPath(modFolderPath.TrimEnd('/', '\\')))!;
                var destination = Path.Combine(beside, Path.GetFileName(archived));

                if (File.Exists(destination)) File.Delete(destination);
                File.Move(archived, destination);
                Directory.Delete(staging, true);
            }
            else
            {
                Directory.Move(staging, modFolderPath);
            }
        }
        catch
        {
            // Put the backup back where it was rather than leaving it in the staging folder.
            if (Directory.Exists(staging) && !Directory.Exists(backup.Path))
                Directory.Move(staging, backup.Path);
            throw;
        }
    }

    // ── Layout ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// The archive inside a backup that holds one, or null for an ordinary folder backup.
    /// </summary>
    private static string? SingleArchiveIn(string backupFolder)
    {
        if (!Directory.Exists(backupFolder)) return null;
        if (Directory.GetDirectories(backupFolder).Length > 0) return null;

        var files = Directory.GetFiles(backupFolder);
        return files.Length == 1 && ModArchiveInstaller.LooksLikeArchive(files[0]) ? files[0] : null;
    }

    private string FolderFor(string modName) => Path.Combine(_root, Sanitise(modName));

    private static string StampFor(DateTimeOffset createdAt, string? version)
    {
        var stamp = createdAt.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(version) ? stamp : $"{stamp}__{Sanitise(version)}";
    }

    private static ModBackup? Read(string path, string modName)
    {
        var name = Path.GetFileName(path);
        var parts = name.Split("__", 2);

        if (!DateTime.TryParseExact(parts[0], "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var createdAt))
            return null;

        return new ModBackup(path, modName, parts.Length > 1 ? parts[1] : null,
            new DateTimeOffset(createdAt, TimeSpan.Zero));
    }

    private void Prune(string modName, int keep)
    {
        foreach (var stale in List(modName).Skip(Math.Max(keep, 1)))
        {
            try
            {
                Directory.Delete(stale.Path, true);
            }
            catch (Exception e)
            {
                Logger.Log($"Could not remove the old backup {stale.Path}: {e.Message}");
            }
        }
    }

    private static string Sanitise(string name)
    {
        var cleaned = new string(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray())
            .Trim()
            .TrimEnd('.');

        return cleaned.Length == 0 ? "mod" : cleaned;
    }
}
