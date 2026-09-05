using CommunityToolkit.Mvvm.Input;

namespace Garethp.ModsOfMistriaGUI.Models;

/// <summary>
/// One folder AIM watches for mods downloaded by hand, as listed in the gear menu.
///
/// The command travels with the entry rather than being reached through the page: a menu item's
/// DataContext is the item, not the view model that supplied the list, so an item template has no
/// path back to the page - the same reason <see cref="ModRowCommands"/> and
/// <see cref="ModBackupChoice"/> exist.
/// </summary>
public sealed record DropFolderEntry(string Path, IRelayCommand<string?> Unlink);
