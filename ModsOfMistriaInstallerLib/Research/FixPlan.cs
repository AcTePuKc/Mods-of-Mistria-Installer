namespace Garethp.ModsOfMistriaInstallerLib.Research;

public enum FixKind
{
    /// <summary>Nothing is broken. Close the issue.</summary>
    CloseAsHarmless,

    /// <summary>Change which mod loads last, so the right one wins the file they share.</summary>
    Reorder,

    /// <summary>Install a mod that exists to make these two work together.</summary>
    InstallPatch,

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
}

/// <summary>
/// Turns a diagnosis and whatever the research turned up into the short list of things that would
/// actually resolve the issue.
///
/// The order is the order a person would try them: if nothing is wrong, say so and stop; if
/// somebody has already written the patch, use theirs rather than improvising; if it comes down to
/// which mod wins, that is a preference and the user picks; and only when none of those apply is
/// editing somebody else's mod on the table at all.
///
/// That last option is offered narrowly and never chosen automatically. Setting a file aside keeps
/// the rest of the mod working and is undone by restoring the snapshot AIM takes first, but it
/// still means the files on disk are no longer the ones the author shipped - so it is something the
/// user asks for, having read what it does.
/// </summary>
public static class FixPlanner
{
    public static IReadOnlyList<FixPlan> Plan(
        ConflictDiagnosis diagnosis,
        IReadOnlyList<PatchCandidate> patches,
        IReadOnlyList<(string ModId, string Name)> mods)
    {
        var plans = new List<FixPlan>();

        if (diagnosis.Verdict == DiagnosisVerdict.Harmless && diagnosis.Certain)
            plans.Add(new FixPlan(
                FixKind.CloseAsHarmless,
                "Mark this as not an issue",
                "AIM checked every file these mods share and nothing either of them contributes " +
                "is lost. The issue stops being reported unless one of the mods is updated."));

        foreach (var patch in patches.Take(3))
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
