namespace Garethp.ModsOfMistriaInstallerLib.Research;

public enum FixKind
{
    /// <summary>Nothing is broken. Close the issue.</summary>
    CloseAsHarmless,

    /// <summary>Change which mod loads last, so the right one wins the file they share.</summary>
    Reorder,

    /// <summary>Install a mod that exists to make these two work together.</summary>
    InstallPatch,

    /// <summary>
    /// The fix is already in the mod list and in a position to work. Nothing to install; the issue
    /// can be closed, with a note saying which mod is handling it.
    /// </summary>
    AlreadyFixed,

    /// <summary>
    /// The fix is already in the mod list but is switched off, or loads before the mods it patches.
    /// Turning it on and moving it last is the whole of the work.
    /// </summary>
    UseExistingFix,

    /// <summary>Take one mod's copy of a contested file out of play, keeping the rest of the mod.</summary>
    SetAsideFile,

    /// <summary>AIM cannot fix this. Somebody has to read the page.</summary>
    ReadThePage
}

/// <summary>
/// Something AIM could do about a conflict, phrased so a person can approve it.
/// </summary>
/// <param name="Consequence">
/// What changes if this is applied, in the user's terms - "you will see Foo's sprites instead of
/// Bar's" - because that, not the mechanism, is what they are actually agreeing to.
/// </param>
/// <param name="Reversible">
/// Whether AIM can put things back. False only for things it does not do itself.
/// </param>
public sealed record FixPlan(
    FixKind Kind,
    string Title,
    string Consequence,
    bool Reversible = true)
{
    /// <summary>For <see cref="FixKind.Reorder"/>: the mod that should end up loading last.</summary>
    public string? WinnerModId { get; init; }

    /// <summary>For <see cref="FixKind.InstallPatch"/>: what to install.</summary>
    public PatchCandidate? Patch { get; init; }

    /// <summary>For <see cref="FixKind.SetAsideFile"/>: whose file, and which.</summary>
    public string? TargetModId { get; init; }

    public IReadOnlyList<string> TargetFiles { get; init; } = [];

    /// <summary>
    /// For <see cref="FixKind.AlreadyFixed"/> and <see cref="FixKind.UseExistingFix"/>: the mod the
    /// user already has that this plan is about.
    /// </summary>
    public InstalledFix? ExistingFix { get; init; }
}

/// <summary>
/// Turns a diagnosis and whatever the research turned up into the short list of things that would
/// actually resolve the issue.
///
/// The order is the order a person would try them: if the fix is already sitting in the mod list,
/// say so before proposing a download; if nothing is wrong, say so and stop; if somebody has
/// already written the patch, use theirs rather than improvising; if it comes down to which mod
/// wins, that is a preference and the user picks; and only when none of those apply is editing
/// somebody else's mod on the table at all.
///
/// That first step is why <see cref="InstalledFixScanner"/> runs at all. Offering to install a
/// patch the user installed last month is not merely redundant - it invites them to reinstall a
/// working setup to fix a problem the thing they already have was quietly preventing.
///
/// That last option is offered narrowly and never chosen automatically. Setting a file aside keeps
/// the rest of the mod working and is undone by restoring the snapshot AIM takes first, but it
/// still means the files on disk are no longer the ones the author shipped - so it is something the
/// user asks for, having read what it does.
/// </summary>
public static class FixPlanner
{
    /// <param name="installed">
    /// Mods the user already has that bear on this conflict, from
    /// <see cref="InstalledFixScanner.Scan"/>. Empty when nothing was scanned, which is what the
    /// planner did before it knew to look.
    /// </param>
    public static IReadOnlyList<FixPlan> Plan(
        ConflictDiagnosis diagnosis,
        IReadOnlyList<PatchCandidate> patches,
        IReadOnlyList<(string ModId, string Name)> mods,
        IReadOnlyList<InstalledFix>? installed = null)
    {
        var plans = new List<FixPlan>();
        installed ??= [];

        // Only mods that claim to fix the pairing get to close the issue. A third mod that merely
        // writes the same files is worth reporting, but it has not promised anything.
        var fixes = installed
            .Where(fix => fix.Evidence != FixEvidence.WritesTheSameFiles)
            .ToList();

        var working = fixes.FirstOrDefault(fix => fix.Effective);

        if (working is not null)
            plans.Add(new FixPlan(
                FixKind.AlreadyFixed,
                $"You already have {working.Name}",
                $"{working.Why} It is switched on and loads after both mods, so it is already " +
                "doing whatever it does about this. The issue closes with a note saying so, and " +
                "comes back if that mod is removed or one of these is updated.")
            {
                ExistingFix = working
            });

        foreach (var fix in fixes.Where(fix => !fix.Effective))
        {
            var wrong = (fix.Enabled, fix.LoadsLast) switch
            {
                (false, false) => "it is switched off, and it sits above the mods it is meant to " +
                                  "patch, where they would overwrite it anyway",
                (false, true) => "it is switched off, so nothing it contains reaches the game",
                _ => "it loads before the mods it is meant to patch, so their files overwrite " +
                     "its own and it has no effect"
            };

            plans.Add(new FixPlan(
                FixKind.UseExistingFix,
                $"Turn on and reposition {fix.Name}",
                $"{fix.Why} But {wrong}. AIM will switch it on if needed and move it below both " +
                "mods, which is the only place a patch does anything. Nothing is downloaded and " +
                "nothing is edited.")
            {
                ExistingFix = fix,
                TargetModId = fix.ModId
            });
        }

        // Not both: "mark this as not an issue" and "you already have the patch" are the same
        // button with different reasons, and the second reason is the better one.
        if (working is null && diagnosis.Verdict == DiagnosisVerdict.Harmless && diagnosis.Certain)
            plans.Add(new FixPlan(
                FixKind.CloseAsHarmless,
                "Mark this as not an issue",
                "AIM checked every file these mods share and nothing either of them contributes " +
                "is lost. The issue stops being reported unless one of the mods is updated."));

        // Whatever the user already has, in any form, is not offered as a download.
        var redundant = InstalledFixScanner.AlreadyHave(installed, patches)
            .Select(patch => (patch.ModId, patch.FileId))
            .ToHashSet();

        foreach (var patch in patches.Where(patch => !redundant.Contains((patch.ModId, patch.FileId))).Take(3))
            plans.Add(new FixPlan(
                FixKind.InstallPatch,
                $"Install {patch.Title}",
                patch.Why + " AIM will download and install it directly if your Nexus account " +
                "allows it, and otherwise open its files so one click on \"Mod Manager Download\" " +
                "hands it back here.")
            {
                Patch = patch
            });

        if (diagnosis.Verdict is DiagnosisVerdict.OrderDecides or DiagnosisVerdict.PartialOverride)
            foreach (var (modId, name) in mods)
                plans.Add(new FixPlan(
                    FixKind.Reorder,
                    $"Let {name} win",
                    $"{name} moves below the others, so where the mods disagree, its version is " +
                    "the one the game uses. Nothing is edited and no files are lost.")
                {
                    WinnerModId = modId
                });

        // Only worth offering when there is a specific file to set aside, and only for the mods
        // that are actually losing something.
        var contested = diagnosis.Files
            .Where(file => file.Outcome is FileOutcome.LastWins or FileOutcome.MergesWithOverride)
            .Select(file => file.Path)
            .ToList();

        if (contested.Count > 0)
            foreach (var (modId, name) in mods)
                plans.Add(new FixPlan(
                    FixKind.SetAsideFile,
                    $"Set aside {name}'s copy of the shared {(contested.Count == 1 ? "file" : "files")}",
                    $"The rest of {name} keeps working; only the part that collides is taken out " +
                    "of play. AIM copies the whole mod into your version history first, marks the " +
                    "row as edited, and you can put it back from the version dropdown.")
                {
                    TargetModId = modId,
                    TargetFiles = contested
                });

        if (plans.Count == 0)
            plans.Add(new FixPlan(
                FixKind.ReadThePage,
                "Open the mod pages",
                "AIM could not settle this from the files. The comment threads and bug trackers " +
                "are where this kind of question usually gets answered.",
                Reversible: false));

        return plans;
    }
}
