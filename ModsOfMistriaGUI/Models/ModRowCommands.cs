using CommunityToolkit.Mvvm.Input;

namespace Garethp.ModsOfMistriaGUI.Models;

/// <summary>
/// The per-mod actions behind a row's right-click menu.
///
/// They are handed to each row rather than reached for through the visual tree because a flyout is
/// a popup: it inherits its DataContext from the row, but it is not a child of the list, so the
/// usual "walk up to the page view model" binding does not resolve inside it.
/// </summary>
public sealed record ModRowCommands(
    IRelayCommand<ModModel> OpenNexusPage,
    IRelayCommand<ModModel> TrackOnNexus,
    IRelayCommand<ModModel> AssociateWithNexus,
    IRelayCommand<ModModel> CheckForUpdate,
    IRelayCommand<ModModel> UpdateFromNexus,
    IRelayCommand<ModModel> ToggleFreeze,

    // Both act on the real load order, not on the filtered view, so "top" means the top of the
    // order rather than the top of whatever the current filter happens to show.
    IRelayCommand<ModModel> MoveToTop,
    IRelayCommand<ModModel> MoveToBottom,
    IRelayCommand<ModModel> RestorePreviousVersion,
    IRelayCommand<ModModel> OpenModFolder,
    IRelayCommand<ModModel> EditManifest,
    IRelayCommand<ModModel> EditConfig,
    IRelayCommand<ModModel> RemoveMod,
    IRelayCommand<ModModel> ShowChangelog,
    IRelayCommand<ModModel> LoadChangelogPreview);
