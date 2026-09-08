using System.Text.RegularExpressions;
using Garethp.ModsOfMistriaInstallerLib.Lang;
using Garethp.ModsOfMistriaInstallerLib.ModTypes;
using SharpCompress.Archives;

namespace Garethp.ModsOfMistriaInstallerLib.Nexus;

public class ModArchiveException(string message, Exception? inner = null) : Exception(message, inner);

public record InstalledModFolder(string Name, string Path, bool ReplacedExisting);

/// <summary>Hard limits for one downloaded mod archive before it is allowed to write to disk.</summary>
public sealed record ModArchiveExtractionLimits(
    int MaxEntries = 20_000,
    long MaxEntryBytes = 512L * 1024 * 1024,
    long MaxTotalBytes = 2L * 1024 * 1024 * 1024)
{
    public static ModArchiveExtractionLimits Default { get; } = new();
}

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
    private static string Text(string key) =>
        Resources.ResourceManager.GetString(key, Resources.Culture) ?? key;

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
        CancellationToken cancellationToken = default,
        ModArchiveExtractionLimits? limits = null,
        string? replacePath = null,
        IReadOnlyList<InstalledModIdentity>? installedMods = null)
        => InstallCore(archivePath, modsLocation, fallbackName, conflictBehaviour, backups, previousVersion,
            cancellationToken, limits, replacePath, null, installedMods);

    /// <summary>
    /// Test-only entry point for exercising recovery failures that cannot be created portably by
    /// locking a directory (Unix permits deleting an open directory while Windows does not).
    /// </summary>
    internal static List<InstalledModFolder> InstallForTesting(
        string archivePath,
        string modsLocation,
        string fallbackName,
        ArchiveConflictBehaviour conflictBehaviour,
        ModBackupStore? backups,
        string? previousVersion,
        CancellationToken cancellationToken,
        ModArchiveExtractionLimits? limits,
        Func<string, string?, Exception?>? forceRestoreFailure,
        string? replacePath = null,
        IReadOnlyList<InstalledModIdentity>? installedMods = null)
        => InstallCore(archivePath, modsLocation, fallbackName, conflictBehaviour, backups, previousVersion,
            cancellationToken, limits, replacePath, forceRestoreFailure, installedMods);

    private static List<InstalledModFolder> InstallCore(
        string archivePath,
        string modsLocation,
        string fallbackName,
        ArchiveConflictBehaviour conflictBehaviour,
        ModBackupStore? backups,
        string? previousVersion,
        CancellationToken cancellationToken,
        ModArchiveExtractionLimits? limits,
        string? replacePath,
        Func<string, string?, Exception?>? forceRestoreFailure,
        IReadOnlyList<InstalledModIdentity>? installedMods = null)
    {
        if (!File.Exists(archivePath))
            throw new ModArchiveException(Text("GUIModArchiveMissing"));
        if (!Directory.Exists(modsLocation))
            throw new ModArchiveException(Text("GUIModArchiveModsFolderMissing"));

        using var archive = OpenArchive(archivePath);

        cancellationToken.ThrowIfCancellationRequested();
        var extractionLimits = limits ?? ModArchiveExtractionLimits.Default;
        var entries = archive.Entries.Where(entry => !entry.IsDirectory && entry.Key is not null).ToList();
        ValidateLimits(entries, extractionLimits, cancellationToken);

        var manifestRoots = entries
            .Where(entry => ManifestNames.Contains(FileName(entry.Key!), StringComparer.OrdinalIgnoreCase))
            .Select(entry => DirectoryOf(entry.Key!))
            .Distinct()
            .OrderBy(root => root.Length)
            .ToList();

        if (manifestRoots.Count == 0)
            throw new ModArchiveException(
                Text("GUIModArchiveManifestMissing"));

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
                .Select(plan => Path.GetFileName(plan.Target))
                .ToList();

            // Matching folder names catch a re-download of the same file and nothing else. Nexus
            // names its downloads with the file id, the version and an upload stamp, so the second
            // install of a mod usually lands in a folder with a different name and the user ends up
            // with the same mod twice - two copies fighting over the same files, and a conflict
            // report blaming a mod for arguing with itself.
            var duplicates = DuplicateInstalls(entries, plans, installedMods, replaceTarget);

            if (existing.Count > 0 || duplicates.Count > 0)
                throw new ModArchiveConflictException(existing, duplicates);
        }

        var installed = new List<ExtractedRoot>();
        var budget = new ExtractionBudget(extractionLimits.MaxTotalBytes);

        // A mod that was installed as a .zip is being replaced by an unpacked folder, so the old
        // archive has to go or the mod list shows both. It is archived rather than deleted, and
        // before extraction rather than after, so a failed unpack restores cleanly - which means
        // remembering where it went, because "put it back" is the whole point of moving it.
        var supersededArchive = SupersededArchive(replacePath, replaceTarget);
        var parkedArchive = supersededArchive is null
            ? null
            : ParkSupersededFile(supersededArchive, backups, previousVersion);

        foreach (var (root, target) in plans)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                installed.Add(ExtractRoot(entries, root, target, backups, previousVersion,
                    extractionLimits, budget, cancellationToken, forceRestoreFailure));
            }
            catch (Exception installError)
            {
                // A bundle installs as a unit. Leaving one of three mods behind after a failure
                // would give the user a mod list that does not match anything they downloaded.
                var rollbackFailures = installed.AsEnumerable().Reverse()
                    .Select(earlier => Restore(earlier.Target, earlier.BackupPath, forceRestoreFailure))
                    .Where(failure => failure is not null)
                    .Select(failure => failure!)
                    .ToList();

                // The .zip the update was superseding was moved out of the way before any of this
                // started. A failed update that leaves it moved has taken the user's installed mod
                // away and given them nothing back, which is worse than the failure itself.
                if (RestoreParkedArchive(parkedArchive, forceRestoreFailure) is { } archiveFailure)
                    rollbackFailures.Add(archiveFailure);

                if (rollbackFailures.Count > 0)
                {
                    var details = string.Join(Environment.NewLine, rollbackFailures.Select(failure =>
                        $"• {Path.GetFileName(failure.Target)}: {failure.Error.Message}{DescribeBackup(failure.BackupPath)}"));
                    throw new ModArchiveException(
                        string.Format(Text("GUIModArchiveRollbackFailed"),
                            installError.Message, details), installError);
                }
                throw;
            }
        }

        foreach (var result in installed.Where(result => result.DeleteBackupAfterBundle))
            TryRemove(result.BackupPath!);

        return installed.Select(result => result.Installed).ToList();
    }

    // ── Already installed ────────────────────────────────────────────────────────

    /// <summary>
    /// The mods in this archive that are already installed somewhere else in the mods folder.
    ///
    /// By manifest identity - author and name - rather than by folder name, because the folder name
    /// is exactly what does not match: Nexus builds a download file name out of the file id, the
    /// version and an upload stamp, so a mod re-downloaded rather than updated lands in a folder
    /// named after that release and sits beside the copy already there.
    /// </summary>
    private static List<InstalledModIdentity> DuplicateInstalls(
        List<IArchiveEntry> entries,
        List<(string Root, string Target)> plans,
        IReadOnlyList<InstalledModIdentity>? installedMods,
        string? replaceTarget)
    {
        if (installedMods is null || installedMods.Count == 0) return [];

        var duplicates = new List<InstalledModIdentity>();

        foreach (var (root, target) in plans)
        {
            var identity = IdentityIn(entries, root);
            if (identity is null) continue;

            duplicates.AddRange(installedMods.Where(installed =>
                installed.Id.Equals(identity, StringComparison.OrdinalIgnoreCase) &&
                !SamePath(installed.SourcePath, target) &&
                !SamePath(installed.SourcePath, replaceTarget)));
        }

        // One install, named once. A bundle whose roots share an identity would otherwise list the
        // same copy twice, which reads as two problems and stops the caller from aiming the
        // replacement at it.
        return duplicates
            .DistinctBy(mod => mod.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>The mod id declared by the manifest at <paramref name="root"/>, if it can be read.</summary>
    private static string? IdentityIn(List<IArchiveEntry> entries, string root)
    {
        var manifest = entries.FirstOrDefault(entry =>
            DirectoryOf(entry.Key!).Equals(root, StringComparison.OrdinalIgnoreCase) &&
            ManifestNames.Contains(FileName(entry.Key!), StringComparer.OrdinalIgnoreCase));

        if (manifest is null) return null;

        try
        {
            using var stream = manifest.OpenEntryStream();
            using var reader = new StreamReader(stream);

            var read = ModIdentity.ReadManifest(FileName(manifest.Key!), reader.ReadToEnd());
            return read is null ? null : ModIdentity.For(read);
        }
        catch (Exception e)
        {
            // Not being able to read it is not a reason to refuse the install - it only means AIM
            // cannot tell whether this is a second copy, so it lets the install go ahead.
            Logger.Log($"Could not read the manifest in the download: {e.Message}");
            return null;
        }
    }

    private static bool SamePath(string left, string? right)
    {
        if (string.IsNullOrWhiteSpace(right)) return false;

        try
        {
            return string.Equals(
                Path.GetFullPath(left.TrimEnd('/', '\\')),
                Path.GetFullPath(right.TrimEnd('/', '\\')),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
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

    /// <summary>
    /// Where the superseded .zip was put, so a failed update can put it back.
    /// </summary>
    /// <param name="StoreFolder">
    /// The stamped backup folder holding it, when the backup store took it. Restoring empties that
    /// folder, and an empty restore point in the versions dropdown is a lie, so it is removed too.
    /// </param>
    private sealed record ParkedArchive(string OriginalPath, string CurrentPath, string? StoreFolder);

    /// <summary>
    /// Moves the superseded .zip out of the mods folder, and says where it went.
    ///
    /// Returning null means the file is still where it was - either there was nothing to move or
    /// the move failed - so there is nothing for a rollback to undo.
    /// </summary>
    private static ParkedArchive? ParkSupersededFile(string path, ModBackupStore? backups, string? previousVersion)
    {
        var leaf = Path.GetFileName(path);

        try
        {
            if (backups?.Archive(path, previousVersion) is { } kept)
            {
                var inStore = Path.Combine(kept.Path, leaf);
                return File.Exists(inStore) ? new ParkedArchive(path, inStore, kept.Path) : null;
            }

            // No store, or the store could not take it. Keeping it aside beside the mods folder is
            // still better than deleting a mod the user has, and better than leaving a duplicate in
            // the list - the installer ignores the .aim-old suffix.
            //
            // An .aim-old already sitting there is a previous update's kept copy. Deleting it to
            // make room would throw away the only copy of a version the user might still want.
            var parked = FreeSidecarName(path);
            File.Move(path, parked);
            return new ParkedArchive(path, parked, null);
        }
        catch (Exception e)
        {
            Logger.Log($"Could not put aside the old archive {leaf}: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Puts a superseded .zip back where it was after the update that replaced it failed.
    /// Returns the failure when it could not be put back, so the caller can name the copy that was
    /// kept instead of reporting a clean rollback that did not happen.
    /// </summary>
    private static RollbackFailure? RestoreParkedArchive(
        ParkedArchive? parked,
        Func<string, string?, Exception?>? forceRestoreFailure)
    {
        if (parked is null) return null;

        try
        {
            var forced = forceRestoreFailure?.Invoke(parked.OriginalPath, parked.CurrentPath);
            if (forced is not null) throw forced;

            // The extraction target is the folder named after the archive, so it cannot be sitting
            // on the archive's own path - but a rollback that has already run leaves nothing to do.
            if (File.Exists(parked.OriginalPath)) return null;

            if (!File.Exists(parked.CurrentPath))
                throw new FileNotFoundException(string.Format(
                    Text("GUIModArchiveParkedCopyMissing"), parked.CurrentPath));

            File.Move(parked.CurrentPath, parked.OriginalPath);

            if (parked.StoreFolder is not null &&
                Directory.Exists(parked.StoreFolder) &&
                Directory.GetFileSystemEntries(parked.StoreFolder).Length == 0)
                Directory.Delete(parked.StoreFolder);

            return null;
        }
        catch (Exception e)
        {
            Logger.Log($"Could not put back the old archive {Path.GetFileName(parked.OriginalPath)}: {e.Message}");
            return new RollbackFailure(parked.OriginalPath, parked.CurrentPath, e);
        }
    }

    /// <summary>
    /// A free <c>.aim-old</c> name beside <paramref name="path"/>. Numbered rather than overwritten:
    /// each of these is the only copy of a version the user once had installed.
    /// </summary>
    private static string FreeSidecarName(string path)
    {
        var candidate = path + ".aim-old";

        for (var attempt = 1; attempt < 1000; attempt++)
        {
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
            candidate = $"{path}.aim-old-{attempt}";
        }

        throw new ModArchiveException(string.Format(
            Text("GUIModArchiveTooManyBackups"), Path.GetFileName(path)));
    }

    // ── Extraction ───────────────────────────────────────────────────────────────

    private static ExtractedRoot ExtractRoot(
        List<IArchiveEntry> entries,
        string root,
        string target,
        ModBackupStore? backups = null,
        string? previousVersion = null,
        ModArchiveExtractionLimits? limits = null,
        ExtractionBudget? budget = null,
        CancellationToken cancellationToken = default,
        Func<string, string?, Exception?>? forceRestoreFailure = null)
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
                // Numbered rather than overwritten: an .aim-old already there was kept because an
                // earlier update could not be filed in the store, and it is the only copy of that
                // version left.
                backup = FreeSidecarName(target);
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
                CopyEntry(input, output, entry.Key!, limits ?? ModArchiveExtractionLimits.Default,
                    budget ?? new ExtractionBudget((limits ?? ModArchiveExtractionLimits.Default).MaxTotalBytes), cancellationToken);
            }

            // A throwaway backup cannot be removed yet: another root in the same Nexus bundle may
            // fail afterwards, in which case this already extracted root must be restored too.
            return new ExtractedRoot(
                new InstalledModFolder(Path.GetFileName(target), target, replaced),
                target,
                backup,
                backup is not null && !backupIsKept);
        }
        catch (Exception e)
        {
            var rollbackFailure = Restore(target, backup, forceRestoreFailure);
            if (rollbackFailure is not null)
                throw new ModArchiveException(string.Format(
                    Text("GUIModArchiveUnpackRollbackFailed"),
                    e.Message, DescribeBackup(rollbackFailure.BackupPath)), e);
            throw new ModArchiveException(
                string.Format(Text("GUIModArchiveUnpackFailed"), e.Message), e);
        }
    }

    private static void ValidateLimits(
        IEnumerable<IArchiveEntry> entries,
        ModArchiveExtractionLimits limits,
        CancellationToken cancellationToken)
    {
        if (limits.MaxEntries <= 0 || limits.MaxEntryBytes <= 0 || limits.MaxTotalBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(limits), "Archive extraction limits must be positive.");

        long declaredTotal = 0;
        var count = 0;
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++count > limits.MaxEntries)
                throw new ModArchiveException(Text("GUIModArchiveTooManyFiles"));
            if (entry.Size < 0 || entry.Size > limits.MaxEntryBytes)
                throw new ModArchiveException(Text("GUIModArchiveFileTooLarge"));
            if (declaredTotal > limits.MaxTotalBytes - entry.Size)
                throw new ModArchiveException(Text("GUIModArchiveTotalTooLarge"));
            declaredTotal += entry.Size;
        }
    }

    private static void CopyEntry(
        Stream input,
        Stream output,
        string entryName,
        ModArchiveExtractionLimits limits,
        ExtractionBudget budget,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[80 * 1024];
        long entryBytes = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = input.Read(buffer, 0, buffer.Length);
            if (read == 0) break;

            if (entryBytes > limits.MaxEntryBytes - read)
                throw new ModArchiveException(string.Format(
                    Text("GUIModArchiveEntryTooLarge"), entryName));
            budget.Consume(read);
            output.Write(buffer, 0, read);
            entryBytes += read;
        }
    }

    internal static void CopyEntryForTesting(
        Stream input,
        Stream output,
        ModArchiveExtractionLimits limits,
        CancellationToken cancellationToken) =>
        CopyEntry(input, output, "test-entry", limits,
            new ExtractionBudget(limits.MaxTotalBytes), cancellationToken);

    private static RollbackFailure? Restore(
        string target,
        string? backup,
        Func<string, string?, Exception?>? forceRestoreFailure)
    {
        try
        {
            var forced = forceRestoreFailure?.Invoke(target, backup);
            if (forced is not null) throw forced;
            if (Directory.Exists(target)) Directory.Delete(target, true);
            if (backup is not null && Directory.Exists(backup)) Directory.Move(backup, target);
            return null;
        }
        catch (Exception e)
        {
            Logger.Log($"Could not restore the previous version of {Path.GetFileName(target)}: {e.Message}");
            return new RollbackFailure(target, backup, e);
        }
    }

    private static string DescribeBackup(string? backup) =>
        string.IsNullOrWhiteSpace(backup) ? " (no backup folder was available)" : $" ({backup})";

    private static void TryRemove(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        catch (Exception e)
        {
            Logger.Log($"Could not remove completed update backup {Path.GetFileName(path)}: {e.Message}");
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
            throw new ModArchiveException(Text("GUIModArchiveUnsupportedFormat"), e);
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
            throw new ModArchiveException(string.Format(
                Text("GUIModArchiveUnsafePath"), relative));

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

    private sealed record ExtractedRoot(
        InstalledModFolder Installed,
        string Target,
        string? BackupPath,
        bool DeleteBackupAfterBundle);

    private sealed record RollbackFailure(string Target, string? BackupPath, Exception Error);

    private sealed class ExtractionBudget(long maximum)
    {
        private long _remaining = maximum;

        public void Consume(int count)
        {
            if (count < 0 || _remaining < count)
                throw new ModArchiveException("The downloaded archive exceeds the supported extracted size.");
            _remaining -= count;
        }
    }
}

/// <summary>
/// Thrown when installing would overwrite folders that are already in the mods folder, or would
/// put a second copy of a mod that is already installed under another name.
/// </summary>
public class ModArchiveConflictException(
    List<string> folders,
    List<InstalledModIdentity>? alreadyInstalled = null)
    : ModArchiveException(Describe(folders, alreadyInstalled))
{
    /// <summary>Folder names in the mods folder that the download would land on top of.</summary>
    public List<string> Folders { get; } = folders;

    /// <summary>
    /// Mods in the download that are already installed elsewhere, and where those copies are.
    /// Replacing one of these is an update; installing beside it is the duplicate this exists to
    /// prevent, so the path is carried along for the caller to aim the install at.
    /// </summary>
    public List<InstalledModIdentity> AlreadyInstalled { get; } = alreadyInstalled ?? [];

    private static string Describe(List<string> folders, List<InstalledModIdentity>? alreadyInstalled)
    {
        var parts = new List<string>();

        if (folders.Count > 0) parts.Add($"Already installed: {string.Join(", ", folders)}");

        if (alreadyInstalled is { Count: > 0 })
            parts.Add("Already installed under another name: " + string.Join(", ",
                alreadyInstalled.Select(mod => $"{mod.Name} ({Path.GetFileName(mod.SourcePath)})")));

        return parts.Count == 0 ? "Already installed." : string.Join(". ", parts);
    }
}
