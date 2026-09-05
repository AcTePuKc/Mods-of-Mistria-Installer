using Garethp.ModsOfMistriaInstallerLib.ModTypes;

namespace Garethp.ModsOfMistriaInstallerLib.Research;

/// <summary>Why an installed mod is thought to bear on a conflict.</summary>
public enum FixEvidence
{
    /// <summary>
    /// It is the very file the research was about to offer to download. Certain, because the match
    /// is on Nexus's own ids rather than on anything anybody wrote.
    /// </summary>
    SamePatch,

    /// <summary>Its name is the name of the patch the research found. Near-certain.</summary>
    NamedLikeThePatch,

    /// <summary>
    /// It names or requires every mod in the conflict. That is what a compatibility patch is, and
    /// it is the only signal that survives when the patch never reached Nexus.
    /// </summary>
    BridgesBothMods,

    /// <summary>
    /// It ships its own copy of files the conflict is about. It may not call itself a patch, but it
    /// is already in the argument and the user should know before another mod is added to it.
    /// </summary>
    WritesTheSameFiles
}

/// <summary>
/// A mod the user already has that bears on this conflict.
/// </summary>
/// <param name="Why">
/// The evidence in a sentence, because "you already have this" is only useful if the user can see
/// how AIM decided that and disagree with it.
/// </param>
public sealed record InstalledFix(
    string ModId,
    string Name,
    FixEvidence Evidence,
    string Why)
{
    /// <summary>True when the mod is switched on. A disabled patch is not a patch.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// True when it loads after every mod in the conflict. A patch that loads before the mods it
    /// patches is overwritten by them and does nothing, which looks exactly like not having it.
    /// </summary>
    public bool LoadsLast { get; init; }

    /// <summary>The conflict's files this mod also writes, if any.</summary>
    public IReadOnlyList<string> SharedFiles { get; init; } = [];

    /// <summary>The download this makes unnecessary, when it matches one the research found.</summary>
    public PatchCandidate? Supersedes { get; init; }

    public string? PageUrl { get; init; }

    /// <summary>Nothing more to do: it is here, switched on, and in a position to take effect.</summary>
    public bool Effective => Enabled && LoadsLast;
}

/// <summary>
/// One installed mod, with the two things the scan needs that <see cref="IMod"/> does not carry:
/// where it sits in the load order, and which Nexus file it came from.
/// </summary>
/// <param name="Position">Higher loads later. Only the ordering matters, not the values.</param>
public sealed record InstalledModView(
    IMod Mod,
    int Position,
    bool Enabled)
{
    public int? NexusModId { get; init; }

    public int? NexusFileId { get; init; }

    public string? PageUrl { get; init; }
}

/// <summary>
/// Looks through the mods the user already has for something that already answers this conflict,
/// before AIM offers to download anything.
///
/// The window used to research a conflict as though the mod list were only the two mods in it. So
/// it would find the compatibility patch on Nexus, present it as the fix, and offer to install it -
/// to someone who had installed it a month earlier. Worse, the patch being installed is often the
/// reason the conflict is *not* a problem, and AIM was throwing that fact away and then asking the
/// user to act on the diagnosis without it.
///
/// Four things count as an answer here, strongest first:
///
///   • The patch the research found is already installed, matched on Nexus's ids. Note the file id
///     is checked when the candidate names one: an optional compatibility file lives on a mod page
///     the user by definition already has, so matching the mod id alone would call every optional
///     patch "already installed".
///   • An installed mod carries the patch's name. This is how a patch installed by hand, from
///     Discord or from an older manual download, is recognised - it never went through Nexus, so it
///     has no ids to match.
///   • An installed mod requires, or is named after, every mod in the conflict. Declaring both as
///     requirements is what a compatibility patch does, and it is a local fact that needs neither
///     the network nor a Nexus key.
///   • An installed mod writes the conflict's own files. It has not claimed to fix anything, but it
///     is a third party to the argument, and its presence changes who wins.
///
/// The last of those is the only one that reads files, and it reads only the paths already named in
/// the conflict - the same ones the diagnosis opens. That still makes it one existence check per
/// installed mod per shared path, which on a long list and a seventy-four-file conflict is a lot of
/// disk, so callers run it off the UI thread the way they already run the diagnosis.
/// </summary>
public static class InstalledFixScanner
{
    /// <summary>
    /// A mod that writes this many or more of the conflict's files is reported even when nothing
    /// else about it says "patch". One shared file out of eleven is a coincidence worth staying
    /// quiet about; it takes a real overlap before a third mod is worth interrupting for.
    /// </summary>
    private const double OverlapShare = 0.25;

    /// <param name="installed">Every mod in the list, including the ones in conflict.</param>
    /// <param name="conflicting">The mods the issue is about.</param>
    /// <param name="sharedPaths">The paths they collide on.</param>
    /// <param name="patches">What the research turned up, so matches can be tied back to it.</param>
    public static IReadOnlyList<InstalledFix> Scan(
        IReadOnlyList<InstalledModView> installed,
        IReadOnlyList<IMod> conflicting,
        IReadOnlyList<string> sharedPaths,
        IReadOnlyList<PatchCandidate> patches)
    {
        if (installed.Count == 0 || conflicting.Count == 0) return [];

        var inConflict = conflicting
            .Select(mod => mod.GetSourcePath())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var conflictNames = conflicting.Select(mod => mod.GetName()).ToList();

        // The bar a patch has to clear to be doing anything: below every mod it patches.
        var lastConflicting = installed
            .Where(view => inConflict.Contains(view.Mod.GetSourcePath()))
            .Select(view => view.Position)
            .DefaultIfEmpty(int.MinValue)
            .Max();

        var found = new List<InstalledFix>();

        foreach (var view in installed)
        {
            if (inConflict.Contains(view.Mod.GetSourcePath())) continue;

            var overlap = FilesAlsoWritten(view.Mod, sharedPaths);

            var reading =
                MatchesAPatch(view, patches) ??
                NamedLikeAPatch(view, patches, conflictNames) ??
                BridgesTheConflict(view, conflictNames) ??
                WritesTheSameFiles(overlap, sharedPaths);

            if (reading is null) continue;

            var (evidence, why, superseded) = reading.Value;

            found.Add(new InstalledFix(view.Mod.GetId(), view.Mod.GetName(), evidence, why)
            {
                Enabled = view.Enabled,
                LoadsLast = view.Position > lastConflicting,
                SharedFiles = overlap,
                Supersedes = superseded,
                PageUrl = view.PageUrl ?? superseded?.Url
            });
        }

        // Strongest evidence first, and among equals the ones that are actually doing something,
        // because "you have it but it is switched off" is a different sentence to "you have it".
        return found
            .OrderBy(fix => fix.Evidence)
            .ThenByDescending(fix => fix.Effective)
            .ThenBy(fix => fix.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// The patches from <paramref name="patches"/> that <paramref name="found"/> shows are already
    /// installed - so the caller can stop offering to download them.
    /// </summary>
    public static IReadOnlyList<PatchCandidate> AlreadyHave(
        IReadOnlyList<InstalledFix> found,
        IReadOnlyList<PatchCandidate> patches)
    {
        var have = found
            .Select(fix => fix.Supersedes)
            .OfType<PatchCandidate>()
            .Select(patch => (patch.ModId, patch.FileId))
            .ToHashSet();

        return patches.Where(patch => have.Contains((patch.ModId, patch.FileId))).ToList();
    }

    // ── The four readings ────────────────────────────────────────────────────────

    private static (FixEvidence, string, PatchCandidate?)? MatchesAPatch(
        InstalledModView view,
        IReadOnlyList<PatchCandidate> patches)
    {
        if (view.NexusModId is not { } modId) return null;

        var patch = patches.FirstOrDefault(candidate =>
            candidate.ModId == modId &&
            // A candidate that names a file is an optional download sitting on a mod page the user
            // already has. Only the file itself counts as having it.
            (candidate.FileId is null || candidate.FileId == view.NexusFileId));

        return patch is null
            ? null
            : (FixEvidence.SamePatch,
                $"This is {patch.Title}, the patch AIM was about to offer you - you installed it " +
                "already.",
                patch);
    }

    private static (FixEvidence, string, PatchCandidate?)? NamedLikeAPatch(
        InstalledModView view,
        IReadOnlyList<PatchCandidate> patches,
        IReadOnlyList<string> conflictNames)
    {
        // The folder as well as the title: a patch installed by hand is often only ever identified
        // by the name of the folder it was unzipped into.
        var names = new[] { view.Mod.GetName(), Path.GetFileName(view.Mod.GetSourcePath().TrimEnd('/', '\\')) };

        var mine = names
            .SelectMany(ModNameMatcher.DistinctiveWords)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // The words the patch's title borrows from the mods it patches cannot be what identifies
        // it. A patch called "March Expanded - Portrait Compatibility Patch" shares "March" and
        // "Expanded" with an add-on called "March Expanded Extras", which is enough for a name
        // match and is not remotely enough to stop offering the patch. What makes it the patch is
        // the part of the title the conflict's own mods do not account for.
        var theirs = conflictNames
            .SelectMany(ModNameMatcher.DistinctiveWords)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var patch = patches.FirstOrDefault(candidate =>
            names.Any(name => ModNameMatcher.Mentions(name, candidate.Title)) &&
            ModNameMatcher.DistinctiveWords(candidate.Title)
                .Any(word => !theirs.Contains(word) && mine.Contains(word)));

        return patch is null
            ? null
            : (FixEvidence.NamedLikeThePatch,
                $"Its name matches {patch.Title}, the patch AIM found on Nexus, so this looks like " +
                "the same thing installed by hand.",
                patch);
    }

    private static (FixEvidence, string, PatchCandidate?)? BridgesTheConflict(
        InstalledModView view,
        IReadOnlyList<string> conflictNames)
    {
        if (conflictNames.Count < 2) return null;

        var required = view.Mod.GetRequirements();
        var title = view.Mod.GetName();

        var bridges = conflictNames.All(name =>
            ModNameMatcher.Mentions(title, name) ||
            required.Any(requirement => ModNameMatcher.Mentions(requirement.Name, name)));

        if (!bridges) return null;

        var how = conflictNames.All(name => ModNameMatcher.Mentions(title, name))
            ? "Its name is about both of the mods in this conflict"
            : "It lists both of the mods in this conflict as requirements";

        return (FixEvidence.BridgesBothMods,
            $"{how}, which is what a compatibility patch looks like from here.",
            null);
    }

    private static (FixEvidence, string, PatchCandidate?)? WritesTheSameFiles(
        IReadOnlyList<string> overlap,
        IReadOnlyList<string> sharedPaths)
    {
        if (sharedPaths.Count == 0 || overlap.Count == 0) return null;

        // One file out of many is a coincidence, and a section that is usually coincidences stops
        // being read. Two is the floor, except where the conflict is a single file - there, writing
        // it is writing all of it.
        var floor = sharedPaths.Count == 1 ? 1 : Math.Max(2, sharedPaths.Count * OverlapShare);
        if (overlap.Count < floor) return null;

        var count = overlap.Count == sharedPaths.Count
            ? "every one of"
            : $"{overlap.Count} of the {sharedPaths.Count}";

        return (FixEvidence.WritesTheSameFiles,
            $"It ships its own copy of {count} the files this conflict is about, so it is already " +
            "part of what happens to them.",
            null);
    }

    private static IReadOnlyList<string> FilesAlsoWritten(IMod mod, IReadOnlyList<string> paths)
    {
        var written = new List<string>();

        foreach (var path in paths)
        {
            try
            {
                if (mod.FileExists(path)) written.Add(path);
            }
            catch (Exception exception)
            {
                // A mod whose archive will not open is not a reason to abandon the scan, but a
                // half-read one must not be reported either: a truncated count would be measured
                // against the overlap floor as though it were the whole answer, and could turn a
                // mod that writes every one of these files into one that writes two of them.
                Logger.Log($"Could not look inside {mod.GetName()} for {path}: {exception.Message}");
                return [];
            }
        }

        return written;
    }
}
