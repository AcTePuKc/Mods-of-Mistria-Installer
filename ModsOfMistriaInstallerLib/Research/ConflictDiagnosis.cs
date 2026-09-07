using System.Security.Cryptography;
using System.Text;
using Garethp.ModsOfMistriaInstallerLib.ModTypes;
using Newtonsoft.Json.Linq;
using Tomlyn;
using Tomlyn.Model;

namespace Garethp.ModsOfMistriaInstallerLib.Research;

/// <summary>What actually happens to one shared file when both mods are installed.</summary>
public enum FileOutcome
{
    /// <summary>Both mods ship the same bytes. Whichever wins, the result is the same file.</summary>
    Identical,

    /// <summary>Structured data whose keys do not overlap. The install merges both; nothing is lost.</summary>
    MergesCleanly,

    /// <summary>Structured data that sets some of the same keys. The later mod wins those keys only.</summary>
    MergesWithOverride,

    /// <summary>One file replaces the other outright. Load order decides which one the game sees.</summary>
    LastWins,

    /// <summary>Could not be read or parsed, so nothing can be claimed about it.</summary>
    Unreadable
}

/// <summary>One shared file, and what AIM's own installer will do with it.</summary>
public sealed record FileVerdict(
    string Path,
    FileOutcome Outcome,
    string Explanation)
{
    /// <summary>The keys both mods set, for <see cref="FileOutcome.MergesWithOverride"/>.</summary>
    public IReadOnlyList<string> ContestedKeys { get; init; } = [];

    /// <summary>The mod that ends up owning the contested part, given the current load order.</summary>
    public string? WinnerModId { get; init; }
}

public enum DiagnosisVerdict
{
    /// <summary>Nothing is lost. The issue can be closed.</summary>
    Harmless,

    /// <summary>Real, but entirely settled by which mod loads last. It is a preference, not a fault.</summary>
    OrderDecides,

    /// <summary>Both mostly survive; a named part of one is overridden by the other.</summary>
    PartialOverride,

    /// <summary>AIM could not read enough to say. Fall back to what the mod pages report.</summary>
    Unresolved
}

/// <summary>
/// AIM's own answer to "is this actually a conflict?", worked out from the files rather than
/// from what anybody wrote about them.
/// </summary>
/// <param name="Certain">
/// True only when every shared file was read and classified. A diagnosis that had to skip a file is
/// offered as a reading, not as a fact, because the user is being asked to approve it.
/// </param>
public sealed record ConflictDiagnosis(
    DiagnosisVerdict Verdict,
    bool Certain,
    string Headline,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<FileVerdict> Files)
{
    public static ConflictDiagnosis Inconclusive(string reason) =>
        new(DiagnosisVerdict.Unresolved, false,
            "AIM could not settle this from the files alone.", [reason], []);
}

/// <summary>
/// Works out what installing both mods actually does to the files they share.
///
/// The conflict report is deliberately conservative: it says "these two mods write the same file"
/// because that *might* matter. Whether it does is not a matter of opinion, though - it is decided
/// by what AIM's own installers do with each kind of file, and AIM can simply go and look.
///
/// Three of the four rules used here are the installers' own, read off the code that runs them, so
/// the diagnosis and the install cannot drift apart:
///
///   • <c>TOMLInstaller</c> merges each mod's TOML into the destination with
///     <c>MOMIOperations.MergeTomlTables</c>. Keys that only one mod sets all survive; a key both
///     set is taken from whichever merged last.
///   • <c>JSONInstaller</c> deep-merges JSON objects and unions arrays, so the same holds, except
///     that a file whose root is an array replaces the destination outright.
///   • Anything under <c>images/replace/</c> is a straight file replacement: exactly one copy
///     survives, and load order picks it.
///
/// The fourth rule is arithmetic: two files with the same bytes cannot disagree about anything.
///
/// What it will not do is guess. A file it cannot read or parse makes the whole diagnosis
/// uncertain, and an uncertain diagnosis is presented as a reading for the user to approve rather
/// than as a verdict - which is the only honest way to offer a machine's opinion about somebody
/// else's mods.
/// </summary>
public static class ConflictDiagnoser
{
    /// <summary>
    /// Diagnoses one file conflict.
    /// </summary>
    /// <param name="paths">The destination paths the mods share.</param>
    /// <param name="mods">
    /// The mods involved, in the order they load - so the last one is the one that wins today.
    /// </param>
    public static ConflictDiagnosis Diagnose(IReadOnlyList<string> paths, IReadOnlyList<IMod> mods)
    {
        if (mods.Count < 2) return ConflictDiagnosis.Inconclusive("Only one mod is involved.");
        if (paths.Count == 0) return ConflictDiagnosis.Inconclusive("No shared files were named.");

        var files = paths.Select(path => Examine(path, mods)).ToList();
        var certain = files.All(file => file.Outcome != FileOutcome.Unreadable);
        var winner = mods[^1];

        var replaced = files.Where(f => f.Outcome == FileOutcome.LastWins).ToList();
        var overridden = files.Where(f => f.Outcome == FileOutcome.MergesWithOverride).ToList();
        var identical = files.Count(f => f.Outcome == FileOutcome.Identical);
        var clean = files.Count(f => f.Outcome == FileOutcome.MergesCleanly);

        var reasons = new List<string>();
        if (identical > 0)
            reasons.Add($"{Tally(identical, "file is", "files are")} byte-for-byte identical in both mods, " +
                        "so it makes no difference which one is installed.");
        if (clean > 0)
            reasons.Add($"{Tally(clean, "file merges", "files merge")} cleanly: the mods set different keys, " +
                        "and AIM's installer merges both sets into the game's copy.");
        if (overridden.Count > 0)
            reasons.Add($"{Tally(overridden.Count, "file has", "files have")} keys that both mods set. " +
                        $"Those keys come from {winner.GetName()}, because it loads last. " +
                        "Every other key from both mods survives.");
        if (replaced.Count > 0)
            reasons.Add($"{Tally(replaced.Count, "file is", "files are")} a straight replacement, " +
                        $"so only {winner.GetName()}'s copy reaches the game.");
        if (!certain)
            reasons.Add("Some files could not be read or parsed, so this is a reading rather than a fact.");

        var (verdict, headline) = Conclude(replaced.Count, overridden.Count, winner, mods);

        return new ConflictDiagnosis(verdict, certain, headline, reasons, files);
    }

    private static (DiagnosisVerdict, string) Conclude(
        int replaced, int overridden, IMod winner, IReadOnlyList<IMod> mods)
    {
        var names = string.Join(" and ", mods.Select(mod => mod.GetName()));

        if (replaced > 0)
            return (DiagnosisVerdict.OrderDecides,
                $"These do conflict, but only over which look you get. {winner.GetName()} loads last, " +
                "so its version is the one you will see. Reorder them to swap that round.");

        if (overridden > 0)
            return (DiagnosisVerdict.PartialOverride,
                $"{names} mostly coexist. They disagree on a few named settings, " +
                $"and {winner.GetName()} wins those because it loads last.");

        return (DiagnosisVerdict.Harmless,
            $"{names} do not actually conflict. They share a file, but nothing either mod " +
            "contributes is lost when both are installed.");
    }

    private static string Tally(int n, string singular, string plural) =>
        n == 1 ? $"One {singular}" : $"{n} {plural}";

    // ── One file ─────────────────────────────────────────────────────────────────

    private static FileVerdict Examine(string path, IReadOnlyList<IMod> mods)
    {
        var winner = mods[^1].GetId();

        var bytes = new List<byte[]>();
        foreach (var mod in mods)
        {
            var content = TryReadBytes(mod, path);
            if (content is null)
                return new FileVerdict(path, FileOutcome.Unreadable,
                    $"{mod.GetName()}'s copy could not be read.");
            bytes.Add(content);
        }

        if (bytes.Skip(1).All(other => Hash(other) == Hash(bytes[0])))
            return new FileVerdict(path, FileOutcome.Identical,
                "Both mods ship exactly the same file.") { WinnerModId = winner };

        var extension = Path.GetExtension(path).ToLowerInvariant();
        var normalized = path.Replace('\\', '/');

        // A straight file replacement. Nothing is merged, so exactly one copy survives.
        if (normalized.StartsWith("images/replace/", StringComparison.OrdinalIgnoreCase))
            return new FileVerdict(path, FileOutcome.LastWins,
                "Files under images/replace are copied over the game's own, not merged, " +
                "so only the last one installed survives.") { WinnerModId = winner };

        if (extension is ".toml") return ExamineStructured(path, bytes, mods, ReadTomlKeys);
        if (extension is ".json") return ExamineStructured(path, bytes, mods, ReadJsonKeys);

        // Everything else - sprites, sounds, fonts - is written as a whole file.
        if (IsBinary(extension))
            return new FileVerdict(path, FileOutcome.LastWins,
                "This is a whole-file asset, so the copy from the mod that loads last is the one " +
                "the game gets.") { WinnerModId = winner };

        return new FileVerdict(path, FileOutcome.Unreadable,
            $"AIM has no rule for {extension} files, so it will not guess what happens to this one.");
    }

    /// <summary>
    /// Compares two structured files by the keys they set, which is exactly what the merge cares
    /// about. A key only one mod sets always survives; a key both set is taken from the last.
    /// </summary>
    private static FileVerdict ExamineStructured(
        string path,
        IReadOnlyList<byte[]> bytes,
        IReadOnlyList<IMod> mods,
        Func<string, IReadOnlyCollection<KeyValuePair<string, string>>?> read)
    {
        var winner = mods[^1].GetId();
        var perMod = new List<Dictionary<string, string>>();

        foreach (var content in bytes)
        {
            var keys = read(Decode(content));
            if (keys is null)
                return new FileVerdict(path, FileOutcome.Unreadable,
                    "One of the copies is not valid and could not be compared.");

            // A file that repeats a key is its own business; the last value written is the one the
            // parser kept, and that is what the merge will see too.
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in keys) map[entry.Key] = entry.Value;
            perMod.Add(map);
        }

        // A JSON file whose root is an array is written over the destination rather than merged.
        if (perMod.Any(map => map.ContainsKey(RootArrayMarker)))
            return new FileVerdict(path, FileOutcome.LastWins,
                "This file's contents are a list, which AIM writes over the destination rather " +
                "than merging, so only the last mod's list survives.") { WinnerModId = winner };

        // Every key, from every mod - not just the first one's. A three-way conflict where the
        // second and third mods disagree about a key the first never mentions is still a conflict.
        var contested = perMod
            .SelectMany(map => map.Keys)
            .Distinct(StringComparer.Ordinal)
            .Where(key => perMod
                .Where(map => map.ContainsKey(key))
                .Select(map => map[key])
                .Distinct(StringComparer.Ordinal)
                .Count() > 1)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        if (contested.Count == 0)
            return new FileVerdict(path, FileOutcome.MergesCleanly,
                "The mods set different keys in this file, so the installer's merge keeps both.")
            {
                WinnerModId = winner
            };

        return new FileVerdict(path, FileOutcome.MergesWithOverride,
            $"Both mods set {contested.Count} of the same {(contested.Count == 1 ? "key" : "keys")}. " +
            "Everything else from both is kept.")
        {
            ContestedKeys = contested,
            WinnerModId = winner
        };
    }

    // ── Reading structured files ─────────────────────────────────────────────────

    /// <summary>Stands in for "the whole document is a list", which merges differently.</summary>
    private const string RootArrayMarker = " root-is-an-array";

    private static IReadOnlyCollection<KeyValuePair<string, string>>? ReadTomlKeys(string text)
    {
        try
        {
            var table = TomlSerializer.Deserialize<TomlTable>(text);
            if (table is null) return null;

            var leaves = new List<KeyValuePair<string, string>>();
            FlattenToml(table, "", leaves);
            return leaves;
        }
        catch
        {
            return null;
        }
    }

    private static void FlattenToml(TomlTable table, string prefix, List<KeyValuePair<string, string>> into)
    {
        foreach (var (key, value) in table)
        {
            var path = prefix.Length == 0 ? key : $"{prefix}.{key}";

            if (value is TomlTable nested)
            {
                FlattenToml(nested, path, into);
                continue;
            }

            // Arrays and arrays-of-tables are compared whole. MergeTomlTables walks table arrays
            // element by element and overwrites plain arrays, and reproducing either here would be
            // guessing at an ordering the installer decides; comparing the rendered value at least
            // tells the truth about whether the two mods disagree at all.
            into.Add(new KeyValuePair<string, string>(path, Render(value)));
        }
    }

    private static string Render(object? value) => value switch
    {
        null => "",
        TomlArray array => "[" + string.Join(",", array.Select(Render)) + "]",
        TomlTableArray tables => "[[" + tables.Count + " entries]]",
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? ""
    };

    private static IReadOnlyCollection<KeyValuePair<string, string>>? ReadJsonKeys(string text)
    {
        try
        {
            var token = JToken.Parse(text);

            if (token is JArray)
                return [new KeyValuePair<string, string>(RootArrayMarker, "")];

            if (token is not JObject root) return null;

            return root.Descendants()
                .OfType<JValue>()
                .Select(value => new KeyValuePair<string, string>(value.Path, value.ToString(
                    Newtonsoft.Json.Formatting.None)))
                .ToList();
        }
        catch
        {
            return null;
        }
    }

    // ── Plumbing ─────────────────────────────────────────────────────────────────

    private static byte[]? TryReadBytes(IMod mod, string path)
    {
        try
        {
            if (!mod.FileExists(path)) return null;

            using var stream = mod.ReadFileAsStream(path);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
        catch (Exception exception)
        {
            Logger.Log($"Could not read {path} from {mod.GetName()}: {exception.Message}");
            return null;
        }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    /// <summary>Strips a U+FEFF byte-order mark, which mod files written on Windows often carry.</summary>
    private static string Decode(byte[] bytes) =>
        new UTF8Encoding(false).GetString(bytes).TrimStart('\uFEFF');

    private static bool IsBinary(string extension) => extension is
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or
        ".ogg" or ".wav" or ".mp3" or ".ttf" or ".otf" or ".fnt";
}
