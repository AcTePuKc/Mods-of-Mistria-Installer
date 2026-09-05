using Garethp.ModsOfMistriaInstallerLib.ModTypes;
using Garethp.ModsOfMistriaInstallerLib.Nexus;

namespace Garethp.ModsOfMistriaInstallerLib.Research;

/// <summary>What happened when AIM tried to change a mod.</summary>
/// <param name="Backup">
/// The restore point taken beforehand. Never null on success: an edit without one is not attempted.
/// </param>
public sealed record EditOutcome(bool Applied, string Message, ModBackup? Backup = null)
{
    public static EditOutcome Refused(string why) => new(false, why);
}

/// <summary>
/// The one place in AIM that changes a file inside somebody else's mod.
///
/// It is deliberately a single narrow operation - take one file out of play - rather than a general
/// editor, and it works by renaming rather than deleting. A file renamed to <c>.aim-disabled</c> is
/// still there, is obvious to anyone looking at the folder, is ignored by the installer because it
/// no longer has an extension any collector recognises, and is put back by renaming it again. There
/// is no version of this that deletes a user's file.
///
/// Three things happen before a single rename:
///
///   • The mod must be a folder. A mod still packed as .zip or .rar is edited by the archive, and
///     an edit inside it would be silently discarded by the next install - so it is refused with an
///     explanation instead of half-working.
///   • The whole mod is copied into the backup store, where it joins the row's version dropdown as
///     a restore point. The user undoes an AIM edit with the same gesture they already use to undo
///     an update.
///   • The edit is recorded against the mod, so its row is marked and nobody is left wondering six
///     weeks later why the files on disk do not match what the author shipped.
///
/// If any of the three cannot be done, nothing is renamed. A backup that failed to copy is the
/// whole reason not to proceed.
/// </summary>
public static class ModFileEditor
{
    /// <summary>The suffix a set-aside file carries. Recognised by nothing, which is the point.</summary>
    public const string DisabledSuffix = ".aim-disabled";

    /// <summary>
    /// Takes the named files out of play in one mod, keeping the rest of it installed.
    /// </summary>
    /// <param name="paths">Destination-relative paths, as the conflict report names them.</param>
    /// <param name="reason">What this edit was for, shown on the mod's row afterwards.</param>
    public static EditOutcome SetAside(
        IMod mod,
        IReadOnlyList<string> paths,
        string reason,
        ModBackupStore backups,
        AppliedEditStore record)
    {
        var folder = mod.GetBasePath();

        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return EditOutcome.Refused(
                $"{mod.GetName()} is not installed as a folder, so AIM cannot edit it. " +
                "Mods packed as .zip or .rar have to be extracted first - any change inside the " +
                "archive would be thrown away by the next install.");

        var targets = paths
            .Select(path => Path.Combine(folder, path.Replace('/', Path.DirectorySeparatorChar)))
            .Where(File.Exists)
            .ToList();

        if (targets.Count == 0)
            return EditOutcome.Refused("None of those files are in the mod's folder any more.");

        // The restore point comes first and is not optional. Everything below this line is
        // reversible only because this succeeded.
        var backup = backups.Snapshot(folder, $"{mod.GetVersion()} before AIM's fix".Trim());
        if (backup is null)
            return EditOutcome.Refused(
                "AIM could not copy the mod into your version history, so it has not changed " +
                "anything. An edit with no way back is not worth making.");

        var renamed = new List<string>();

        try
        {
            foreach (var target in targets)
            {
                var disabled = target + DisabledSuffix;
                if (File.Exists(disabled)) File.Delete(disabled);

                File.Move(target, disabled);
                renamed.Add(Path.GetRelativePath(folder, target));
            }
        }
        catch (Exception exception)
        {
            // Put back whatever was already renamed. A half-applied edit is the one state that is
            // worse than either applying it or not.
            foreach (var undo in renamed)
            {
                var original = Path.Combine(folder, undo);
                try { File.Move(original + DisabledSuffix, original); } catch { }
            }

            Logger.Log($"Could not set aside files in {mod.GetName()}: {exception}");
            return EditOutcome.Refused(
                $"AIM could not change {mod.GetName()} and has put it back as it was: {exception.Message}");
        }

        record.Record(new AppliedEdit(
            mod.GetId(),
            reason,
            renamed,
            backup.Path,
            DateTimeOffset.UtcNow));

        return new EditOutcome(true,
            $"Set aside {renamed.Count} {(renamed.Count == 1 ? "file" : "files")} in {mod.GetName()}. " +
            "The mod's row is marked as edited, and the version before this is in its dropdown.",
            backup);
    }

    /// <summary>
    /// Replaces one line of one file inside a mod.
    ///
    /// This exists for the fix that is written down somewhere else. A mod's bug thread says "line
    /// 14 of stores.toml needs an icon" or "change the version check to 1.0.4", and until now the
    /// user's options were a text editor and a prayer, with nothing recording that the mod on disk
    /// is no longer the mod the author shipped.
    ///
    /// AIM does not read the fix out of the thread and it does not invent one: the replacement text
    /// is the user's, typed in having read the post. What AIM adds is everything around it - the
    /// snapshot taken first, the record on the mod's row, and the entry in the version dropdown
    /// that puts it all back. The same three preconditions as <see cref="SetAside"/> apply, and for
    /// the same reasons; an edit inside a .zip would be discarded by the next install, and an edit
    /// with no way back is not worth making.
    /// </summary>
    /// <param name="path">Mod-relative, as the diagnosis names it.</param>
    /// <param name="line">1-based, as every error message and bug report counts them.</param>
    /// <param name="replacement">
    /// The new text for that line. An empty string is allowed and means "blank this line out",
    /// which is a real fix for a stray declaration; the line is kept rather than removed so that
    /// every line number below it still means what the bug report said it meant.
    /// </param>
    public static EditOutcome ReplaceLine(
        IMod mod,
        string path,
        int line,
        string replacement,
        string reason,
        ModBackupStore backups,
        AppliedEditStore record)
    {
        var folder = mod.GetBasePath();

        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return EditOutcome.Refused(
                $"{mod.GetName()} is not installed as a folder, so AIM cannot edit it. " +
                "Mods packed as .zip or .rar have to be extracted first - any change inside the " +
                "archive would be thrown away by the next install.");

        var target = Path.Combine(folder, path.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(target))
            return EditOutcome.Refused($"{path} is not in {mod.GetName()}'s folder.");

        string[] lines;
        string newline;

        try
        {
            var text = File.ReadAllText(target);

            // Preserved rather than normalised. Rewriting a mod's whole file with different line
            // endings turns a one-line fix into a diff the author cannot read, and makes the next
            // update's merge look like a conflict on every line.
            newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            lines = text.Replace("\r\n", "\n").Split('\n');
        }
        catch (Exception exception)
        {
            Logger.Log($"Could not read {path} in {mod.GetName()}: {exception}");
            return EditOutcome.Refused($"AIM could not read {path}: {exception.Message}");
        }

        if (line < 1 || line > lines.Length)
            return EditOutcome.Refused(
                $"{path} has {lines.Length} lines, so there is no line {line} to change. " +
                "The file may have been updated since that fix was written.");

        var was = lines[line - 1];

        if (string.Equals(was, replacement, StringComparison.Ordinal))
            return EditOutcome.Refused($"Line {line} of {path} already says exactly that.");

        // The restore point comes first and is not optional. Everything below this line is
        // reversible only because this succeeded.
        var backup = backups.Snapshot(folder, $"{mod.GetVersion()} before AIM's fix".Trim());

        if (backup is null)
            return EditOutcome.Refused(
                "AIM could not copy the mod into your version history, so it has not changed " +
                "anything. An edit with no way back is not worth making.");

        try
        {
            lines[line - 1] = replacement;
            File.WriteAllText(target, string.Join(newline, lines));
        }
        catch (Exception exception)
        {
            Logger.Log($"Could not edit {path} in {mod.GetName()}: {exception}");
            return EditOutcome.Refused(
                $"AIM could not change {path}: {exception.Message}. The mod is as it was, and the " +
                "copy it took beforehand is in the row's version dropdown.");
        }

        record.Record(new AppliedEdit(
            mod.GetId(),
            reason,
            [path],
            backup.Path,
            DateTimeOffset.UtcNow));

        return new EditOutcome(true,
            $"Line {line} of {path} in {mod.GetName()} was changed from \"{Short(was)}\" to " +
            $"\"{Short(replacement)}\". The mod's row is marked as edited, and the version before " +
            "this is in its dropdown.",
            backup);
    }

    private static string Short(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= 60 ? trimmed : trimmed[..57] + "…";
    }

    /// <summary>
    /// Puts back every file AIM set aside in a mod, without touching anything else.
    ///
    /// Restoring the whole snapshot from the version dropdown also works and is the bigger hammer;
    /// this exists so that undoing one edit does not also undo whatever else the user has changed
    /// in the folder since.
    /// </summary>
    public static EditOutcome PutBack(IMod mod, AppliedEditStore record)
    {
        var folder = mod.GetBasePath();
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return EditOutcome.Refused($"{mod.GetName()}'s folder is no longer there.");

        var restored = 0;

        foreach (var disabled in Directory.GetFiles(folder, "*" + DisabledSuffix, SearchOption.AllDirectories))
        {
            var original = disabled[..^DisabledSuffix.Length];

            try
            {
                if (File.Exists(original)) File.Delete(original);
                File.Move(disabled, original);
                restored++;
            }
            catch (Exception exception)
            {
                Logger.Log($"Could not put back {disabled}: {exception.Message}");
            }
        }

        if (restored == 0) return EditOutcome.Refused("There was nothing to put back.");

        record.Forget(mod.GetId());

        return new EditOutcome(true,
            $"Put back {restored} {(restored == 1 ? "file" : "files")} in {mod.GetName()}.");
    }
}
