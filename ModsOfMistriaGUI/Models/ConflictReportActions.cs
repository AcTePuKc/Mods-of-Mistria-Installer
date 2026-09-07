using Avalonia.Controls;
using Garethp.ModsOfMistriaInstallerLib;
using Garethp.ModsOfMistriaInstallerLib.GmlMods;

namespace Garethp.ModsOfMistriaGUI.Models;

/// <summary>
/// What the conflict report can do about an issue, rather than merely describe it.
///
/// The report is a modal dialog over the mod list, so every one of these reaches back into the page
/// view model that owns the list, the Nexus client and the backup store. They are handed in as
/// delegates for the same reason <see cref="ModRowCommands"/> is: the window has no data context of
/// its own to bind through.
///
/// Null when the window is showing the load-order summary instead, which reports what already
/// happened and has nothing to act on.
/// </summary>
/// <param name="MakeModWin">
/// Moves a mod below the others in its conflict, so its copy of the shared files is the one that
/// survives. True when the order actually changed.
/// </param>
/// <param name="Research">
/// Opens the research window for an issue, owned by the window that asked. Returns what the user
/// concluded, or null if they left it undecided.
///
/// The owner is passed in rather than taken from the app, because the report is itself a modal
/// child of the main window: parenting the research dialog to the main window instead would leave
/// the report clickable underneath it, allow two research dialogs at once, and let the report be
/// closed while a continuation was still waiting to redraw it.
/// </param>
/// <param name="InspectRebind">
/// Whether this mod's shortcut can be changed from here, and which blocker stands in the way when
/// it cannot.
/// </param>
/// <param name="RebindHotkey">
/// Moves this mod onto a free key. Returns the new key on success, or null when the user backed out
/// or nothing could be written.
/// </param>
public sealed record ConflictReportActions(
    Func<LoadOrderNote, IssueParticipant, bool> MakeModWin,
    Func<Window, LoadOrderNote, Task<IssueVerdict?>> Research,
    Func<LoadOrderNote, IssueParticipant, RebindCapability> InspectRebind,
    Func<LoadOrderNote, IssueParticipant, Task<string?>> RebindHotkey);
