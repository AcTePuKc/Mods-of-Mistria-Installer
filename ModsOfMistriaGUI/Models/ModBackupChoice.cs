using CommunityToolkit.Mvvm.Input;
using Garethp.ModsOfMistriaInstallerLib.Nexus;

namespace Garethp.ModsOfMistriaGUI.Models;

/// <summary>
/// One archived copy of a mod, offered in the row's version dropdown.
///
/// The command travels with the choice rather than being reached through the row. A flyout's items
/// take their DataContext from the item, not from the row that opened it, so an item template has
/// no path back to the page view model - the same reason <see cref="ModRowCommands"/> exists.
/// </summary>
public sealed record ModBackupChoice(
    ModModel Mod,
    ModBackup Backup,
    IRelayCommand<ModBackupChoice> Restore)
{
    /// <summary>Version and date, e.g. "1.2.0 (14/03/2026 09:12)".</summary>
    public string Label => Backup.Describe();
}
