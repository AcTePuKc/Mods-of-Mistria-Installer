using Garethp.ModsOfMistriaInstallerLib.Crash;
using Garethp.ModsOfMistriaInstallerLib.ModTypes;
using Garethp.ModsOfMistriaInstallerLib.Nexus;
using Garethp.ModsOfMistriaInstallerLib.Research;

namespace Garethp.ModsOfMistriaGUI.Models;

/// <summary>
/// One copy of a mod AIM can put back, and what it was taken before.
/// </summary>
/// <param name="IsPreEdit">
/// True for the snapshot taken immediately before AIM changed the mod. It is the one the user
/// almost always means by "undo that", so it is offered by name rather than by date.
/// </param>
public sealed record VersionChoice(string Label, ModBackup Backup, bool IsPreEdit);

/// <summary>
/// Everything the crash window needs to explain a crash and act on it.
///
/// Same arrangement as <see cref="ResearchContext"/> and for the same reason: the window is a
/// dialog with no data context, and the things it must be able to do - switch a mod off, rebuild
/// the game archive, start the game and watch it, edit a mod and roll the edit back - belong to the
/// mod list, the installer and the backup store rather than to a window. They arrive as delegates
/// so the dialog can use them without being handed the page view model.
///
/// Every capability is optional except the first three. A window that cannot rebuild the archive
/// still explains the crash and still writes the bug report; it just does not offer to verify a
/// fix, and says so rather than offering a button that fails.
/// </summary>
/// <param name="Enabled">The enabled mods in load order - the set that was in the game when it broke.</param>
/// <param name="MistriaLocation">The game folder, for reading the built archive.</param>
/// <param name="InstalledAt">
/// When the archive now on disk was published. This is what tells the window whether the crash it
/// is looking at happened to the game the user currently has, or to one that no longer exists.
/// </param>
public sealed record CrashContext(
    IReadOnlyList<IMod> Enabled,
    string MistriaLocation,
    DateTimeOffset? InstalledAt)
{
    public IReadOnlyList<InstalledModView> Installed { get; init; } = [];

    /// <summary>Switches a mod off by id. True when the list actually changed.</summary>
    public Func<string, bool>? Disable { get; init; }

    /// <summary>
    /// Switches a mod back on by id, for a candidate a run has just cleared.
    ///
    /// Elimination only pays for itself if the mods it exonerates come back. A user who works
    /// through eight suspects and finds the eighth should finish with seven mods still installed,
    /// not with seven mods they have to remember to re-tick - and if AIM left them off, the next
    /// run would be testing a game missing seven things for no reason.
    /// </summary>
    public Func<string, bool>? Enable { get; init; }

    /// <summary>
    /// Whether a mod is ticked in the list right now.
    ///
    /// <see cref="Enabled"/> is the set the game had when it crashed, which is the right answer to
    /// every question about the crash and the wrong answer to the only question asked after a
    /// trial: is this mod back on. A run switches mods off and on underneath that snapshot, so the
    /// window asks the list rather than reading a photograph of it.
    /// </summary>
    public Func<string, bool>? IsEnabled { get; init; }

    /// <summary>
    /// Brings one mod's row in the list into line with the verdicts now in <see cref="Trials"/>:
    /// marked as crashing the game, or not.
    ///
    /// A verdict is useless where it is earned. The crash window closes and the user is looking at
    /// two hundred checkboxes, one of them unticked for a reason that is now nowhere on screen.
    ///
    /// It recomputes from the store rather than being told what to display, which is what makes
    /// taking a mark back safe: a mod cleared for the crash on screen may still be the proven
    /// culprit of another one, and only the store knows that.
    /// </summary>
    public Action<string>? RefreshCrasherMark { get; init; }

    /// <summary>
    /// What earlier runs proved, kept between sessions.
    ///
    /// Null when AIM has no mods folder to write it beside, in which case the window still runs
    /// trials - it just cannot remember them, and says so rather than pretending.
    /// </summary>
    public CrashTrialStore? Trials { get; init; }

    /// <summary>
    /// Rebuilds the game's archive from the current selection. Null on success, or the reason it
    /// did not happen.
    ///
    /// Disabling a mod changes nothing the game can see until this runs - the mods are compiled
    /// into assets.zip - so a "disable and try again" that skipped it would test the same game
    /// twice and report that the fix did not work.
    /// </summary>
    public Func<Task<string?>>? Reinstall { get; init; }

    /// <summary>Starts the game, waits, and reports whether the crash came back.</summary>
    public Func<TimeSpan, Task<GameRunOutcome>>? RunAndWatch { get; init; }

    /// <summary>
    /// Takes named files inside a mod out of play, after copying the whole mod into its version
    /// history. The same operation the conflict window uses, so an edit made here appears in the
    /// row's dropdown and is undone the same way.
    /// </summary>
    public Func<string, IReadOnlyList<string>, string, Task<EditOutcome>>? SetAside { get; init; }

    /// <summary>Replaces one line of a file inside a mod, snapshotting the mod first.</summary>
    public Func<string, string, int, string, string, Task<EditOutcome>>? ReplaceLine { get; init; }

    /// <summary>
    /// The fixes AIM can justify for a mod on its own, without a bug thread to copy from.
    ///
    /// Reads every data file in the mod, so it is called off the UI thread and its results cached
    /// rather than recomputed per keystroke.
    /// </summary>
    public Func<string, IReadOnlyList<ModRepair>>? Repairs { get; init; }

    /// <summary>
    /// Applies one of those fixes: snapshot, edit, tag the row, and hold the mod back from updates
    /// so the next update check does not quietly throw the fix away.
    /// </summary>
    public Func<string, ModRepair, Task<EditOutcome>>? ApplyRepair { get; init; }

    /// <summary>Puts back everything AIM set aside in a mod, leaving the rest of the folder alone.</summary>
    public Func<string, Task<EditOutcome>>? PutBack { get; init; }

    /// <summary>The copies of a mod AIM can restore, newest first.</summary>
    public Func<string, IReadOnlyList<VersionChoice>>? Versions { get; init; }

    /// <summary>Rolls a mod back to one of those copies. True when it happened.</summary>
    public Func<string, VersionChoice, Task<bool>>? RestoreVersion { get; init; }

    /// <summary>Takes a mod out of the mods folder entirely, with the list's own confirmation.</summary>
    public Func<string, Task<bool>>? RemoveMod { get; init; }

    /// <summary>What AIM has already changed inside a mod, for the version panel to describe.</summary>
    public Func<string, IReadOnlyList<string>>? EditHistory { get; init; }

    /// <summary>The mod row for a suspect, when it is still in the list.</summary>
    public IMod? Find(string modId) =>
        Enabled.FirstOrDefault(mod => string.Equals(mod.GetId(), modId, StringComparison.OrdinalIgnoreCase))
        ?? Installed.FirstOrDefault(view =>
            string.Equals(view.Mod.GetId(), modId, StringComparison.OrdinalIgnoreCase))?.Mod;

    /// <summary>Where a suspect lives on Nexus, from whatever provenance AIM has.</summary>
    public InstalledModView? Provenance(string modId) =>
        Installed.FirstOrDefault(view =>
            string.Equals(view.Mod.GetId(), modId, StringComparison.OrdinalIgnoreCase));
}
