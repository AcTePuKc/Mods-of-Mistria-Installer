using System.Text.RegularExpressions;
using Garethp.ModsOfMistriaInstallerLib.ModTypes;

namespace Garethp.ModsOfMistriaInstallerLib.Crash;

/// <summary>How sure AIM is that a mod is involved. Drives the order and the colour, nothing else.</summary>
public enum CrashConfidence
{
    /// <summary>Something merely points this way. Worth listing, not worth acting on alone.</summary>
    Possible,

    /// <summary>It writes the data the crash was reading. The usual verdict for a data crash.</summary>
    Likely,

    /// <summary>Its own name appears in the failure, or its data has the exact fault reported.</summary>
    Strong,

    /// <summary>
    /// The crash is inside a file this mod shipped, or the broken entry in the game's own data
    /// carries content only this mod supplies. Not an inference.
    /// </summary>
    Certain
}

/// <summary>One mod the crash points at, and why.</summary>
public sealed record CrashSuspect(
    string ModId,
    string Name,
    string SourcePath,
    CrashConfidence Confidence,
    IReadOnlyList<string> Evidence)
{
    /// <summary>Where in the mod the fault appears to be, when AIM found a specific place.</summary>
    public string? Where { get; init; }
}

/// <summary>AIM's answer to "what crashed my game", before anybody has read a mod page.</summary>
public sealed record CrashDiagnosis(
    string Headline,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<CrashSuspect> Suspects,
    IReadOnlyList<CrashSource> Sources)
{
    /// <summary>
    /// True when the crash is older than the installation now on disk.
    ///
    /// The single most important thing this window can say. A user who has installed, uninstalled
    /// and reordered mods since the crash is looking at a report about a game that no longer
    /// exists, and every suspect below it is an accusation against the wrong mod. It is not a
    /// reason to hide the report - the crash may well recur - but it is a reason to say so first.
    /// </summary>
    public bool Stale { get; init; }

    /// <summary>The mods that were installed when it crashed, if AIM captured that.</summary>
    public IReadOnlyList<string> ModsAtLaunch { get; init; } = [];

    /// <summary>
    /// True when the fault the crash describes is still present in the game archive on disk.
    ///
    /// This is what makes a stale crash actionable rather than merely old: the crash happened to a
    /// game that no longer exists, but if the same broken entry is still in the data the current
    /// game loads, it is going to happen again on the next launch.
    /// </summary>
    public bool StillPresent { get; init; }

    public bool AnyCertain => Suspects.Any(suspect => suspect.Confidence == CrashConfidence.Certain);
}

/// <summary>
/// Works out which mods a crash points at, from the backtrace and the files, with no network and
/// no API key.
///
/// There are four ways a mod gets named here, and they are worth keeping apart because they are
/// worth very different amounts:
///
///   1. The backtrace lands inside the mod's own code. Every mod's GML is installed under a
///      directory named after that mod, so this is not an inference at all - the frame says which
///      mod it is. Nothing else here is as good as this.
///   2. The mod's name appears in the failure itself.
///   3. The crash is in engine code that was reading mod data. Engine code does not change between
///      installs, so when it fails while reading a data set, the data is what changed - and AIM
///      knows exactly which mods contributed to each data set. This is a shortlist, not a verdict:
///      a dozen mods can add to one data set and eleven of them are innocent.
///   4. The broken entry is found in the built data itself, and traced back to the mod that put it
///      there. This is the one that matters. The game told us what was missing; AIM opens the file
///      the game actually loaded, finds the entry that is missing it, and matches the content of
///      that entry against the mods that could have written it. That turns "try disabling these
///      thirteen mods" into "these two, and here are the lines".
/// </summary>
public static class CrashDiagnoser
{
    public static CrashDiagnosis Diagnose(
        GameCrashLog crash,
        IReadOnlyList<IMod> enabled,
        CrashSourceIndex source,
        DateTimeOffset? installedAt)
    {
        var sources = crash.Frames
            .Select(source.Read)
            .OfType<CrashSource>()
            .ToList();

        var scores = new Dictionary<string, Score>(StringComparer.OrdinalIgnoreCase);
        var reasons = new List<string>();
        var stillPresent = false;

        void Note(IMod mod, CrashConfidence confidence, string evidence, string? where = null)
        {
            var id = mod.GetId();

            if (!scores.TryGetValue(id, out var existing))
            {
                scores[id] = new Score(mod, confidence, [evidence], where);
                return;
            }

            if (!existing.Evidence.Contains(evidence)) existing.Evidence.Add(evidence);

            scores[id] = existing with
            {
                Confidence = confidence > existing.Confidence ? confidence : existing.Confidence,
                Where = where ?? existing.Where
            };
        }

        // ── 1. Frames inside a mod's own installed code ──────────────────────────

        var bySymbol = new Dictionary<string, IMod>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in enabled) bySymbol[Symbol(mod)] = mod;

        foreach (var frame in crash.Frames)
        {
            if (frame.Symbol is not { } symbol || !bySymbol.TryGetValue(symbol, out var owner)) continue;

            // Named as the mod ships it rather than as the game installed it. The installed path
            // is assets/gml/scripts/<symbol>/State.gml; the file the user can actually open, and
            // the one a fix from a bug thread will be written against, is gml/State.gml.
            var inTheMod = Shipped(frame, symbol);

            Note(owner,
                frame.Index == 0 ? CrashConfidence.Certain : CrashConfidence.Strong,
                frame.Index == 0
                    ? $"The game broke inside this mod's own code, at {inTheMod} line {frame.Line}."
                    : $"This mod's code is on the call stack, at {inTheMod} line {frame.Line}.",
                $"{inTheMod}:{frame.Line}");
        }

        // ── 2. The mod's name in the failure itself ──────────────────────────────

        foreach (var mod in enabled)
        {
            var symbol = Symbol(mod);

            // Short symbols match half the English language once they are looked for inside a
            // sentence, so the message is searched on a word boundary and very short ones are
            // skipped entirely rather than filling the list with coincidences.
            if (symbol.Length < 6) continue;

            if (Regex.IsMatch(crash.RawReport, $@"\b{Regex.Escape(symbol)}\b", RegexOptions.IgnoreCase) &&
                !crash.Frames.Any(frame => string.Equals(frame.Symbol, symbol, StringComparison.OrdinalIgnoreCase)))
                Note(mod, CrashConfidence.Strong, "This mod is named in the crash message itself.");
        }

        // ── 3 and 4. Engine code reading data that mods contribute to ────────────

        var domains = sources
            .SelectMany(entry => entry.DataDomains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var domain in domains)
        {
            var writers = enabled.Where(mod => DomainFiles(mod, domain).Count > 0).ToList();
            if (writers.Count == 0) continue;

            reasons.Add(
                $"The game crashed while reading its \"{domain}\" data, which {writers.Count} " +
                $"{(writers.Count == 1 ? "installed mod adds to" : "installed mods add to")}.");

            foreach (var mod in writers)
                Note(mod, CrashConfidence.Likely,
                    $"It adds to the \"{domain}\" data the game was reading when it crashed.");

            if (crash.Symptom != CrashSymptom.MissingField || crash.Subject.Length == 0) continue;

            // The built data is the evidence, not the mods' sources. Most of those thirteen mods
            // write the same shape of entry and are perfectly fine; what distinguishes the guilty
            // one is that its entry survived into the file the game loaded still missing the field.
            var faults = BuiltFaults(source, domain, crash.Subject);

            if (faults.Count == 0)
            {
                reasons.Add(
                    $"The \"{domain}\" data in the game archive on disk has no entry missing " +
                    $"{crash.Subject}, so whatever caused this has already been resolved - by an " +
                    "update, a reinstall, or a mod you have since switched off. If it happens " +
                    "again, come back: the mods below are where to look.");
                continue;
            }

            stillPresent = true;

            reasons.Add(
                $"The \"{domain}\" data the game loads right now still has " +
                $"{(faults.Count == 1 ? "one entry" : $"{faults.Count} entries")} with no " +
                $"{crash.Subject}. This crash will happen again on the next launch.");

            foreach (var fault in faults) Blame(fault, writers, domain, crash.Subject, Note);
        }

        // ── 5. Hooks that were required and may not have installed ───────────────

        if (crash.Symptom == CrashSymptom.NotAFunction)
            foreach (var mod in enabled.Where(mod => mod.GetRequiredHooks().Count > 0))
                Note(mod, CrashConfidence.Possible,
                    "It requires engine hooks to be installed, and the crash is a call to something " +
                    "that turned out not to be a function - which is what a hook that did not " +
                    "install looks like from inside the game.");

        var suspects = scores.Values
            .Select(entry => new CrashSuspect(
                entry.Mod.GetId(),
                entry.Mod.GetName(),
                entry.Mod.GetSourcePath(),
                entry.Confidence,
                entry.Evidence) { Where = entry.Where })
            .OrderByDescending(suspect => suspect.Confidence)
            .ThenBy(suspect => suspect.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var stale = installedAt is not null && crash.When < installedAt;

        if (stale)
            reasons.Insert(0, stillPresent
                ? "This crash is older than the mods currently installed - but the fault it " +
                  "describes is still in the game archive on disk, so it is not a stale report. " +
                  "Expect it again on the next launch."
                : "This crash is older than the mods currently installed. AIM rebuilt the game's " +
                  "archive after it happened, so the game that crashed is not the game on disk " +
                  "now - treat everything below as a lead rather than a verdict, and play once " +
                  "before changing anything.");

        foreach (var entry in sources.Take(1))
            if (entry.Function is not null)
                reasons.Add($"It broke inside {entry.Function}(), on: {entry.Text}");

        return new CrashDiagnosis(
            Headline(crash, suspects, stale, stillPresent), reasons, suspects, sources)
        {
            Stale = stale,
            StillPresent = stillPresent,
            ModsAtLaunch = crash.ModsAtLaunch ?? []
        };
    }

    /// <summary>One mod's running score while the rules are being applied.</summary>
    private sealed record Score(
        IMod Mod,
        CrashConfidence Confidence,
        List<string> Evidence,
        string? Where);

    // ── Tracing a broken entry back to the mod that wrote it ─────────────────────

    /// <summary>
    /// Works out which mod put a broken entry into the game's data.
    ///
    /// By its contents, which is the only thing that survives the merge. The entry in the built
    /// file has lost every trace of where it came from, but it still names the items it sells, the
    /// recipes it unlocks or the sprites it draws - and those names were invented by the mod that
    /// added them, so a name that appears in exactly one installed mod's own files identifies that
    /// mod outright. A name that appears in several (a shared icon, a vanilla item) proves nothing
    /// and is used only to narrow the field.
    /// </summary>
    private static void Blame(
        BuiltFault fault,
        IReadOnlyList<IMod> writers,
        string domain,
        string key,
        Action<IMod, CrashConfidence, string, string?> note)
    {
        // The entry's own content first. Only if none of it identifies anybody are the names it
        // was trying to merge into considered, because those are the game's names and being the
        // only installed mod that quotes a stock icon is not evidence of anything.
        var owners = Owners(fault.Literals, writers, domain);

        if (owners.Count == 0) owners = Owners(fault.Matchers, writers, domain);

        var signature = owners.FirstOrDefault(pair => pair.Value.Count == 1);

        if (signature.Value is { Count: 1 })
        {
            note(signature.Value[0], CrashConfidence.Certain,
                $"The game's own \"{domain}\" data has an entry with no {key} at line {fault.Line}, " +
                $"and it contains \"{signature.Key}\" - which no other installed mod defines. " +
                (fault.Identifies
                    ? "The entry still carries its MOMIidentify block, which means the merge it was " +
                      "waiting for never matched anything: instead of updating an entry that " +
                      $"already had a {key}, it was added as a new one without any."
                    : $"An entry with no {key} is exactly what the game stopped on."),
                $"{domain} line {fault.Line}");

            return;
        }

        // No single owner. Everything that touched the entry is named, at a lower confidence,
        // rather than picking one of them and being wrong about it.
        foreach (var mod in owners.SelectMany(pair => pair.Value).Distinct())
            note(mod, CrashConfidence.Strong,
                $"The game's \"{domain}\" data has an entry with no {key} at line {fault.Line}, and " +
                "this mod is one of the ones that could have written it.",
                $"{domain} line {fault.Line}");
    }

    /// <summary>Which of the writers quotes each of these names in its own data for this domain.</summary>
    private static Dictionary<string, List<IMod>> Owners(
        IReadOnlyList<string> names, IReadOnlyList<IMod> writers, string domain)
    {
        var owners = new Dictionary<string, List<IMod>>(StringComparer.Ordinal);

        foreach (var name in names)
        foreach (var mod in writers)
            if (Mentions(mod, domain, name))
            {
                if (!owners.TryGetValue(name, out var list)) owners[name] = list = [];
                list.Add(mod);
            }

        return owners;
    }

    /// <summary>Whether a mod's own data for this domain contains a literal by that exact name.</summary>
    private static bool Mentions(IMod mod, string domain, string literal)
    {
        var quoted = "\"" + literal + "\"";

        foreach (var file in DomainFiles(mod, domain))
        {
            try
            {
                if (mod.ReadFile(file).Contains(quoted, StringComparison.Ordinal)) return true;
            }
            catch (Exception exception)
            {
                Logger.Log($"Could not search {file} in {mod.GetName()}: {exception.Message}");
            }
        }

        return false;
    }

    // ── The one-line answer ──────────────────────────────────────────────────────

    private static string Headline(
        GameCrashLog crash, IReadOnlyList<CrashSuspect> suspects, bool stale, bool stillPresent)
    {
        if (stale && !stillPresent && suspects.Count == 0)
            return "This crash predates your current install, and nothing in it matches a mod you have now.";

        var top = suspects.FirstOrDefault();
        if (top is null) return Describe(crash);

        var certain = suspects.Count(suspect => suspect.Confidence == CrashConfidence.Certain);

        return top.Confidence switch
        {
            CrashConfidence.Certain when certain == 1 => $"{top.Name} is what crashed the game.",
            CrashConfidence.Certain =>
                $"{certain} mods each put something into the game's data that it cannot load. " +
                "Any one of them is enough to cause this.",
            CrashConfidence.Strong => $"{top.Name} is the most likely cause.",
            CrashConfidence.Likely when suspects.Count(s => s.Confidence >= CrashConfidence.Likely) == 1 =>
                $"{top.Name} is the only installed mod that touches what the game was reading.",
            CrashConfidence.Likely =>
                $"{suspects.Count(s => s.Confidence >= CrashConfidence.Likely)} installed mods add to " +
                "the data the game crashed on. One of them is very likely the cause.",
            _ => "Nothing points clearly at a mod, but a few are worth ruling out."
        };
    }

    private static string Describe(GameCrashLog crash) => crash.Symptom switch
    {
        CrashSymptom.MissingField =>
            $"Something the game loaded was missing its \"{crash.Subject}\", and no installed mod " +
            "obviously owns it.",
        CrashSymptom.UndefinedVariable =>
            $"The game read \"{crash.Subject}\" before anything set it. That is usually mod code.",
        CrashSymptom.NotAFunction =>
            "The game called something that was not a function - usually a hook that did not install.",
        CrashSymptom.MissingAsset =>
            $"The game could not find the asset \"{crash.Subject}\" - usually a sprite a mod refers " +
            "to but does not ship.",
        _ => "AIM could not tie this crash to a mod from the backtrace alone."
    };

    // ── Reading the data ─────────────────────────────────────────────────────────

    /// <summary>The install namespace this mod's GML lands in. Mirrors <c>GmlModCode.Symbol</c>.</summary>
    private static string Symbol(IMod mod) => mod.GetId().Replace('.', '_').Replace('-', '_');

    /// <summary>
    /// A mod's own files for one fiddle data set, as mod-relative paths.
    ///
    /// Relative rather than absolute because <see cref="IMod.ReadFile"/> resolves against the mod,
    /// while <see cref="IMod.GetFilesInFolder"/> hands back full paths - a mismatch that silently
    /// reads nothing rather than failing, which is the worst way for it to be wrong.
    /// </summary>
    private static List<string> DomainFiles(IMod mod, string domain)
    {
        var found = new List<string>();

        try
        {
            foreach (var extension in Extensions)
                if (mod.FileExists($"fiddle/{domain}{extension}"))
                    found.Add($"fiddle/{domain}{extension}");

            if (mod.FolderExists($"fiddle/{domain}"))
            {
                var basePath = mod.GetBasePath().Replace('\\', '/').TrimEnd('/') + "/";

                found.AddRange(mod.GetFilesInFolder($"fiddle/{domain}")
                    .Select(path => path.Replace('\\', '/'))
                    .Where(path => Extensions.Any(extension =>
                        path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
                    .Select(path => basePath.Length > 1 &&
                                    path.StartsWith(basePath, StringComparison.OrdinalIgnoreCase)
                        ? path[basePath.Length..]
                        : path));
            }
        }
        catch (Exception exception)
        {
            Logger.Log($"Could not list {domain} files in {mod.GetName()}: {exception.Message}");
        }

        return found;
    }

    private static readonly string[] Extensions = [".toml", ".json"];

    /// <summary>An entry in the built data that is missing the field the game demanded.</summary>
    /// <param name="Literals">
    /// The quoted names inside it. These are the fingerprints that trace it back to a mod.
    /// </param>
    /// <param name="Identifies">
    /// True when the entry still carries a MOMIidentify block. That block is an instruction to the
    /// installer, not content: finding it in the built file means the merge it asked for never
    /// matched, which is a different bug from an author simply omitting a field - and one worth
    /// telling the author about in those words.
    /// </param>
    /// <param name="Matchers">
    /// The names from the MOMIidentify block - what the entry was trying to merge into. Searched
    /// only if the content did not identify anyone, because these are usually vanilla names that
    /// several mods quote and one of them being the only installed user of a stock icon does not
    /// make it guilty.
    /// </param>
    private sealed record BuiltFault(
        int Line,
        string Header,
        IReadOnlyList<string> Literals,
        IReadOnlyList<string> Matchers,
        bool Identifies);

    private static readonly Regex BlockHeader = new(@"^\s*\[\[(?<name>[^\]]+)\]\]\s*$", RegexOptions.Compiled);

    private static readonly Regex SubTable = new(@"^\s*\[(?<name>[^\]]+)\]\s*$", RegexOptions.Compiled);

    /// <summary>Quoted names long enough to be somebody's identifier rather than a flag or a word.</summary>
    private static readonly Regex Literal =
        new("\"(?<name>[A-Za-z_][A-Za-z0-9_]{5,})\"", RegexOptions.Compiled);

    /// <summary>
    /// Every table-array entry in the game's built data for this domain that never sets the named
    /// key at its own level.
    ///
    /// Deliberately literal about "its own level": a key nested inside a MOMIidentify block is not
    /// the entry's value, it is a description of the entry the mod hoped to merge into. Counting it
    /// is exactly the mistake that hides this class of bug, because the broken entries are the ones
    /// where that merge did not happen.
    /// </summary>
    private static List<BuiltFault> BuiltFaults(CrashSourceIndex source, string domain, string key)
    {
        var faults = new List<BuiltFault>();

        foreach (var extension in Extensions)
        {
            var lines = source.ReadAll($"assets/fiddle/{domain}{extension}");
            if (lines is null) continue;

            var assignment = new Regex($@"^\s*{Regex.Escape(key)}\s*=", RegexOptions.IgnoreCase);

            var header = "";
            var line = 0;
            var hasKey = false;
            var identifies = false;
            var inBody = false;
            var inIdentify = false;
            var literals = new List<string>();
            var matchers = new List<string>();

            void Close()
            {
                if (header.Length > 0 && !hasKey)
                    faults.Add(new BuiltFault(
                        line,
                        header,
                        literals.Distinct(StringComparer.Ordinal).ToList(),
                        matchers.Distinct(StringComparer.Ordinal).ToList(),
                        identifies));
            }

            for (var i = 0; i < lines.Count; i++)
            {
                var match = BlockHeader.Match(lines[i]);

                if (match.Success)
                {
                    Close();

                    header = match.Groups["name"].Value.Trim();
                    line = i + 1;
                    hasKey = false;
                    identifies = false;
                    inBody = true;
                    inIdentify = false;
                    literals = [];
                    matchers = [];
                    continue;
                }

                if (header.Length == 0) continue;

                var sub = SubTable.Match(lines[i]);

                if (sub.Success)
                {
                    // A nested table ends the entry's own key/value lines. Which nested table it is
                    // decides where its contents count: a MOMIidentify block describes the entry
                    // this one wanted to merge into, so its names belong to the game rather than to
                    // the mod, and cannot be used to prove who wrote this.
                    inBody = false;
                    inIdentify = sub.Groups["name"].Value
                        .Contains("MOMIidentify", StringComparison.OrdinalIgnoreCase);

                    if (inIdentify) identifies = true;
                    continue;
                }

                if (inBody && assignment.IsMatch(lines[i])) hasKey = true;
                if (inBody && lines[i].Contains("MOMIidentify", StringComparison.OrdinalIgnoreCase))
                    identifies = true;

                foreach (Match found in Literal.Matches(lines[i]))
                    (inIdentify ? matchers : literals).Add(found.Groups["name"].Value);
            }

            Close();

            // The domain is one file or the other, never both, and having read one there is no
            // reason to look for a second.
            if (faults.Count > 0 || lines.Count > 0) break;
        }

        return faults;
    }

    private static string Trim(string path) =>
        path.StartsWith("assets/", StringComparison.OrdinalIgnoreCase) ? path["assets/".Length..] : path;

    /// <summary>
    /// A frame's path as it exists inside the mod, undoing the install's own mapping.
    ///
    /// <c>GmlLayer.Stage</c> installs each of a mod's <c>gml/…</c> files to
    /// <c>assets/gml/scripts/&lt;symbol&gt;/…</c>, so this is that in reverse. It matters because
    /// every use of the answer - showing the user where to look, prefilling the edit form, quoting
    /// a line in a bug report - is about the mod's own folder, which is the only copy anyone can
    /// change.
    /// </summary>
    private static string Shipped(CrashFrame frame, string symbol)
    {
        var prefix = $"assets/gml/scripts/{symbol}/";

        return frame.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? "gml/" + frame.Path[prefix.Length..]
            : Trim(frame.Path);
    }
}
