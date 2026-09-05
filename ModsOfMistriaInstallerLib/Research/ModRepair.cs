using System.Text.RegularExpressions;
using Garethp.ModsOfMistriaInstallerLib.ModTypes;

namespace Garethp.ModsOfMistriaInstallerLib.Research;

/// <summary>
/// One repair AIM worked out for itself, as a change to exactly one line.
/// </summary>
/// <param name="Path">Mod-relative, as <see cref="ModFileEditor.ReplaceLine"/> wants it.</param>
/// <param name="Line">1-based, as every error message and bug report counts them.</param>
/// <param name="Was">The line as it stands, for the diff the user approves.</param>
/// <param name="Becomes">The line afterwards.</param>
/// <param name="Why">
/// The evidence, in the user's terms - "this line points at a file the mod does not contain", not
/// "rule A2 matched". A fix nobody can check is a fix nobody should accept.
/// </param>
public sealed record ModRepair(
    string ModId,
    string Title,
    string Path,
    int Line,
    string Was,
    string Becomes,
    string Why)
{
    /// <summary>The two lines, for the confirmation dialog and the applied-edit record.</summary>
    public string Diff => $"- {Was.Trim()}\n+ {Becomes.Trim()}";
}

/// <summary>
/// The fixes AIM is willing to write into somebody else's mod without being told what to write.
///
/// The bar is deliberately high, and it is not "AIM is fairly confident". It is: the file says
/// something that is demonstrably, checkably false, and the repair is the mechanical consequence of
/// that - not an inference about what the author meant. A mod that points at
/// <c>sprites/portrait.png</c> when there is no such file in the mod is not ambiguous and does not
/// need interpreting; the reference is wrong however you read it.
///
/// So every rule here is removal-shaped. AIM takes the broken line out of play by commenting it
/// out; it never invents a value, guesses a path, or completes something the author left half
/// written. Inventing is the failure mode that would make this feature worse than nothing - a
/// plausible-looking wrong value is much harder for the author to spot in a bug report than a
/// commented-out line with AIM's name on it.
///
/// Two rules clear that bar today:
///
///   • A path-shaped value naming a file the mod does not contain. This is exactly the test
///     <see cref="ValidationTools.CheckSpriteFileExists"/> already applies, so a repair here can
///     never disagree with AIM's own validation.
///   • The same key declared twice in the same table. One of the two is silently discarded by any
///     TOML reader, so the file cannot mean what it appears to say, and which half is lost is not
///     something the author chose.
///
/// Everything else - a missing required field, a malformed line, a value of the wrong type - is
/// reported and left alone, because repairing it would mean deciding what it should have said.
/// </summary>
public static class ModRepairPlanner
{
    /// <summary>
    /// Extensions that make a value a file reference rather than a name that has a dot in it.
    ///
    /// Images and audio only. These are the ones AIM's own validator checks by literal path, so a
    /// missing one is a fact rather than a reading. Data files are left out deliberately: a mod may
    /// name one that another mod supplies, and AIM would be accusing it of a fault that belongs to
    /// the pair.
    /// </summary>
    private static readonly string[] AssetExtensions =
        [".png", ".jpg", ".jpeg", ".gif", ".ogg", ".wav", ".mp3"];

    /// <summary>
    /// A quoted scalar assignment: <c>key = "value"</c>, with an optional trailing comment.
    /// Deliberately narrow. A line AIM cannot parse with certainty is a line it does not touch.
    /// </summary>
    private static readonly Regex Assignment = new(
        """^(?<indent>\s*)(?<key>[A-Za-z0-9_\-.]+)\s*=\s*"(?<value>[^"]*)"\s*(?<trailing>#.*)?$""",
        RegexOptions.Compiled);

    /// <summary>A table header: <c>[thing]</c> or <c>[[thing]]</c>.</summary>
    private static readonly Regex TableHeader = new(
        @"^\s*\[\[?(?<name>[^\]]+)\]\]?\s*(#.*)?$", RegexOptions.Compiled);

    /// <summary>
    /// Every repair AIM can justify in this mod, best first.
    ///
    /// Reading files, so it is meant for a background thread. It never writes anything: applying is
    /// a separate decision made by somebody who has seen the diff.
    /// </summary>
    public static IReadOnlyList<ModRepair> For(IMod mod)
    {
        var repairs = new List<ModRepair>();

        // A mod still packed as an archive cannot be edited at all - the next install would discard
        // the change - so proposing a fix for one would be offering something AIM cannot deliver.
        var folder = mod.GetBasePath();
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return repairs;

        List<string> files;

        try
        {
            files = mod.GetAllFiles(".toml");
        }
        catch (Exception exception)
        {
            Logger.Log($"Could not list {mod.GetName()}'s data files: {exception.Message}");
            return repairs;
        }

        foreach (var absolute in files)
        {
            // GetAllFiles hands back full paths; everything downstream - ReadFile, FileExists,
            // ModFileEditor.ReplaceLine - is mod-relative, and so is every path a bug report or a
            // diagnosis quotes. Convert once, here, rather than in four places that could disagree.
            var path = Relative(folder, absolute);
            if (path is null) continue;

            try
            {
                repairs.AddRange(InFile(mod, path));
            }
            catch (Exception exception)
            {
                // One unreadable file costs its own repairs, not the whole scan.
                Logger.Log($"Could not scan {path} in {mod.GetName()}: {exception.Message}");
            }
        }

        return repairs;
    }

    private static string? Relative(string folder, string absolute)
    {
        try
        {
            var relative = Path.GetRelativePath(folder, absolute).Replace('\\', '/');

            // A file outside the mod folder is not this mod's to repair, whatever put it in the
            // listing.
            return relative.StartsWith("../", StringComparison.Ordinal) ? null : relative;
        }
        catch (Exception exception)
        {
            Logger.Log($"Could not place {absolute} inside {folder}: {exception.Message}");
            return null;
        }
    }

    private static IEnumerable<ModRepair> InFile(IMod mod, string path)
    {
        var text = mod.ReadFile(path);
        if (string.IsNullOrEmpty(text)) yield break;

        var lines = text.Replace("\r\n", "\n").Split('\n');

        // Keys already seen in the table being read. A [[table]] entry starts a fresh scope: an
        // array of tables is meant to repeat its keys, once per entry, and flagging that would
        // report every well-formed list in the mod.
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var table = "";

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var number = index + 1;

            var header = TableHeader.Match(line);

            if (header.Success)
            {
                table = header.Groups["name"].Value.Trim();
                seen.Clear();
                continue;
            }

            var assignment = Assignment.Match(line);
            if (!assignment.Success) continue;

            var key = assignment.Groups["key"].Value;
            var value = assignment.Groups["value"].Value;

            // ── Rule: the same key twice in one table ────────────────────────────────
            if (seen.TryGetValue(key, out var first))
            {
                yield return new ModRepair(
                    mod.GetId(),
                    $"Remove the second \"{key}\" in [{table}]",
                    path,
                    number,
                    line,
                    Comment(line, $"duplicate of line {first}"),
                    $"\"{key}\" is set twice in [{table}] - on line {first} and again here. A TOML " +
                    "reader keeps one and silently discards the other, so the file cannot mean both " +
                    "things, and which one survives is not something the author chose. Commenting " +
                    "out the second makes the file say what it looks like it says.");

                continue;
            }

            seen[key] = number;

            // ── Rule: a path to a file the mod does not contain ──────────────────────
            if (!LooksLikeAssetPath(value)) continue;
            if (Exists(mod, path, value)) continue;

            yield return new ModRepair(
                mod.GetId(),
                $"Remove the reference to {value}",
                path,
                number,
                line,
                Comment(line, "file is not in this mod"),
                $"This line points at \"{value}\", and there is no such file anywhere in " +
                $"{mod.GetName()}'s folder. The game loads what a mod's data files tell it to load, " +
                "so a reference to a file that is not there is a load that cannot succeed. AIM is " +
                "not guessing what the path should have been - it is taking out a line that is " +
                "wrong whatever the intended path was.");
        }
    }

    /// <summary>
    /// A value is a file reference when it carries a file extension AIM recognises. The extension
    /// is what distinguishes <c>"sprites/cat.png"</c> from a display name that happens to contain a
    /// dot, and getting that wrong in the permissive direction would have AIM commenting out prose.
    /// </summary>
    private static bool LooksLikeAssetPath(string value) =>
        value.Length > 0 &&
        !value.Contains("://", StringComparison.Ordinal) &&
        AssetExtensions.Any(extension => value.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The same test AIM's validator applies, plus one allowance: a path may be written relative to
    /// the data file that mentions it. Checking both is what keeps this rule from accusing a
    /// perfectly good mod that organises its folders that way.
    /// </summary>
    private static bool Exists(IMod mod, string dataFile, string value)
    {
        var normalised = value.Replace('\\', '/').TrimStart('/');

        foreach (var candidate in Candidates(dataFile, normalised))
        {
            try
            {
                if (mod.FileExists(candidate)) return true;
            }
            catch (Exception exception)
            {
                // A path that escapes the mod folder makes FileExists throw. That is not a missing
                // file, it is a path AIM has no business judging, so treat it as present and say
                // nothing about it.
                Logger.Log($"Could not check {candidate} in {mod.GetName()}: {exception.Message}");
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> Candidates(string dataFile, string value)
    {
        yield return value;

        var folder = Path.GetDirectoryName(dataFile)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(folder)) yield return $"{folder}/{value}";
    }

    /// <summary>
    /// Comments the line out and signs it, so that anybody reading the file afterwards - the user
    /// in six weeks, or the author reading a bug report - can see at once that AIM did this and
    /// why, rather than finding a line that mysteriously stopped working.
    /// </summary>
    private static string Comment(string line, string why)
    {
        var indent = line.Length - line.TrimStart().Length;
        return $"{line[..indent]}# {line.TrimStart()}  # disabled by AIM: {why}";
    }
}
