// PatchCandidate and ResearchResult sit in the library's root namespace beside ConflictResearch,
// while the diagnosis types are in its Research child. Both are needed here.
using Garethp.ModsOfMistriaInstallerLib;
using Garethp.ModsOfMistriaInstallerLib.ModTypes;
using Garethp.ModsOfMistriaInstallerLib.Research;

namespace Garethp.ModsOfMistriaGUI.Models;

/// <summary>
/// Everything the research window needs to diagnose one conflict and act on it.
///
/// The window used to receive only the mods' Nexus identities, because reading their pages was all
/// it did. Diagnosing the conflict means opening the files themselves, and applying a fix means
/// reaching back into the mod list, the Nexus downloader and the backup store - none of which a
/// dialog owns. So the capabilities arrive as delegates, for the same reason
/// <see cref="ConflictReportActions"/> does: the window has no data context to bind through, and
/// giving it one would mean handing a modal dialog the whole page view model.
/// </summary>
/// <param name="Mods">
/// The mods in conflict, in load order, so the last one is the one that wins as things stand. These
/// are the real <see cref="IMod"/> objects: the diagnosis reads their files.
/// </param>
/// <param name="SharedPaths">The destination paths they collide on.</param>
/// <param name="MakeWin">
/// Moves the named mod below the others, so its copy of the shared files is the one that survives.
/// True when the order actually changed.
/// </param>
/// <param name="InstallPatch">
/// Downloads and installs a mod that fixes the pairing. Returns null on success, or a message
/// saying what stopped it.
/// </param>
/// <param name="SetAside">
/// Takes one mod's copy of the contested files out of play, after copying the whole mod into its
/// version history. Null when AIM has nowhere to put the backup.
/// </param>
/// <param name="Installed">
/// Every mod in the list, in load order, with the Nexus ids AIM has for each. This is what lets the
/// window check whether the user already has the fix before offering to download one - without it,
/// research treats the mod list as though it contained only the two mods in the conflict.
/// </param>
/// <param name="Enable">
/// Switches a mod on by id. Needed because a patch the user already installed and then disabled is
/// indistinguishable, from the game's point of view, from one they never had.
/// </param>
public sealed record ResearchContext(
    IReadOnlyList<IMod> Mods,
    IReadOnlyList<string> SharedPaths,
    Func<string, bool> MakeWin,
    Func<PatchCandidate, Task<string?>> InstallPatch,
    Func<string, IReadOnlyList<string>, string, Task<EditOutcome>>? SetAside)
{
    public IReadOnlyList<InstalledModView> Installed { get; init; } = [];

    public Func<string, bool>? Enable { get; init; }

    /// <summary>
    /// Takes a mod out of the mods folder, by id. True when it is gone.
    ///
    /// The confirmation, the recycle bin and the Nexus bookkeeping all belong to the mod list, so
    /// this is the same removal the row's own context menu performs - not a second one that would
    /// have to be kept in step with it.
    /// </summary>
    public Func<string, Task<bool>>? RemoveMod { get; init; }
}
