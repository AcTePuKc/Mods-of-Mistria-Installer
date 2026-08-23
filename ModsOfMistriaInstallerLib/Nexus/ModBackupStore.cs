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
        if (!Directory.Exists(modFolderPath)) return null;

        var modName = Path.GetFileName(modFolderPath.TrimEnd('/', '\\'));
        var createdAt = DateTimeOffset.UtcNow;
        var destination = Path.Combine(FolderFor(modName), StampFor(createdAt, version));

        try
        {
            Directory.CreateDirectory(FolderFor(modName));
            if (Directory.Exists(destination)) Directory.Delete(destination, true);

            Directory.Move(modFolderPath, destination);
        }
        catch (Exception e)
        {
            Logger.Log($"Could not back up {modName} before updating it: {e.Message}");
            return null;
        }

        Prune(modName, keep);
        return new ModBackup(destination, modName, version, createdAt);
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
            if (Directory.Exists(modFolderPath)) Archive(modFolderPath, "replaced");
            Directory.Move(staging, modFolderPath);
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
