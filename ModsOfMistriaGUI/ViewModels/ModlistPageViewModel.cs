using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Garethp.ModsOfMistriaGUI.Models;
using Garethp.ModsOfMistriaGUI.Services;
using Garethp.ModsOfMistriaGUI.Views;
using Garethp.ModsOfMistriaInstallerLib;
using Garethp.ModsOfMistriaInstallerLib.Bindings;
using Garethp.ModsOfMistriaInstallerLib.Crash;
using Garethp.ModsOfMistriaInstallerLib.Generator;
using Garethp.ModsOfMistriaInstallerLib.GmlMods;
using Garethp.ModsOfMistriaInstallerLib.Lang;
using Garethp.ModsOfMistriaInstallerLib.ModTypes;
using Garethp.ModsOfMistriaInstallerLib.Nexus;
using Garethp.ModsOfMistriaInstallerLib.Research;
using Garethp.ModsOfMistriaInstallerLib.Store;
using Garethp.ModsOfMistriaInstallerLib.Worker;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using UpdateChecker = Garethp.ModsOfMistriaInstallerLib.UpdateChecker;

namespace Garethp.ModsOfMistriaGUI.ViewModels;

public partial class ModlistPageViewModel : PageViewBase
{
    private int _modlistLoadInProgress;
    private int _modlistReloadRequested;
    private DispatcherTimer? _installUiHeartbeat;
    private long _lastHeartbeatTimestamp;
    private readonly Settings _settings;
    private ProfileManager? _profileManager;
    private int _localizationRefreshVersion;
    private int _conflictRefreshVersion;
    private int _bulkSelectionChangeDepth;
    private LoadOrderResultWindow? _issueReportWindow;
    private IReadOnlyList<ModModel> _filteredMods = [];

    // True when in-GUI state differs from what is saved in the current profile
    private bool _isDirty;
    // Suppresses dirty-marking during programmatic enabled-state changes (profile apply)
    private bool _suppressDirty;
    // Prevents re-entrant cascades when auto-enabling/disabling dependents
    private bool _cascading;

    private string? _archiveStatusKey;
    private int _archiveStatusModCount;

    // Notices mods copied into the folder while AIM is open.
    private ModsFolderWatcher? _modsFolderWatcher;

    // Nexus update checking, bound to the current mods folder.
    private NexusUpdateService? _updateService;
    private ModBackupStore? _backupStore;
    private ModRowCommands? _rowCommands;

    public ModlistPageViewModel(Settings settings, NexusDownloadsViewModel? nexus = null)
    {
        _settings = settings;
        Nexus = nexus;

        // A mod that arrives from Nexus is a new folder in the mods directory, so the list has to
        // be rebuilt before it can be selected and installed.
        if (Nexus is not null)
            Nexus.ModsChanged += (_, _) => Dispatcher.UIThread.Post(() => UpdateModlist(true));

        SetLanguageCommand = new RelayCommand<string?>(SetLanguage);
        Localization.LanguageChanged += OnLocalizationChanged;
        _settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Settings.LaunchGameDirectly))
                OnPropertyChanged(nameof(LaunchGameDirectly));

            // A language or preference change must not rediscover every mod.
            // Rebuilding the list opens archives and can make the window look
            // unresponsive, while only a different game/mods folder actually
            // changes the list's source data.
            if (e.PropertyName is nameof(Settings.MistriaLocation) or nameof(Settings.ModsLocation))
                Task.Run(UpdateModlist);
        };
        Task.Run(UpdateModlist);
    }

    /// <summary>Null in design-time and test contexts that build the page on its own.</summary>
    public NexusDownloadsViewModel? Nexus { get; }

    public IRelayCommand<string?> SetLanguageCommand { get; }

    private void SetLanguage(string? languageCode)
    {
        _settings.UiLanguage = string.IsNullOrWhiteSpace(languageCode) ? "system" : languageCode;
        Localization.SetLanguage(_settings.UiLanguage);
    }

    private void OnLocalizationChanged(object? sender, EventArgs e)
    {
        RefreshLocalizedText();
    }

    private void RefreshLocalizedText()
    {
        var stopwatch = Stopwatch.StartNew();
        var refreshVersion = Interlocked.Increment(ref _localizationRefreshVersion);
        var modsNeedingValidation = Mods.Where(model => model.NeedsLocalizedValidation).ToList();

        GreetingText = isAprilFools ? Resources.GUIGreetingText_April : Resources.GUIGreetingText;
        InstallButtonText = isAprilFools ? Resources.GUIInstallButtonText_April : Resources.GUIInstallButtonText;
        InstallInProgressText = isAprilFools ? Resources.GUIInstallInProgress_April : Resources.GUIInstallInProgress;
        NoModsToInstallText = isAprilFools ? Resources.GUINoModsToInstall_April : Resources.GUINoModsToInstall;
        ModsWillBeInstalledText = isAprilFools ? Resources.GUIModsWillBeInstalled_April : Resources.GUIModsWillBeInstalled;
        RefreshCachedArchiveStatusText();
        var modelRefreshStopwatch = Stopwatch.StartNew();
        foreach (var model in Mods)
            model.RefreshLocalizedText();
        RefreshFilteredMods();
        PerformanceDiagnostics.Log($"Language refresh: mod row notifications={modelRefreshStopwatch.ElapsedMilliseconds} ms, mods={Mods.Count}");

        // Conflict and compatibility strings are localized too, but their
        // source scans remain off the UI thread.
        RefreshSelectedModConflicts();

        var uiRefreshMilliseconds = stopwatch.ElapsedMilliseconds;
        if (modsNeedingValidation.Count == 0)
        {
            PerformanceDiagnostics.Log($"Language refresh: UI={uiRefreshMilliseconds} ms, validation=0 ms, mods={Mods.Count}");
            return;
        }

        _ = Task.Run(() =>
        {
            var validationStopwatch = Stopwatch.StartNew();
            foreach (var model in modsNeedingValidation)
                model.RevalidateForLocalization();

            var validationMilliseconds = validationStopwatch.ElapsedMilliseconds;
            Dispatcher.UIThread.Post(() =>
            {
                if (refreshVersion != Volatile.Read(ref _localizationRefreshVersion)) return;

                foreach (var model in modsNeedingValidation)
                    model.RefreshValidation();

                PerformanceDiagnostics.Log($"Language refresh: UI={uiRefreshMilliseconds} ms, validation={validationMilliseconds} ms, mods={Mods.Count}, revalidated={modsNeedingValidation.Count}");
            });
        });
    }

    private void RefreshCachedArchiveStatusText()
    {
        if (_archiveStatusKey is null) return;
        ArchiveStatus = _archiveStatusKey == "GUIArchiveMatch"
            ? string.Format(Localized(_archiveStatusKey), _archiveStatusModCount)
            : Localized(_archiveStatusKey);
    }

    public bool LaunchGameDirectly
    {
        get => _settings.LaunchGameDirectly;
        set => _settings.LaunchGameDirectly = value;
    }

    // ── Profile management ────────────────────────────────────────────────────────

    public ObservableCollection<string> Profiles { get; } = [];

    // Starts empty so the first profile refresh raises a notification even when
    // the clean-folder fallback profile is Default; otherwise the ComboBox can
    // contain Default while rendering no selected item.
    [ObservableProperty] private string _currentProfile = "";

    [RelayCommand]
    private async Task SwitchProfile(string profileName)
    {
        if (profileName == CurrentProfile) return;

        if (_isDirty && _profileManager is not null)
        {
            var box = MessageBoxManager.GetMessageBoxStandard(
                Texts.GUIConfirmSaveProfileTitle,
                string.Format(Texts.GUIConfirmSaveProfileMessage, CurrentProfile),
                ButtonEnum.YesNoCancel);
            var result = await box.ShowAsync();

            if (result == ButtonResult.Cancel) return;
            if (result == ButtonResult.Yes)
                SaveCurrentProfileState();
        }

        _profileManager?.SwitchProfile(profileName);
        CurrentProfile = profileName;
        ApplyProfileToMods();    // sets _isDirty = false internally
    }

    [RelayCommand]
    private async Task CreateProfile()
    {
        var name = string.Format(Texts.GUIProfileName, Profiles.Count + 1);
        _profileManager?.CreateProfile(name);
        _profileManager?.SwitchProfile(name);
        CurrentProfile = name;
        RefreshProfileList();
        ApplyProfileToMods();
    }

    [RelayCommand]
    private async Task DeleteCurrentProfile()
    {
        if (CurrentProfile == "Default") return;

        var box = MessageBoxManager.GetMessageBoxStandard(
            Texts.GUIConfirmDeleteProfileTitle,
            string.Format(Texts.GUIConfirmDeleteProfileMessage, CurrentProfile),
            ButtonEnum.YesNo);
        var result = await box.ShowAsync();
        if (result != ButtonResult.Yes) return;

        _profileManager?.DeleteProfile(CurrentProfile);
        CurrentProfile = "Default";
        RefreshProfileList();
        ApplyProfileToMods();
    }

    public void SaveCurrentProfileState()
    {
        if (_profileManager is null) return;
        var enabled   = Mods.Where(m => m.Enabled).Select(m => m.Mod.GetId()).ToList();
        var enabledSources = Mods.Where(m => m.Enabled)
            .Select(m => DuplicateModDetector.NormalizeSource(m.Mod.GetSourcePath())).ToList();
        var loadOrder = Mods.Select(m => m.Mod.GetId()).ToList();
        var loadOrderSources = Mods
            .Select(m => DuplicateModDetector.NormalizeSource(m.Mod.GetSourcePath())).ToList();
        _profileManager.SaveCurrentProfile(enabled, loadOrder, enabledSources, loadOrderSources);
        _isDirty = false;
    }

    private void RefreshProfileList()
    {
        var names = _profileManager?.GetProfileNames() ?? ["Default"];
        var active = _profileManager?.CurrentProfileName ?? "Default";
        var selected = names.Contains(active) ? active : "Default";

        // Clearing the collection clears ComboBox.SelectedItem. Resetting the
        // backing value first is important when the same profile remains
        // active (especially Default), otherwise no property notification is
        // raised and the ComboBox stays visually unselected.
        CurrentProfile = "";
        Profiles.Clear();
        foreach (var n in names) Profiles.Add(n);
        CurrentProfile = selected;
    }

    private void ApplyProfileToMods()
    {
        if (_profileManager is null || Mods.Count == 0) return;

        _suppressDirty = true;
        try
        {
            var (enabledIds, loadOrder) = _profileManager.GetCurrentProfile();
            var loadOrderSources = _profileManager.GetCurrentProfileLoadOrderSources();

            // If profile has never been saved (both empty), default to all enabled
            var allMods    = Mods.Select(m => m.Mod).ToList();
            var duplicateCopies = BuildDuplicateCopyMap(allMods);
            var enabledSources = _profileManager.GetCurrentProfileEnabledSources()
                .Select(DuplicateModDetector.NormalizeSource)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var freshProfile = enabledIds.Count == 0 && loadOrder.Count == 0;
            var enabledSet = freshProfile
                ? allMods.Select(m => m.GetId()).ToHashSet(StringComparer.OrdinalIgnoreCase)
                : enabledIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (enabledSources.Count == 0)
                enabledSources = DefaultDuplicateSources(allMods, duplicateCopies, enabledSet, freshProfile);

            var sorted = ProfileManager.SortByLoadOrder(allMods, loadOrder, loadOrderSources);

            var newModels = sorted.Select((mod, idx) =>
            {
                // Match the physical mod object, not only its logical ID. Two
                // copies can intentionally share the same author/name ID.
                var model = Mods.FirstOrDefault(m => ReferenceEquals(m.Mod, mod))
                            ?? new ModModel(mod);
                if (duplicateCopies.TryGetValue(DuplicateModDetector.NormalizeSource(mod.GetSourcePath()), out var copies))
                    model.SetDuplicateCopies(copies);
                model.Enabled = IsProfileSelected(mod, enabledSet, enabledSources, duplicateCopies);
                model.Position = idx + 1;
                return model;
            }).ToList();

            Mods.Clear();
            foreach (var m in newModels) Mods.Add(m);
            RefreshFilteredMods();
        }
        finally
        {
            _suppressDirty = false;
            _isDirty = false;
            RefreshArchiveStatus();
            InstallModsCommand.NotifyCanExecuteChanged();
        }
    }

    // ── Load order ────────────────────────────────────────────────────────────────

    public void MoveMod(ModModel draggedMod, ModModel targetMod, bool insertBeforeTarget)
    {
        var sourceIndex = Mods.IndexOf(draggedMod);
        var targetIndex = Mods.IndexOf(targetMod);
        if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex) return;

        // ObservableCollection.Move expects the final index after the source item has
        // been removed, so a downward move needs to account for that removal.
        var destinationIndex = insertBeforeTarget ? targetIndex : targetIndex + 1;
        if (sourceIndex < destinationIndex) destinationIndex--;
        if (sourceIndex == destinationIndex) return;

        // ItemsRepeater can retain a stale visual container after an
        // ObservableCollection.Move notification, which briefly renders the
        // moved row twice. Remove and insert emit unambiguous container
        // lifecycle events while preserving the same model instance/state.
        Mods.RemoveAt(sourceIndex);
        Mods.Insert(destinationIndex, draggedMod);
        RefreshPositions();
        RefreshFilteredMods();
        _isDirty = true;
    }

    /// <summary>
    /// Sends a mod to one end of the load order.
    ///
    /// The two ends are where the useful answers live - a framework everything else needs goes to
    /// the top, and the recolour that must beat every other recolour goes to the bottom - and on a
    /// list of two hundred mods, dragging a row that far means holding the mouse against the edge
    /// of a scrolling list for half a minute and usually missing.
    ///
    /// It works on the real order rather than the filtered view, so "top" means the top of the load
    /// order and not the top of whatever happens to be on screen. That is also why it is available
    /// while a filter is on, when dragging is not.
    /// </summary>
    private void MoveModToTop(ModModel? model) => MoveModToEnd(model, top: true);

    private void MoveModToBottom(ModModel? model) => MoveModToEnd(model, top: false);

    private void MoveModToEnd(ModModel? model, bool top)
    {
        // Not while an install is running: it is building the archive from this exact order.
        if (model is null || IsInstalling || Mods.Count < 2) return;

        var from = Mods.IndexOf(model);
        var to = top ? 0 : Mods.Count - 1;
        if (from < 0 || from == to) return;

        // Remove and insert rather than Move, for the reason MoveMod gives: ItemsRepeater can hold
        // on to a stale container after a Move notification and render the row twice.
        Mods.RemoveAt(from);
        Mods.Insert(to, model);

        RefreshPositions();
        RefreshFilteredMods();
        _isDirty = true;
    }

    /// <summary>
    /// Points the folder watcher at the current mods folder. Reloading is skipped while an install
    /// is running: that writes to the game archive, and a mod list rebuilding underneath it would
    /// change the selection the install is working from.
    /// </summary>
    private void WatchModsFolder()
    {
        _modsFolderWatcher?.Dispose();
        _modsFolderWatcher = null;

        if (string.IsNullOrEmpty(ModsLocation) || !Directory.Exists(ModsLocation)) return;

        var watcher = new ModsFolderWatcher(ModsLocation, () =>
            Dispatcher.UIThread.Post(() =>
            {
                if (IsInstalling) return;
                Logger.Log("The mods folder changed; reloading the mod list.");
                UpdateModlist(true);
            }));

        if (watcher.Start()) _modsFolderWatcher = watcher;

        WatchDropFolders();
    }

    // ── Watched download folders ─────────────────────────────────────────────────

    private readonly List<ModsFolderWatcher> _dropFolderWatchers = [];

    /// <summary>
    /// The folders AIM watches for mods downloaded by hand, for the gear menu to list.
    /// </summary>
    public ObservableCollection<DropFolderEntry> DropFolders { get; } = [];

    public bool HasDropFolders => DropFolders.Count > 0;

    /// <summary>
    /// Watches each linked folder for mods arriving in it.
    ///
    /// The same watcher the mods folder uses, so a burst of file events from a download settles into
    /// one import rather than a dozen attempts at a half-written archive. A sweep runs immediately
    /// as well: AIM is usually not running while the browser downloads, so most of what it finds was
    /// put there before it started.
    /// </summary>
    private void WatchDropFolders()
    {
        foreach (var watcher in _dropFolderWatchers) watcher.Dispose();
        _dropFolderWatchers.Clear();

        DropFolders.Clear();
        foreach (var folder in _settings.DropFolders)
            DropFolders.Add(new DropFolderEntry(folder, UnlinkDropFolderCommand));
        OnPropertyChanged(nameof(HasDropFolders));

        if (string.IsNullOrEmpty(ModsLocation) || DropFolders.Count == 0) return;

        foreach (var folder in DropFolders.Select(entry => entry.Path).Where(Directory.Exists))
        {
            var watcher = new ModsFolderWatcher(folder,
                () => Dispatcher.UIThread.Post(() => ImportDroppedMods(announce: false)),
                TimeSpan.FromSeconds(4));

            if (watcher.Start()) _dropFolderWatchers.Add(watcher);
        }

        ImportDroppedMods(announce: false);
    }

    /// <summary>
    /// Moves anything mod-shaped out of the watched folders and into the mods folder.
    /// </summary>
    /// <param name="announce">
    /// Whether to say so when nothing was found. The automatic sweeps stay silent - a watcher that
    /// reported "nothing to import" every time a browser touched the downloads folder would be
    /// unusable - but a user who clicked the menu item is owed an answer either way.
    /// </param>
    private void ImportDroppedMods(bool announce)
    {
        if (IsInstalling || string.IsNullOrEmpty(ModsLocation) || DropFolders.Count == 0) return;

        var folders = DropFolders.Select(entry => entry.Path).ToList();
        var modsLocation = ModsLocation;

        _ = Task.Run(() =>
        {
            List<ImportedMod> imported;
            try
            {
                imported = ModDropFolders.Import(folders, modsLocation);
            }
            catch (Exception exception)
            {
                Logger.Log($"Importing from the watched folders failed: {exception.Message}");
                return;
            }

            Dispatcher.UIThread.Post(async () =>
            {
                if (imported.Count > 0)
                {
                    InstallStatus = string.Format(Texts.GUIDropFolderImported, imported.Count);
                    // The mods folder watcher would get here on its own a couple of seconds later,
                    // but a list that updates the instant the file lands is what makes the feature
                    // feel like it worked.
                    UpdateModlist(true);
                }

                if (!announce) return;

                await MessageBoxManager.GetMessageBoxStandard(
                    Texts.GUIDropFolderTitle,
                    imported.Count == 0
                        ? Texts.GUIDropFolderNothingFound
                        : string.Format(Texts.GUIDropFolderImportedDetail,
                            string.Join("\r\n", imported.Select(mod => $"• {mod.Name}"))),
                    ButtonEnum.Ok).ShowAsync();
            });
        });
    }

    /// <summary>Adds a folder to watch - the browser's downloads folder, usually.</summary>
    [RelayCommand]
    private async Task LinkDropFolder()
    {
        if (App.TopLevel is not { } topLevel) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Texts.GUIDropFolderPickTitle,
            AllowMultiple = false
        });

        if (folders.Count != 1 || folders[0].TryGetLocalPath() is not { } path) return;

        _settings.SetDropFolders(_settings.DropFolders.Append(Path.GetFullPath(path)));
        WatchDropFolders();
    }

    [RelayCommand]
    private void UnlinkDropFolder(string? folder)
    {
        if (string.IsNullOrEmpty(folder)) return;

        _settings.SetDropFolders(_settings.DropFolders
            .Where(existing => !existing.Equals(folder, StringComparison.OrdinalIgnoreCase)));
        WatchDropFolders();
    }

    [RelayCommand]
    private void ImportDropFoldersNow() => ImportDroppedMods(announce: true);

    /// <summary>Opens the mods folder itself, rather than one mod's folder.</summary>
    [RelayCommand]
    private void OpenModsFolder()
    {
        if (string.IsNullOrEmpty(ModsLocation) || !Directory.Exists(ModsLocation))
        {
            Exception = Texts.GUIDropFolderNoModsFolder;
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = ModsLocation, UseShellExecute = true });
        }
        catch (Exception exception)
        {
            Logger.Log($"Could not open {ModsLocation}: {exception.Message}");
            Exception = string.Format(Texts.GUIDropFolderCannotOpen, exception.Message);
        }
    }

    private NexusUpdateService? UpdateService
    {
        get
        {
            if (Nexus is null || string.IsNullOrEmpty(ModsLocation)) return null;
            return _updateService ??= Nexus.CreateUpdateService(ModsLocation);
        }
    }

    private ModBackupStore? BackupStore
    {
        get
        {
            if (string.IsNullOrEmpty(ModsLocation)) return null;
            return _backupStore ??= new ModBackupStore(ModsLocation);
        }
    }

    private ModRowCommands RowCommands => _rowCommands ??= new ModRowCommands(
        new RelayCommand<ModModel>(OpenNexusPage),
        new AsyncRelayCommand<ModModel>(TrackOnNexus),
        new AsyncRelayCommand<ModModel>(AssociateWithNexus),
        new AsyncRelayCommand<ModModel>(CheckModForUpdate),
        new AsyncRelayCommand<ModModel>(UpdateModFromNexus),
        new RelayCommand<ModModel>(ToggleModFreeze),
        new RelayCommand<ModModel>(MoveModToTop),
        new RelayCommand<ModModel>(MoveModToBottom),
        new AsyncRelayCommand<ModModel>(RestorePreviousVersion),
        new RelayCommand<ModModel>(OpenModFolder),
        new RelayCommand<ModModel>(EditModManifest),
        new RelayCommand<ModModel>(EditModConfig),
        new AsyncRelayCommand<ModModel>(RemoveMod),
        new AsyncRelayCommand<ModModel>(ShowChangelog),
        new AsyncRelayCommand<ModModel>(LoadChangelogPreview));

    /// <summary>
    /// Fills in what AIM knows about each row from Nexus: which page it came from, whether the user
    /// froze it, and whether a previous version is kept. No network calls - this is all local
    /// bookkeeping, so it runs on every list load.
    /// </summary>
    private void RefreshNexusState()
    {
        var service = UpdateService;
        var backups = BackupStore;

        foreach (var model in Mods)
        {
            model.Commands = RowCommands;

            var record = service?.Resolve(model.Mod);
            model.NexusPageUrl = record?.PageUrl;
            model.IsFrozen = service?.IsFrozen(model.Mod) ?? false;

            // Read on every list load for the same reason the edit marker is: it has to survive a
            // restart, which is exactly when the user has forgotten why one of their mods is being
            // held back.
            model.FreezeReason = service?.FreezeReason(model.Mod) ?? "";
            model.UpdateMayFixEdit = false;
            model.UpdatedAt = LastChangedOnDisk(model.Mod.GetSourcePath());

            // Only mods AIM can identify on Nexus have release notes to fetch. The preview resets
            // with the list so an updated mod re-reads its notes rather than showing the old ones.
            model.HasChangelogSource = record is not null;
            model.ChangelogRequested = false;
            model.ChangelogPreview = Texts.GUIChangelogLoading;

            var archived = backups?.List(ModBackupStore.ModNameFor(model.Mod.GetSourcePath())) ?? [];
            model.SetBackups(archived.Select(backup =>
                new ModBackupChoice(model, backup, RestoreChosenVersionCommand)));

            // Whether AIM has changed anything inside this mod. Read on every list load rather than
            // only after an edit: the marker has to survive a restart, which is precisely when
            // somebody has forgotten the edit was ever made.
            var edits = EditStore;
            model.WasEditedByAim = edits?.WasEdited(model.Mod.GetId()) ?? false;
            model.AimEditSummary = edits?.DescribeEdits(model.Mod.GetId()) ?? "";
        }

        RefreshEditableFiles();
    }

    /// <summary>
    /// Works out which of the "edit this file" menu items each row should offer.
    ///
    /// A mod with no recognisably named config costs a directory listing to rule out, and there is
    /// one of those per mod on every list load. That is small but it is not free, and this runs
    /// inside the dispatcher pass that applies the list - so the scan happens off the UI thread and
    /// the rows are filled in when it comes back. Until then both items are simply greyed out.
    /// </summary>
    private void RefreshEditableFiles()
    {
        var rows = Mods.ToList();
        if (rows.Count == 0) return;

        Task.Run(() =>
        {
            var found = rows
                .Select(model => (
                    Model: model,
                    Manifest: ModEditableFiles.FindManifest(model.Mod) is not null,
                    Config: ModEditableFiles.FindConfig(model.Mod) is not null))
                .ToList();

            Dispatcher.UIThread.Post(() =>
            {
                foreach (var (model, manifest, config) in found)
                {
                    model.HasEditableManifest = manifest;
                    model.HasEditableConfig = config;
                }
            });
        });
    }

    /// <summary>
    /// When a mod last changed on disk.
    ///
    /// A folder's own timestamp moves when it is replaced wholesale, which is what an update does,
    /// so it does not need to walk the contents. A mod that has vanished sorts to the bottom rather
    /// than throwing.
    /// </summary>
    private static DateTimeOffset LastChangedOnDisk(string? path)
    {
        if (string.IsNullOrEmpty(path)) return DateTimeOffset.MinValue;

        try
        {
            if (Directory.Exists(path)) return new DateTimeOffset(Directory.GetLastWriteTimeUtc(path), TimeSpan.Zero);
            if (File.Exists(path)) return new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
        }
        catch (Exception exception)
        {
            Logger.Log($"Could not read when {path} last changed: {exception.Message}");
        }

        return DateTimeOffset.MinValue;
    }

    private void RefreshPositions()
    {
        for (var i = 0; i < Mods.Count; i++)
            Mods[i].Position = i + 1;
    }

    // ── Selection summary ─────────────────────────────────────────────────────────

    /// <summary>
    /// Reads "how much of this list is selected" for the header checkbox: true for all, false for
    /// none, null for a mix, which is what makes it show the indeterminate mark.
    /// </summary>
    public bool? AllModsSelected
    {
        get
        {
            // Invalid mods cannot be enabled (ModModel.Enabled deliberately
            // returns false for them), so they must not prevent the header
            // checkbox from reaching its checked state for every selectable mod.
            var selectable = Mods.Where(mod => !mod.InError).ToList();
            if (selectable.Count == 0) return false;
            var selected = selectable.Count(mod => mod.Enabled);
            if (selected == 0) return false;
            return selected == selectable.Count ? true : null;
        }
    }

    /// <summary>
    /// A ticked mod is a mod that will be in the game after the next install, not one queued to be
    /// added - installing rebuilds the archive from the ticked set. The counts spell that out, so
    /// "already installed" mods can be recognised at a glance without unticking them.
    /// </summary>
    [ObservableProperty] private string _selectionSummary = "";

    private void RefreshSelectionSummary()
    {
        var total = Mods.Count;
        var selected = Mods.Count(mod => mod.Enabled);

        // Counted from the game archive rather than from this session's install outcomes. Reading
        // the outcomes meant a mod installed a minute ago still showed as "will be added", and the
        // count only corrected itself when AIM was restarted.
        var installed = Mods.Count(mod => mod.IsInGameArchive);
        var pending = Mods.Count(mod => mod.IsPendingInstall);

        SelectionSummary = total == 0
            ? ""
            : string.Format(Texts.GUIModSelectionSummary, selected, total, installed, pending);

        OnPropertyChanged(nameof(AllModsSelected));
    }

    // When a mod is enabled, walk its requirements and enable them transitively.
    // Returns requirements that could not be found in the current mod list.
    // _cascading is already true when this is called, so their PropertyChanged won't re-enter.
    private List<ModRequirement> EnableDependenciesOf(ModModel mod)
    {
        var missing = new List<ModRequirement>();
        foreach (var req in mod.Mod.GetRequirements())
        {
            var dep = Mods.FirstOrDefault(m => m.Mod.GetId() == req.GetId());
            if (dep is null)
            {
                missing.Add(req);
                continue;
            }
            if (dep.Enabled) continue;
            dep.Enabled = true;
            missing.AddRange(EnableDependenciesOf(dep));
        }
        return missing;
    }

    // When a mod is disabled, find every enabled mod that (directly or transitively)
    // requires it and disable those too.
    private void DisableDependentsOf(ModModel mod)
    {
        var modId = mod.Mod.GetId();
        foreach (var other in Mods.ToList())
        {
            if (!other.Enabled) continue;
            if (!other.Mod.GetRequirements().Any(r => r.GetId() == modId)) continue;
            other.Enabled = false;
            DisableDependentsOf(other);
        }
    }

    // ── Mod list loading ──────────────────────────────────────────────────────────

    private static Dictionary<string, IReadOnlyList<IMod>> BuildDuplicateCopyMap(IEnumerable<IMod> mods)
    {
        var map = new Dictionary<string, IReadOnlyList<IMod>>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in DuplicateModDetector.Find(mods))
        {
            foreach (var copy in group.Copies)
                map[DuplicateModDetector.NormalizeSource(copy.GetSourcePath())] = group.Copies;
        }
        return map;
    }

    private static HashSet<string> DefaultDuplicateSources(
        IEnumerable<IMod> mods,
        Dictionary<string, IReadOnlyList<IMod>> duplicateCopies,
        HashSet<string> enabledIds,
        bool freshProfile)
    {
        var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in mods)
        {
            var source = DuplicateModDetector.NormalizeSource(mod.GetSourcePath());
            if (!duplicateCopies.TryGetValue(source, out var copies) ||
                (ReferenceEquals(copies[0], mod) && (freshProfile || enabledIds.Contains(mod.GetId()))))
                sources.Add(source);
        }
        return sources;
    }

    private static bool IsProfileSelected(
        IMod mod,
        HashSet<string> enabledIds,
        HashSet<string> enabledSources,
        Dictionary<string, IReadOnlyList<IMod>> duplicateCopies)
    {
        var source = DuplicateModDetector.NormalizeSource(mod.GetSourcePath());
        if (duplicateCopies.ContainsKey(source))
            return enabledSources.Contains(source);
        return enabledIds.Contains(mod.GetId());
    }

    private void UpdateModlist() => _ = UpdateModlistAsync(false);

    private void UpdateModlist(bool force) => _ = UpdateModlistAsync(force);

    private async Task UpdateModlistAsync(bool force)
    {
        if (Interlocked.Exchange(ref _modlistLoadInProgress, 1) != 0)
        {
            if (force) Volatile.Write(ref _modlistReloadRequested, 1);
            return;
        }

        try
        {
            var snapshot = await Dispatcher.UIThread.InvokeAsync(() =>
                new ModlistSnapshot(_settings.MistriaLocation, _settings.ModsLocation, MistriaLocation, ModsLocation));

            if (!force && snapshot.MistriaLocation == snapshot.CurrentMistriaLocation &&
                snapshot.ModsLocation == snapshot.CurrentModsLocation)
                return;

            var result = await Task.Run(() => LoadModlist(snapshot.MistriaLocation, snapshot.ModsLocation));
            await Dispatcher.UIThread.InvokeAsync(() => ApplyModlist(result));
        }
        finally
        {
            Volatile.Write(ref _modlistLoadInProgress, 0);
            if (Interlocked.Exchange(ref _modlistReloadRequested, 0) != 0)
                _ = UpdateModlistAsync(true);
        }
    }

    private ModlistLoadResult LoadModlist(string mistriaLocation, string modsLocation)
    {
        var totalStopwatch = Stopwatch.StartNew();
        ProfileManager? profileManager = null;
        List<IMod> rawMods = [];
        var discoveryMilliseconds = 0L;
        var validationMilliseconds = 0L;
        var duplicateCopies = new Dictionary<string, IReadOnlyList<IMod>>(StringComparer.OrdinalIgnoreCase);
        List<IMod> orderedMods = [];
        HashSet<string> enabledIds = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> enabledSources = new(StringComparer.OrdinalIgnoreCase);

        if (Directory.Exists(modsLocation))
        {
            try { profileManager = new ProfileManager(modsLocation); }
            catch { profileManager = null; }

            var discoveryStopwatch = Stopwatch.StartNew();
            rawMods = MistriaLocator.GetMods(mistriaLocation, modsLocation);
            discoveryMilliseconds = discoveryStopwatch.ElapsedMilliseconds;

            var validationStopwatch = Stopwatch.StartNew();
            ModInstaller.ValidateMods(rawMods);
            validationMilliseconds = validationStopwatch.ElapsedMilliseconds;
            duplicateCopies = BuildDuplicateCopyMap(rawMods);

            if (profileManager is not null)
            {
                var (profileEnabledIds, loadOrder) = profileManager.GetCurrentProfile();
                var loadOrderSources = profileManager.GetCurrentProfileLoadOrderSources();
                var resolvedEnabled = profileEnabledIds.Count == 0 && loadOrder.Count == 0
                    ? rawMods.Select(m => m.GetId()).ToList()
                    : ProfileManager.ResolveEnabledWithDeps(rawMods, profileEnabledIds);
                enabledIds = resolvedEnabled.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var configuredSources = profileManager.GetCurrentProfileEnabledSources()
                    .Select(DuplicateModDetector.NormalizeSource)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                enabledSources = configuredSources.Count > 0
                    ? configuredSources
                    : DefaultDuplicateSources(rawMods, duplicateCopies, enabledIds, false);
                orderedMods = ProfileManager.SortByLoadOrder(rawMods, loadOrder, loadOrderSources);
            }
            else
            {
                var allDisabled = rawMods.All(m => !m.IsInstalled());
                if (allDisabled) rawMods.ForEach(m => m.SetInstalled(true));
                orderedMods = rawMods;
            }
        }

        PerformanceDiagnostics.Log($"Modlist load: total={totalStopwatch.ElapsedMilliseconds} ms, discovery={discoveryMilliseconds} ms, validation={validationMilliseconds} ms, mods={orderedMods.Count}");
        return new ModlistLoadResult(mistriaLocation, modsLocation, profileManager, orderedMods, enabledIds, enabledSources, duplicateCopies);
    }

    private void ApplyModlist(ModlistLoadResult result)
    {
        {
            MistriaLocation = result.MistriaLocation;
            ModsLocation = result.ModsLocation;
            _profileManager = result.ProfileManager;
            Mods.Clear();

            for (var i = 0; i < result.OrderedMods.Count; i++)
            {
                var mod = result.OrderedMods[i];
                var model = new ModModel(mod)
                {
                    Enabled = IsProfileSelected(mod, result.EnabledIds, result.EnabledSources, result.DuplicateCopies),
                    Position = i + 1,
                    ContextActionsLocked = IsInstalling
                };
                if (result.DuplicateCopies.TryGetValue(
                        DuplicateModDetector.NormalizeSource(mod.GetSourcePath()), out var copies))
                    model.SetDuplicateCopies(copies);
                AttachModPropertyHandlers(model);
                Mods.Add(model);
            }

            RefreshFilteredMods();

            RefreshProfileList();
            _isDirty = false;
            _updateService = null;
            _backupStore = null;
            _bindingVault = null;
            _changelogStore = null;
            _crashTrials = null;
            RefreshNexusState();
            RefreshPendingUpdates();
            RefreshSelectionSummary();
            WatchModsFolder();
            var modSnapshot = Mods.ToList();
            _ = Task.Run(() => CheckModUpdatesAsync(modSnapshot));

            InstallStatus = "";
            RefreshGameReady();
            InstallModsCommand.NotifyCanExecuteChanged();
            UnInstallModsCommand.NotifyCanExecuteChanged();
            if (MistriaLocation.Equals("")) InstallStatus = Resources.GUICouldNotFindMistria;
            else if (ModsLocation.Equals("")) InstallStatus = Resources.GUICouldNotFindMods;
            else if (Mods.Count == 0) InstallStatus = NoModsToInstallText;
            RefreshSelectedModConflicts();

            // The rows are new objects, so the marks earned in the crash window have to be put back
            // on them from the verdicts on disk. Otherwise a mod caught crashing the game loses its
            // mark at the next reload - which happens whenever the mods folder changes, which is
            // precisely when the user is deciding what to tick.
            RefreshCrashMarks();

            // A mod that reset one of the user's keybinds did so the last time the game ran, so the
            // list load is the first moment AIM can notice and offer to put it back.
            _ = OfferBindingRestore();
        }
    }

    private void AttachModPropertyHandlers(ModModel model)
    {
        model.PropertyChanged += async (sender, e) =>
        {
            if (e.PropertyName != nameof(ModModel.Enabled) || _suppressDirty) return;
            // Select/clear-all changes a whole collection synchronously. Defer
            // the expensive archive and conflict work until the final checkbox
            // has changed, otherwise 40 mods cause 40 whole-list scans.
            if (_bulkSelectionChangeDepth > 0) return;
            _isDirty = true;
            RefreshSelectionSummary();
            RefreshArchiveStatus();
            RefreshSelectedModConflicts();

            // The enabled-only filter is a view of exactly this property, so a row unticked while
            // it is on has to leave the list. Only when that filter is actually on.
            if (ShowOnlyEnabled) RefreshVisibleMods();

            if (_cascading) return;
            _cascading = true;
            List<ModRequirement> missing;
            try
            {
                var changed = (ModModel)sender!;
                if (changed.Enabled) missing = EnableDependenciesOf(changed);
                else
                {
                    DisableDependentsOf(changed);
                    missing = [];
                }
            }
            finally { _cascading = false; }
            InstallModsCommand.NotifyCanExecuteChanged();
            UnInstallModsCommand.NotifyCanExecuteChanged();

            if (missing.Count == 0) return;

            var lines = string.Join("\n\n", missing.Select(r =>
            {
                var line = $"• \"{r.Name}\" by {r.Author}";
                if (!string.IsNullOrEmpty(r.DownloadUrl)) line += $"\n  {r.DownloadUrl}";
                return line;
            }));
            var urls = missing.Where(r => !string.IsNullOrEmpty(r.DownloadUrl))
                .Select(r => r.DownloadUrl!).ToList();

            if (urls.Count > 0)
            {
                var ask = await MessageBoxManager.GetMessageBoxStandard(
                    Texts.GUIMissingRequirementsTitle,
                    string.Format(Texts.GUIMissingRequirementsMessage, lines),
                    ButtonEnum.YesNo).ShowAsync();
                if (ask == ButtonResult.Yes)
                {
                    var urlList = string.Join("\n", urls.Select(u => $"• {u}"));
                    var confirm = await MessageBoxManager.GetMessageBoxStandard(
                        Texts.GUIOpenExternalLinksTitle,
                        string.Format(Texts.GUIOpenExternalLinksMessage, urlList),
                        ButtonEnum.YesNo).ShowAsync();
                    if (confirm == ButtonResult.Yes)
                        foreach (var url in urls.Where(ExternalUrl.IsAllowed))
                            System.Diagnostics.Process.Start(new ProcessStartInfo
                            {
                                FileName = url,
                                UseShellExecute = true
                            });
                }
            }
            else
            {
                await MessageBoxManager.GetMessageBoxStandard(
                    Texts.GUIMissingRequirementsTitle,
                    string.Format(Texts.GUIMissingRequirementsManual, lines),
                    ButtonEnum.Ok).ShowAsync();
            }
        };
    }

    // This runs after loading, selection changes, and archive operations. The
    // file scan is off the UI thread and never touches the game archive.
    private void RefreshSelectedModConflicts()
    {
        var refreshVersion = Interlocked.Increment(ref _conflictRefreshVersion);
        var models = Mods.ToList();
        var selectedModels = models.Where(m => m.Enabled).ToList();
        var selected = selectedModels.Select(m => m.Mod).ToList();

        // Compatibility checks run for every discovered mod when the list is
        // loaded and are refreshed after selection changes. They are cheap
        // metadata/code-signature checks and do not touch the game archive.
        _ = Task.Run(() =>
        {
            var dismissed = LoadDismissedIssues();
            var detected = new Dictionary<ModModel, IReadOnlyList<string>>();
            var cosmeticIssues = new Dictionary<ModModel, IReadOnlyList<string>>();
            var settledLegacy = new HashSet<ModModel>();
            var settledCosmetic = new HashSet<ModModel>();
            var looseGml = new HashSet<ModModel>();
            var settledInline = new Dictionary<ModModel, IReadOnlyCollection<string>>();
            foreach (var model in models)
            {
                // Read on the same pass and against the same store as everything else here, so a
                // tick in the report reaches the row's own warnings too.
                try { settledInline[model] = SettledInlineWarningsFor(model.Mod, dismissed); }
                catch (Exception exception)
                {
                    Logger.Log($"Could not read settled warnings for {model.Mod.GetId()}: {exception.Message}");
                    settledInline[model] = [];
                }

                try
                {
                    detected[model] = LegacyGameCompatibilityDetector.Find(model.Mod);
                    cosmeticIssues[model] = LegacyCosmeticCompatibilityDetector.Analyze(model.Mod).Issues;

                    // Scanned here rather than in the UI post below: it walks the mod folder, and a
                    // folder that has just been removed would otherwise throw on the UI thread,
                    // where nothing catches it and the process goes down.
                    if (model.Mod.GetAllFiles(".gml").Count > 0 && model.Mod.GetRequiredHooks().Count == 0)
                        looseGml.Add(model);
                }
                catch (Exception exception)
                {
                    Logger.Log($"Modlist compatibility check skipped for {model.Mod.GetId()}: {exception.Message}");
                    detected[model] = [];
                    cosmeticIssues[model] = [];
                }

                // An issue the user has looked at and ticked off in the report is settled. Leaving
                // the row's warning triangle lit for it would contradict the report and teach the
                // user that the triangle means nothing.
                if (IsSettled(dismissed, LoadOrderNoteKind.CompatibilityWarning, "legacy-gml", model.Mod))
                    settledLegacy.Add(model);
                if (IsSettled(dismissed, LoadOrderNoteKind.CompatibilityWarning, "legacy-cosmetic", model.Mod))
                    settledCosmetic.Add(model);
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (refreshVersion != Volatile.Read(ref _conflictRefreshVersion)) return;
                foreach (var model in models)
                {
                    model.SetSettledInlineWarnings(settledInline.GetValueOrDefault(model) ?? []);

                    var warnings = new List<string>();

                    // Both branches below are the same issue as far as the report is concerned -
                    // they share one note and one dismissal - so the tick has to silence the pair.
                    if (!settledLegacy.Contains(model))
                    {
                        if (detected.TryGetValue(model, out var findings) && findings.Count > 0)
                        {
                            // Legacy signatures are advisory. They must be visible
                            // before installation but must never disable a mod.
                            warnings.Add(string.Join("\r\n", new[]
                            {
                                Texts.GUIModLegacyGamePatch,
                                $"  • {string.Join("\r\n  • ", findings)}"
                            }));
                        }
                        else if (looseGml.Contains(model))
                        {
                            warnings.Add(Texts.GUIModLegacyGml);
                        }
                    }

                    if (!settledCosmetic.Contains(model) &&
                        cosmeticIssues.TryGetValue(model, out var issues))
                        warnings.AddRange(issues);

                    model.SetCompatibilityWarnings(warnings);
                }
            });
        });

        _ = Task.Run(() =>
        {
            // Loaded once for the whole sweep. Every warning below is checked against it, so a row
            // shows a triangle only for issues the user has not already settled in the report - and
            // un-ticking one there brings the triangle straight back on the next refresh.
            var dismissed = LoadDismissedIssues();

            IReadOnlyList<ModConflict> conflicts;
            try
            {
                conflicts = ModConflictDetector.Find(selected)
                    .Where(conflict => !IsSettled(dismissed, LoadOrderNoteKind.HookConflict,
                        $"{conflict.Key}|{DescribeOwners(conflict.ModIds, selected)}"))
                    .ToList();
            }
            catch (Exception exception)
            {
                Logger.Log($"Selection conflict check skipped: {exception.Message}");
                conflicts = [];
            }

            IReadOnlyList<ModFileConflict> fileConflicts;
            try
            {
                fileConflicts = FilterSettledFileConflicts(
                    ModFileConflictDetector.Find(selected), selected, dismissed);
            }
            catch (Exception exception)
            {
                Logger.Log($"Selection file-conflict check skipped: {exception.Message}");
                fileConflicts = [];
            }

            // The same real-binding scan the conflict report uses, rather than the mods' compiled-in
            // defaults. A row that keeps warning about a clash the report considers resolved is
            // worse than no warning at all - it teaches the user to ignore the icon.
            Dictionary<string, List<string>> hotkeyWarnings;
            try { hotkeyWarnings = DescribeBindingClashes(selected, dismissed); }
            catch (Exception exception)
            {
                Logger.Log($"Selection hotkey-conflict check skipped: {exception.Message}");
                hotkeyWarnings = [];
            }

            foreach (var conflict in fileConflicts)
                PerformanceDiagnostics.Log($"Selection file conflict: {conflict.Kind}; {conflict.Path}; mods={string.Join(", ", conflict.ModIds)}");

            Dispatcher.UIThread.Post(() =>
            {
                if (refreshVersion != Volatile.Read(ref _conflictRefreshVersion)) return;

                foreach (var model in models)
                {
                    var warnings = conflicts
                        .Where(conflict => conflict.ModIds.Contains(model.Mod.GetId(), StringComparer.OrdinalIgnoreCase))
                        .Select(conflict => string.Format(Texts.GUIModConflicts, $"• {conflict.Description}"))
                        .ToList();
                    var sharedPaths = fileConflicts
                        .Where(conflict => conflict.ModIds.Contains(model.Mod.GetId(), StringComparer.OrdinalIgnoreCase))
                        .Select(conflict => FormatFileConflict(conflict, selected))
                        .ToList();
                    if (sharedPaths.Count > 0)
                        warnings.Add(string.Format(Texts.GUIModFileConflicts, string.Join("\r\n", sharedPaths)));
                    if (hotkeyWarnings.TryGetValue(model.Mod.GetId(), out var hotkeys) && hotkeys.Count > 0)
                        warnings.Add(string.Format(Texts.GUIModHotkeyConflicts, string.Join("\r\n", hotkeys)));
                    model.SetConflictWarnings(warnings);
                }
            });
        });
    }

    /// <summary>
    /// The shortcut clashes each mod is caught in, keyed by mod id, for the row's warning tooltip.
    ///
    /// One line per clash naming the key and the other mods on it - the row has a hover tooltip,
    /// not a report, so it says what and who and leaves the how to "Check issues".
    /// </summary>
    private Dictionary<string, List<string>> DescribeBindingClashes(
        IReadOnlyList<IMod> selected, DismissedIssueStore? dismissed = null)
    {
        var entries = BindingScanner.Scan(selected, ModDataStore.Locate());
        var overlaps = BindingScanner.FindOverlaps(entries);
        var warnings = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (entry, others) in overlaps)
        {
            if (entry.Binding is null) continue;

            // The identity the report files this clash under, built the same way
            // BuildBindingConflictNotes builds it so that a tick there silences the row here.
            var group = others.Append(entry)
                .DistinctBy(item => item.FeatureKey)
                .OrderBy(item => item.ModName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var identity = string.Join(",", group.Select(item => item.FeatureKey).Order(StringComparer.Ordinal));

            if (IsSettled(dismissed, LoadOrderNoteKind.HotkeyConflict, $"{entry.Binding}|{identity}")) continue;

            var line = string.Format(Texts.GUIRowBindingClash,
                entry.Binding,
                entry.FieldLabel,
                string.Join(", ", others.Select(other => other.ModName).Distinct(StringComparer.OrdinalIgnoreCase)));

            if (!warnings.TryGetValue(entry.ModId, out var lines))
                warnings[entry.ModId] = lines = [];
            if (!lines.Contains(line)) lines.Add(line);
        }

        return warnings;
    }

    private string FormatFileConflict(ModFileConflict conflict, IReadOnlyList<IMod> selected)
    {
        var description = conflict.Kind switch
        {
            ModFileConflictKind.HardReplacement => Texts.GUIFileConflictReplacement,
            ModFileConflictKind.MergeableMetadata => Texts.GUIFileConflictMerge,
            ModFileConflictKind.SharedLocalization => Texts.GUIFileConflictLocalization,
            _ => Texts.GUIFileConflictShared
        };
        var owners = selected
            .Where(mod => conflict.ModIds.Contains(mod.GetId(), StringComparer.OrdinalIgnoreCase))
            .Select(mod => $"{mod.GetName()} v{mod.GetVersion()} [{mod.GetSourcePath()}]")
            .ToList();
        return $"• {conflict.Path}\r\n  {description}\r\n  {string.Join("\r\n  ", owners)}";
    }

    private static List<IssueParticipant> ParticipantsFor(
        IEnumerable<string> modIds, IReadOnlyList<IMod> selected)
    {
        var ids = modIds.ToList();
        return selected
            .Where(mod => ids.Any(id => id.Equals(mod.GetId(), StringComparison.OrdinalIgnoreCase)))
            .Select(mod => new IssueParticipant(
                mod.GetId(), mod.GetName(), mod.GetVersion(), mod.GetSourcePath()))
            .ToList();
    }

    /// <summary>
    /// The mods a detector named, paired with the versions currently installed, in a fixed order.
    ///
    /// This is the half of an issue key that makes a dismissal expire: the same two mods at new
    /// versions produce a different string, so "I checked this, it's fine" applies to the code the
    /// user actually checked and not to whatever replaces it.
    /// </summary>
    private static string DescribeOwners(IEnumerable<string> modIds, IReadOnlyList<IMod> selected)
    {
        var ids = modIds.ToList();
        var owners = selected
            .Where(mod => ids.Any(id => id.Equals(mod.GetId(), StringComparison.OrdinalIgnoreCase)))
            .ToList();

        // A detector can name a mod that is no longer in the selection. Falling back to the bare
        // IDs keeps the key stable rather than collapsing it to an empty string that every such
        // issue would then share.
        return owners.Count > 0
            ? LoadOrderNote.DescribeMods(owners)
            : string.Join(",", ids.OrderBy(id => id, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Shortcut clashes, judged on what the mods are actually bound to.
    ///
    /// This used to read the defaults compiled into each mod's source, which meant reporting a
    /// clash between two mods the user had separated in the game's own settings months earlier -
    /// and missing every clash between two keys a user had chosen. Mod settings live in the game's
    /// config directory, so that is where the real answer is; the compiled-in default is used only
    /// for a mod that has never written settings, where it is what the game will use.
    ///
    /// It also covers what the old scan could not: controller buttons, letters and digits, and
    /// chords like SHIFT+F5.
    /// </summary>
    /// <summary>
    /// The row-only warnings, as report notes: the mod's own validation warnings, and duplicate
    /// copies of one mod.
    ///
    /// Keyed by the mod, its version and the warning's own text, so an author fixing the thing in
    /// the next release brings the note back rather than inheriting the tick - the same rule every
    /// other issue key here follows.
    ///
    /// <see cref="SettledInlineWarningsFor"/> reads the same keys back, which is what lets a tick
    /// in the report turn the row's triangle off.
    /// </summary>
    private List<LoadOrderNote> BuildInlineWarningNotes(IReadOnlyList<IMod> selected)
    {
        var notes = new List<LoadOrderNote>();

        var copies = BuildDuplicateCopyMap(selected.ToList());
        var reportedDuplicates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var mod in selected)
        {
            var id = mod.GetId();
            var version = mod.GetVersion();

            foreach (var message in mod.GetValidation().Warnings
                         .Select(warning => warning.Message)
                         .Where(message => !string.IsNullOrWhiteSpace(message))
                         .Distinct(StringComparer.Ordinal))
            {
                notes.Add(new LoadOrderNote(
                    LoadOrderNoteKind.CompatibilityWarning,
                    $"{mod.GetName()} v{version}\r\n{message}")
                {
                    IssueKey = $"validation|{id}@{version}|{message}",
                    Participants = ParticipantsFor([id], selected)
                });
            }

            if (!copies.TryGetValue(DuplicateModDetector.NormalizeSource(mod.GetSourcePath()), out var group) ||
                group.Count <= 1)
                continue;

            // One note per mod, not one per copy: the user is being told they have two of
            // something, and telling them twice is the same joke.
            if (!reportedDuplicates.Add(id)) continue;

            var paths = string.Join("\r\n", group.Select(copy => $"• {copy.GetVersion()} — {copy.GetSourcePath()}"));

            notes.Add(new LoadOrderNote(
                LoadOrderNoteKind.CompatibilityWarning,
                $"{mod.GetName()} v{version}\r\n{string.Format(Texts.GUIModDuplicateCopies, paths)}")
            {
                IssueKey = $"validation|{id}@{version}|{ModModel.DuplicateWarningKey}",
                Participants = ParticipantsFor([id], selected)
            });
        }

        return notes;
    }

    /// <summary>
    /// Which of a row's own warnings the user has already ticked off, as the row states them.
    ///
    /// The mirror of <see cref="BuildInlineWarningNotes"/>: the same keys, read rather than
    /// written. Returns the warning texts, because that is what the row has to match against.
    /// </summary>
    private static IReadOnlyCollection<string> SettledInlineWarningsFor(
        IMod mod, DismissedIssueStore? dismissed)
    {
        if (dismissed is null) return [];

        var id = mod.GetId();
        var version = mod.GetVersion();

        var settled = mod.GetValidation().Warnings
            .Select(warning => warning.Message)
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Distinct(StringComparer.Ordinal)
            .Where(message => IsSettled(dismissed, LoadOrderNoteKind.CompatibilityWarning,
                $"validation|{id}@{version}|{message}"))
            .ToList();

        if (IsSettled(dismissed, LoadOrderNoteKind.CompatibilityWarning,
                $"validation|{id}@{version}|{ModModel.DuplicateWarningKey}"))
            settled.Add(ModModel.DuplicateWarningKey);

        return settled;
    }

    private List<LoadOrderNote> BuildBindingConflictNotes(IReadOnlyList<IMod> selected)
    {
        var entries = BindingScanner.Scan(selected, ModDataStore.Locate());
        var overlaps = BindingScanner.FindOverlaps(entries);
        if (overlaps.Count == 0) return [];

        var notes = new List<LoadOrderNote>();
        var reported = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (entry, others) in overlaps)
        {
            if (entry.Binding is null) continue;

            var group = others.Append(entry)
                .DistinctBy(item => item.FeatureKey)
                .OrderBy(item => item.ModName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Each clashing set appears once, however many of its members we arrive from.
            var identity = string.Join(",", group.Select(item => item.FeatureKey).Order(StringComparer.Ordinal));
            if (!reported.Add(identity)) continue;

            var participants = group
                .Select(item => new IssueParticipant(
                    item.ModId,
                    item.ModName,
                    ModVersionOf(item.ModId, selected),
                    SourcePathOf(item.ModId, selected))
                {
                    Detail = item.Source == BindingSource.ModDefault
                        ? string.Format(Texts.GUIBindingIsDefault, item.FieldLabel)
                        : $"{item.FieldLabel} = {item.Value}"
                })
                .ToList();

            notes.Add(new LoadOrderNote(
                LoadOrderNoteKind.HotkeyConflict,
                string.Format(Texts.GUIModHotkeyConflicts, $"• {entry.Binding}"))
            {
                IssueKey = $"{entry.Binding}|{string.Join(",", group.Select(item => item.FeatureKey).Order(StringComparer.Ordinal))}",
                HotkeyKey = entry.Binding.ToString(),
                Participants = participants
            });
        }

        return notes;
    }

    // ── Issues the user has already settled ──────────────────────────────────────

    /// <summary>
    /// What the user has ticked off in the issues report, for the mods folder in use.
    ///
    /// Read fresh on every refresh rather than cached: the report writes to the same file as soon
    /// as a box is ticked, and a row that keeps warning about an issue the user just resolved is
    /// the thing this is here to prevent.
    /// </summary>
    private DismissedIssueStore? LoadDismissedIssues()
    {
        if (string.IsNullOrEmpty(ModsLocation)) return null;

        try
        {
            return new DismissedIssueStore(ModsLocation);
        }
        catch (Exception exception)
        {
            // No store means every issue is live, which is the safe way to be wrong.
            Logger.Log($"Could not read the settled issues: {exception.Message}");
            return null;
        }
    }

    /// <summary>
    /// Whether an issue has been marked solved, addressed by the same key the report uses.
    /// <see cref="LoadOrderNote.StableKey"/> includes the mod versions, so an update to either mod
    /// makes a new key and the warning returns for a fresh judgement.
    /// </summary>
    private static bool IsSettled(DismissedIssueStore? dismissed, LoadOrderNoteKind kind, string issueKey) =>
        dismissed is not null && issueKey.Length > 0 && dismissed.IsDismissed($"{kind}|{issueKey}");

    private static bool IsSettled(
        DismissedIssueStore? dismissed, LoadOrderNoteKind kind, string prefix, IMod mod) =>
        IsSettled(dismissed, kind, $"{prefix}|{mod.GetId()}@{mod.GetVersion()}");

    /// <summary>
    /// Drops the shared-file conflicts whose issue the user has settled.
    ///
    /// The report does not raise one issue per file; it raises one per set of mods, however many
    /// files they happen to share, and keys it on those mods and their versions. So the conflicts
    /// are grouped the same way here before being matched against the store - matching file by file
    /// would leave a row warning about the very issue the user just ticked off.
    /// </summary>
    private static List<ModFileConflict> FilterSettledFileConflicts(
        IReadOnlyList<ModFileConflict> conflicts, IReadOnlyList<IMod> selected, DismissedIssueStore? dismissed)
    {
        if (dismissed is null) return conflicts.ToList();

        var versions = selected
            .GroupBy(mod => mod.GetId(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last().GetVersion(), StringComparer.OrdinalIgnoreCase);

        return conflicts.Where(conflict =>
        {
            var ids = conflict.ModIds.Where(versions.ContainsKey).ToList();
            if (ids.Count < 2) return true;

            var key = string.Join(",", ids
                .Select(id => $"{id}@{versions[id]}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(entry => entry, StringComparer.OrdinalIgnoreCase));

            // The two combining kinds are reported and dismissed under their own key - see
            // LoadOrderPlanner.DescribeFileConflicts. They are on the rows, so they have to be
            // silenceable from the report like everything else on the rows; the suffix keeps that
            // tick from also silencing an override between the same pair, which is a separate
            // judgement about a different problem.
            var combining = conflict.Kind
                is ModFileConflictKind.MergeableMetadata
                or ModFileConflictKind.SharedLocalization;

            return !IsSettled(dismissed, LoadOrderNoteKind.FileConflict, combining ? key + "|merge" : key);
        }).ToList();
    }

    private static string ModVersionOf(string modId, IReadOnlyList<IMod> selected) =>
        selected.FirstOrDefault(mod => mod.GetId().Equals(modId, StringComparison.OrdinalIgnoreCase))
            ?.GetVersion() ?? "";

    private static string SourcePathOf(string modId, IReadOnlyList<IMod> selected) =>
        selected.FirstOrDefault(mod => mod.GetId().Equals(modId, StringComparison.OrdinalIgnoreCase))
            ?.GetSourcePath() ?? "";

    private List<LoadOrderNote> BuildDetailedSelectionNotes(IReadOnlyList<IMod> selected)
    {
        var notes = new List<LoadOrderNote>();

        try
        {
            foreach (var conflict in ModConflictDetector.Find(selected))
            {
                notes.Add(new LoadOrderNote(
                    LoadOrderNoteKind.HookConflict,
                    string.Format(Texts.GUIModConflicts, $"• {conflict.Description}"))
                {
                    IssueKey = $"{conflict.Key}|{DescribeOwners(conflict.ModIds, selected)}",
                    Participants = ParticipantsFor(conflict.ModIds, selected)
                });
            }
        }
        catch (Exception exception)
        {
            Logger.Log($"Detailed hook-conflict check skipped: {exception.Message}");
        }

        try
        {
            // One note per shortcut rather than one note listing every shortcut: the user judges
            // these individually ("F5 is fine, they never overlap"), and a single lumped note
            // could only be dismissed all or nothing.
            foreach (var note in BuildBindingConflictNotes(selected)) notes.Add(note);
        }
        catch (Exception exception)
        {
            Logger.Log($"Detailed hotkey-conflict check skipped: {exception.Message}");
        }

        // The two warnings that used to exist only on the row: what the mod's own validator said
        // about it, and having two copies of it installed. Neither was ever reported, so a mod
        // could sit there with a lit triangle while the report said there was nothing outstanding -
        // which is exactly the reading that teaches a user to stop looking at triangles.
        try
        {
            foreach (var note in BuildInlineWarningNotes(selected)) notes.Add(note);
        }
        catch (Exception exception)
        {
            Logger.Log($"Detailed inline-warning check skipped: {exception.Message}");
        }

        foreach (var mod in selected)
        {
            try
            {
                var findings = LegacyGameCompatibilityDetector.Find(mod);
                var warning = findings.Count > 0
                    ? string.Join("\r\n", new[]
                    {
                        Texts.GUIModLegacyGamePatch,
                        $"  • {string.Join("\r\n  • ", findings)}"
                    })
                    : mod.GetAllFiles(".gml").Count > 0 && mod.GetRequiredHooks().Count == 0
                        ? Texts.GUIModLegacyGml
                        : null;

                if (warning is not null)
                {
                    notes.Add(new LoadOrderNote(
                        LoadOrderNoteKind.CompatibilityWarning,
                        $"{mod.GetName()} v{mod.GetVersion()}\r\n{warning}")
                    {
                        IssueKey = $"legacy-gml|{mod.GetId()}@{mod.GetVersion()}",
                        Participants = ParticipantsFor([mod.GetId()], selected)
                    });
                }

                var cosmetic = LegacyCosmeticCompatibilityDetector.Analyze(mod);
                if (cosmetic.UsesLegacyFormat)
                {
                    var detail = cosmetic.Issues.Count == 0
                        ? "Uses legacy momi/outfit cosmetic definitions. AIM installs this format in compatibility mode; no files were changed."
                        : string.Join("\r\n", cosmetic.Issues);
                    notes.Add(new LoadOrderNote(
                        LoadOrderNoteKind.CompatibilityWarning,
                        $"{mod.GetName()} v{mod.GetVersion()}\r\n{detail}")
                    {
                        IssueKey = $"legacy-cosmetic|{mod.GetId()}@{mod.GetVersion()}",
                        Participants = ParticipantsFor([mod.GetId()], selected)
                    });
                }
            }
            catch (Exception exception)
            {
                Logger.Log($"Detailed compatibility check skipped for {mod.GetId()}: {exception.Message}");
            }
        }

        return notes;
    }

    private sealed record ModlistSnapshot(
        string MistriaLocation,
        string ModsLocation,
        string CurrentMistriaLocation,
        string CurrentModsLocation);

    private sealed record ModlistLoadResult(
        string MistriaLocation,
        string ModsLocation,
        ProfileManager? ProfileManager,
        List<IMod> OrderedMods,
        HashSet<string> EnabledIds,
        HashSet<string> EnabledSources,
        Dictionary<string, IReadOnlyList<IMod>> DuplicateCopies);

    // Checks all mods for updates in parallel; each result updates the matching
    // ModModel on the UI thread so the update badge appears as responses arrive.
    //
    // An instance method because a badge appearing has to reach the "needs attention" filter, which
    // is view-model state. Both callers are instance methods already.
    private async Task CheckModUpdatesAsync(List<ModModel> models)
    {
#if AIM_NEXUS_DISTRIBUTION
        // Nexus packages must not use GitHub as an update source. This remains
        // disabled until Nexus approves the application's OAuth/API flow.
        PerformanceDiagnostics.Log("Mod update checks skipped for Nexus distribution");
        return;
#else

        var stopwatch = Stopwatch.StartNew();
        var tasks = models.Select(async model =>
        {
            try
            {
                var info = await UpdateChecker.CheckAsync(model.Mod);
                if (info?.IsNewer != true) return;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    model.UpdateAvailable    = true;
                    model.LatestVersion      = info.LatestVersion;
                    model.UpdateDownloadUrl  = info.DownloadUrl;

                    // This runs after the list is already on screen, so the badge appearing has to
                    // reach the "needs attention" filter too.
                    RefreshPendingUpdates();
                });
            }
            catch { /* network failures are silent */ }
        });
        await Task.WhenAll(tasks);
        PerformanceDiagnostics.Log($"Mod update checks: {stopwatch.ElapsedMilliseconds} ms, mods={models.Count}");
#endif
    }

    public async Task CheckForModUpdatesNowAsync()
    {
#if AIM_NEXUS_DISTRIBUTION
        // Nexus downloads and explicit
        // update checks. No fake/test badges are produced here.
        await CheckManyForUpdates(Mods.ToList());
#else
        await CheckModUpdatesAsync(Mods.ToList());
#endif
    }

    [RelayCommand]
    private async Task CheckForUpdatesNow()
    {
        if (IsInstalling) return;
        await CheckForModUpdatesNowAsync();
    }

    // ── Observable properties ─────────────────────────────────────────────────────

    [ObservableProperty] private string _installStatus = "";

    // Describes the archive on disk separately from the selected profile.
    [ObservableProperty] private string _archiveStatus = "";

    private static readonly bool isAprilFools = DateTime.Today.Month == 4 && DateTime.Today.Day == 1;

    [ObservableProperty] private string _greetingText =
        isAprilFools ? Resources.GUIGreetingText_April : Resources.GUIGreetingText;

    [ObservableProperty] private string _installButtonText =
        isAprilFools ? Resources.GUIInstallButtonText_April : Resources.GUIInstallButtonText;

    [ObservableProperty] private string _installInProgressText =
        isAprilFools ? Resources.GUIInstallInProgress_April : Resources.GUIInstallInProgress;

    [ObservableProperty] private string _noModsToInstallText =
        isAprilFools ? Resources.GUINoModsToInstall_April : Resources.GUINoModsToInstall;

    [ObservableProperty] private string _modsWillBeInstalledText =
        isAprilFools ? Resources.GUIModsWillBeInstalled_April : Resources.GUIModsWillBeInstalled;

    [NotifyCanExecuteChangedFor(nameof(InstallModsCommand))]
    [ObservableProperty] private string _modsLocation = "";

    [NotifyCanExecuteChangedFor(nameof(InstallModsCommand))]
    [NotifyCanExecuteChangedFor(nameof(UnInstallModsCommand))]
    [ObservableProperty] private string _mistriaLocation = "";

    [ObservableProperty] private string _exception = "";

    [NotifyCanExecuteChangedFor(nameof(InstallModsCommand))]
    [NotifyCanExecuteChangedFor(nameof(UnInstallModsCommand))]
    [NotifyCanExecuteChangedFor(nameof(LaunchGameCommand))]
    [NotifyPropertyChangedFor(nameof(CanReorderMods))]
    [NotifyPropertyChangedFor(nameof(CanDragReorderMods))]
    [NotifyPropertyChangedFor(nameof(CanChangeModSelection))]
    [NotifyCanExecuteChangedFor(nameof(ToggleAllModsCommand))]
    [NotifyCanExecuteChangedFor(nameof(SuggestLoadOrderCommand))]
    [ObservableProperty] private bool _isInstalling;

    partial void OnIsInstallingChanged(bool value)
    {
        foreach (var model in Mods)
            model.ContextActionsLocked = value;
    }

    // Load order is part of the same profile state as the checkboxes. Do not let a
    // drag operation alter it while an archive operation is using that state.
    public bool CanReorderMods => !IsInstalling;

    // Reordering only part of the list, or a list shown in an order that is not the real one, would
    // make the resulting full order unclear - so drag and drop is paused whenever the view is
    // filtered or sorted, not only while searching.
    public bool CanDragReorderMods => !IsInstalling && !IsListReordered;

    // Keep checkbox changes out of an in-progress archive operation as well.
    public bool CanChangeModSelection => !IsInstalling;

    [NotifyCanExecuteChangedFor(nameof(InstallModsCommand))]
    [ObservableProperty] private bool _installationNeedsRebuild;

    [NotifyCanExecuteChangedFor(nameof(LaunchGameCommand))]
    [ObservableProperty] private bool _gameReady;

    public ObservableCollection<ModModel> Mods { get; } = [];

    /// <summary>
    /// What the list shows, which is not what the list <em>is</em>.
    ///
    /// Searching, filtering and A-Z sorting never alter profile state or load order: they supply
    /// only the visible projection, so none of them reopens archives or reruns compatibility scans,
    /// and none of them changes which mod wins a shared file. <see cref="Mods"/> stays in load
    /// order throughout.
    /// </summary>
    public IReadOnlyList<ModModel> FilteredMods => _filteredMods;

    public bool HasModSearch => !string.IsNullOrWhiteSpace(ModSearchQuery);

    /// <summary>True when the list is not showing every mod in its real order.</summary>
    public bool IsListReordered =>
        HasModSearch || ShowOnlyEnabled || ShowOnlyUpdatable || SortAlphabetically || SortByRecentlyUpdated;

    [ObservableProperty] private string _modSearchQuery = "";

    /// <summary>
    /// Sorts the visible list A-Z. Purely a way of finding a mod in a long list - the load order
    /// underneath is untouched, which is why dragging is paused while it is on.
    /// </summary>
    [ObservableProperty] private bool _sortAlphabetically;

    /// <summary>
    /// Sorts the visible list by when each mod last changed on disk, newest first. Also view-only.
    /// </summary>
    [ObservableProperty] private bool _sortByRecentlyUpdated;

    /// <summary>
    /// Show only the mods that are switched on.
    ///
    /// The load order that matters is the order of the ticked mods - the rest are not in the game -
    /// and on a list of two hundred with forty ticked, reading that order means scrolling past a
    /// hundred and sixty rows that have nothing to do with it. Like the other filters, this changes
    /// only what is shown; the order underneath is untouched, which is why dragging pauses while it
    /// is on.
    /// </summary>
    [ObservableProperty] private bool _showOnlyEnabled;

    /// <summary>Show only mods with a pending update, or that the last update check could not reach.</summary>
    [ObservableProperty] private bool _showOnlyUpdatable;

    private static bool NeedsUpdateAttention(ModModel model) =>
        model.UpdateAvailable || model.UpdateCheckFailed;

    partial void OnModSearchQueryChanged(string value) => RefreshVisibleMods();

    // The two sorts are alternatives, not layers - a list cannot be in two orders at once - so
    // turning one on turns the other off rather than silently losing to it in FilteredMods.
    partial void OnSortAlphabeticallyChanged(bool value)
    {
        if (value) SortByRecentlyUpdated = false;
        RefreshVisibleMods();
    }

    partial void OnSortByRecentlyUpdatedChanged(bool value)
    {
        if (value) SortAlphabetically = false;
        RefreshVisibleMods();
    }

    partial void OnShowOnlyEnabledChanged(bool value) => RefreshVisibleMods();

    partial void OnShowOnlyUpdatableChanged(bool value) => RefreshVisibleMods();

    private void RefreshVisibleMods()
    {
        RefreshFilteredMods();
        OnPropertyChanged(nameof(HasModSearch));
        OnPropertyChanged(nameof(IsListReordered));
        OnPropertyChanged(nameof(CanDragReorderMods));
    }

    private bool MatchesModSearch(ModModel model)
    {
        var query = ModSearchQuery.Trim();
        return model.Mod.GetDisplayName(Localization.LanguageCode).Contains(query, StringComparison.OrdinalIgnoreCase)
               || model.Mod.GetName().Contains(query, StringComparison.OrdinalIgnoreCase)
               || model.Mod.GetAuthor().Contains(query, StringComparison.OrdinalIgnoreCase)
               || model.Mod.GetDisplayDescription(Localization.LanguageCode).Contains(query, StringComparison.OrdinalIgnoreCase)
               || model.Mod.GetDisplayDescription(null).Contains(query, StringComparison.OrdinalIgnoreCase)
               || model.Mod.GetVersion().Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshFilteredMods()
    {
        // Keep the same projection when its contents have not changed so
        // filtering does not recreate visible mod rows unnecessarily.
        IReadOnlyList<ModModel> filtered;

        if (!IsListReordered)
        {
            filtered = Mods;
        }
        else
        {
            IEnumerable<ModModel> visible = Mods;

            if (HasModSearch) visible = visible.Where(MatchesModSearch);
            if (ShowOnlyEnabled) visible = visible.Where(model => model.Enabled);
            if (ShowOnlyUpdatable) visible = visible.Where(NeedsUpdateAttention);

            // Ordinal-ignore-case rather than the culture's collation: the list is a lookup aid,
            // and a stable A-Z that matches what the user sees beats a linguistically perfect one
            // that reorders itself when the UI language changes.
            if (SortAlphabetically)
                visible = visible.OrderBy(
                    model => model.Mod.GetDisplayName(Localization.LanguageCode),
                    StringComparer.OrdinalIgnoreCase);
            else if (SortByRecentlyUpdated)
                visible = visible
                    .OrderByDescending(model => model.UpdatedAt)
                    .ThenBy(model => model.Mod.GetDisplayName(Localization.LanguageCode),
                        StringComparer.OrdinalIgnoreCase);

            filtered = visible.ToList();
        }

        // Mods is observable, so the repeater already receives collection
        // changes when the unfiltered projection points at it.
        if (ReferenceEquals(_filteredMods, filtered))
            return;

        if (_filteredMods.Count == filtered.Count && _filteredMods.SequenceEqual(filtered))
            return;

        _filteredMods = filtered;
        OnPropertyChanged(nameof(FilteredMods));
    }

    // ── Commands ──────────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private void InstallMods() => _ = RunInstallAsync();

    /// <summary>
    /// The install itself, awaitable and with an answer.
    ///
    /// The button does not care when the install finishes - it has the progress line and the row
    /// icons to say so. The crash check does: "switch this mod off and see whether the crash comes
    /// back" is only a check if the game is rebuilt before it is launched, and only an answer if a
    /// failed rebuild is reported rather than silently followed by a run of the old archive.
    /// </summary>
    /// <returns>Null when the install succeeded, or the reason it did not.</returns>
    private async Task<string?> RunInstallAsync()
    {
        var duplicate = FindSelectedDuplicateGroup();
        if (duplicate is not null)
        {
            var message = Localized("GUIDuplicateModInstallBlocked");
            Exception = message;
            InstallStatus = message;
            return message;
        }

        // Hard replacements are advisory, not fatal: the selected load order decides
        // which replacement is written last. The conflict is already shown on the
        // affected mod rows. Keep blocking only duplicate physical copies of the
        // same mod, which have no meaningful load-order winner.
        Exception = "";

        // Auto-save profile state before installing so load order is persisted
        SaveCurrentProfileState();

        // The icons describe the install that is about to run, not the last one
        foreach (var mod in Mods) mod.SetInstallOutcome(ModInstallState.None);

        InstallStatus = InstallInProgressText;
        IsInstalling  = true;
        StartInstallUiDiagnostics();

        // BackgroundInstall catches its own failures and returns the reason, so this reads the
        // outcome of *this* install rather than the page's Exception banner.
        //
        // The distinction matters more than it looks. Exception is page-wide and anything may set
        // it: the mods-folder watcher noticing the archive change, a Nexus check finishing, a
        // validation warning from an unrelated mod. A caller that read it as "did my install work"
        // - which the crash window's disable-and-check does - would be told the rebuild failed
        // because something else had something to say, and would stop before launching the game.
        return await BackgroundInstall();
    }

    [RelayCommand]
    private async Task SaveLogFile()
    {
        var topLevel = App.TopLevel;
        if (topLevel is null) return;

        var logs  = Logger.GetLogs();
        var files = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title             = Resources.GUIPickLogFile,
            SuggestedFileName = $"aim-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
            DefaultExtension  = "txt",
            FileTypeChoices   = [FilePickerFileTypes.TextPlain]
        });

        if (files is not null)
            await File.WriteAllTextAsync(files.Path.AbsolutePath, string.Join("\r\n", logs));
    }

    [RelayCommand]
    private void DismissException() => Exception = "";

    [RelayCommand]
    private void ClearModSearch() => ModSearchQuery = "";

    [RelayCommand]
    private void ReloadModlist()
    {
        Exception = "";
        UpdateModlist(true);
    }

    /// <summary>
    /// One button for the header checkbox: select everything, or clear the selection when
    /// everything is already selected. A partial selection fills up rather than clearing, which is
    /// what a half-ticked box invites you to do.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanChangeModSelection))]
    private void ToggleAllMods()
    {
        if (Mods.Count == 0) return;

        var select = Mods.Where(mod => !mod.InError).Any(mod => !mod.Enabled);
        SetAllModSelection(select);
    }

    /// <summary>
    /// Ctrl+A: tick or untick everything currently on screen, and nothing else.
    ///
    /// Scoped to the filter rather than to the whole list on purpose, and this is the difference
    /// that makes it usable. With a search or a filter on, the rows the user can see are the set
    /// they are thinking about - "all the Crys mods", "everything with an update" - and a select-all
    /// that quietly took in the two hundred mods scrolled out of view would be a select-all nobody
    /// could risk pressing. With no filter on, everything is visible and this is a plain select-all.
    ///
    /// Filling up rather than clearing when the visible set is part-ticked, for the same reason the
    /// header checkbox does: a half-ticked box invites completing it.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanChangeModSelection))]
    private void ToggleVisibleMods()
    {
        var visible = FilteredMods.Where(mod => !mod.InError).ToList();
        if (visible.Count == 0) return;

        SetModSelection(visible, visible.Any(mod => !mod.Enabled));
    }

    /// <summary>
    /// Applies a bulk checkbox change as one logical selection update. The
    /// individual rows still notify their bindings immediately, but costly
    /// derived state (archive status and selected-mod scans) is recomputed once.
    /// </summary>
    private void SetAllModSelection(bool enabled) =>
        SetModSelection(Mods.Where(mod => !mod.InError).ToList(), enabled);

    private void SetModSelection(IReadOnlyList<ModModel> rows, bool enabled)
    {
        _bulkSelectionChangeDepth++;
        try
        {
            // A mod AIM cannot install is left alone wherever bulk selection happens: its checkbox
            // is disabled for a reason, and a shortcut must not be a way around that.
            foreach (var mod in rows.Where(mod => !mod.InError))
                mod.Enabled = enabled;
        }
        finally
        {
            _bulkSelectionChangeDepth--;
        }

        _isDirty = true;
        RefreshSelectionSummary();
        RefreshArchiveStatus();
        RefreshSelectedModConflicts();
        InstallModsCommand.NotifyCanExecuteChanged();
        UnInstallModsCommand.NotifyCanExecuteChanged();

        // The enabled-only filter is a view of exactly this property, so rows that have just left
        // the filter have to leave the list with it.
        if (ShowOnlyEnabled) RefreshVisibleMods();
    }

    // Where the last checkbox click landed, as an index into the visible list. Ranges are measured
    // against what the user can see: shift-clicking two rows that appear adjacent must not sweep up
    // everything hidden between them by a search or a filter.
    private int _lastToggledVisibleIndex = -1;

    /// <summary>
    /// Extends a checkbox click across a range, the way a file manager does.
    ///
    /// Called after the clicked row has already flipped, so its new state is what the rest of the
    /// range is set to - shift-clicking to tick sets the range ticked, shift-clicking to untick
    /// clears it.
    /// </summary>
    /// <param name="clicked">The row whose checkbox was just changed.</param>
    /// <param name="extend">True when Shift was held.</param>
    public void ExtendSelectionTo(ModModel clicked, bool extend)
    {
        var visible = FilteredMods;

        // By identity, and by hand: FilteredMods is an IReadOnlyList, which has no IndexOf, and
        // rows are the same ModModel instances the list holds - two mods that merely look alike
        // must not match.
        var index = -1;
        for (var i = 0; i < visible.Count; i++)
        {
            if (!ReferenceEquals(visible[i], clicked)) continue;
            index = i;
            break;
        }

        if (index < 0)
        {
            _lastToggledVisibleIndex = -1;
            return;
        }

        if (extend && _lastToggledVisibleIndex >= 0 && _lastToggledVisibleIndex < visible.Count)
        {
            var from = Math.Min(_lastToggledVisibleIndex, index);
            var to = Math.Max(_lastToggledVisibleIndex, index);
            var value = clicked.Enabled;

            _bulkSelectionChangeDepth++;
            try
            {
                for (var i = from; i <= to; i++)
                {
                    // A mod AIM cannot install is left alone: its checkbox is disabled for a
                    // reason, and a range should not be a way around that.
                    if (!visible[i].InError) visible[i].Enabled = value;
                }
            }
            finally
            {
                _bulkSelectionChangeDepth--;
            }

            _isDirty = true;
            RefreshSelectionSummary();
            RefreshArchiveStatus();
            RefreshSelectedModConflicts();
            InstallModsCommand.NotifyCanExecuteChanged();
            UnInstallModsCommand.NotifyCanExecuteChanged();
        }

        // The anchor moves to the row just clicked, shift or not, so the next shift-click extends
        // from here.
        _lastToggledVisibleIndex = index;
    }

    /// <summary>
    /// Reorders the list so every mod loads after the mods it requires.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanReorderMods))]
    private async Task SuggestLoadOrder()
    {
        if (Mods.Count == 0) return;

        try
        {
            var current = Mods.Select(model => model.Mod).ToList();
            var enabled = Mods.Where(model => model.Enabled).Select(model => model.Mod).ToList();

            // groupByRole: this is the one caller that is asking for the best order AIM can
            // propose, rather than for a reading of the order the user already has.
            var plan = await Task.Run(() =>
                LoadOrderPlanner.Plan(current, enabled, groupByRole: true));

            // Requirement moves first: they are facts, and a user skimming the window should meet
            // the certain things before the advisory ones.
            var orderNotes = plan.Notes
                .Where(note => note.Kind is LoadOrderNoteKind.DependencyMove or LoadOrderNoteKind.RoleMove)
                .OrderBy(note => note.Kind == LoadOrderNoteKind.RoleMove)
                .ToList();

            if (plan.ChangesAnything)
            {
                var reordered = plan.Order
                    // IDs are not unique when both a folder and an archive copy are present.
                    // Match the actual mod instance so Suggest Order never collapses duplicate rows.
                    .Select(mod => Mods.FirstOrDefault(model => ReferenceEquals(model.Mod, mod)))
                    .Where(model => model is not null)
                    .Select(model => model!)
                    .ToList();

                if (reordered.Count == Mods.Count)
                {
                    Mods.Clear();
                    foreach (var model in reordered) Mods.Add(model);
                }

                RefreshPositions();
                _isDirty = true;
            }

            var summary = plan.ChangesAnything ? Texts.GUILoadOrderChanged : Texts.GUILoadOrderAlreadyGood;

            if (App.TopLevel is Window owner)
                await LoadOrderResultWindow.ShowAsync(owner, summary, orderNotes, compact: true);
            else
                await MessageBoxManager.GetMessageBoxStandard(
                    Texts.GUILoadOrderTitle,
                    summary,
                    ButtonEnum.Ok).ShowAsync();
        }
        catch (Exception exception)
        {
            Logger.Log($"Suggest load order failed: {exception}");
            Exception = $"{Texts.GUILoadOrderTitle}: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task ReportConflicts()
    {
        if (Mods.Count == 0) return;

        if (_issueReportWindow?.IsVisible == true)
        {
            _issueReportWindow.Activate();
            return;
        }

        try
        {
            var report = await BuildIssueReportAsync();

            if (App.TopLevel is Window)
            {
                _issueReportWindow = LoadOrderResultWindow.Show(
                    report,
                    Texts.GUIConflictReportTitle,
                    BuildIssueReportAsync,
                    await GetDismissedIssuesAsync(),
                    ConflictActions,

                    // Every tick, not just the last one. The rows read the same store, so re-running
                    // the sweep is what makes their warning triangles agree with the report - and
                    // the report is modeless, so "when it closes" could be twenty minutes of the
                    // list contradicting the window sitting next to it.
                    RefreshSelectedModConflicts);

                _issueReportWindow.Closed += (_, _) =>
                {
                    _issueReportWindow = null;

                    // Once more on the way out, in case anything settled the store without going
                    // through the report - the research window can be closed by applying a fix.
                    RefreshSelectedModConflicts();
                };
            }
            else
                await MessageBoxManager.GetMessageBoxStandard(
                    Texts.GUIConflictReportTitle,
                    report.Summary.Length > 0 ? report.Summary : string.Join("\r\n\r\n", report.Notes.Select(note => $"• {note.Message}")),
                    ButtonEnum.Ok).ShowAsync();
        }
        catch (Exception exception)
        {
            Logger.Log($"Conflict report failed: {exception}");
            Exception = $"{Texts.GUIConflictReportTitle}: {exception.Message}";
        }
    }

    // ── Crashes ───────────────────────────────────────────────────────────────────

    private CrashArchive? _crashArchive;
    private CrashTrialStore? _crashTrials;
    private CrashWatcher? _crashWatcher;

    private CrashArchive CrashLogs => _crashArchive ??= new CrashArchive();

    /// <summary>
    /// What earlier disable-and-check runs proved, so the hunt for a bad mod survives closing the
    /// window. Null until there is a mods folder to keep it in.
    /// </summary>
    private CrashTrialStore? CrashTrials
    {
        get
        {
            if (string.IsNullOrEmpty(ModsLocation)) return null;
            if (_crashTrials is not null) return _crashTrials;

            try
            {
                var store = new CrashTrialStore(ModsLocation);

                // A year, matching the dismissed issues. A verdict older than that is about mods
                // and a game version that have both moved on.
                store.PruneOlderThan(TimeSpan.FromDays(365));
                return _crashTrials = store;
            }
            catch (Exception exception)
            {
                // Losing the record costs the user repeated runs; refusing to open the crash window
                // costs them the diagnosis. The former is the cheaper failure.
                Logger.Log($"Could not open the crash trial store: {exception.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// Starts watching for crashes, if it is not already.
    ///
    /// Armed by pressing Play and by opening the crash window, which between them cover the moments
    /// a crash is about to happen and the moment the user has come looking for one. It is not armed
    /// at startup: a user who never launches the game from AIM has nothing to capture, and a file
    /// watcher on a folder that may not exist is not worth holding open for a session.
    /// </summary>
    private void EnsureCrashWatcher()
    {
        if (_crashWatcher is not null) return;

        try
        {
            // The load order is read at capture time rather than now: the point of recording it is
            // to know what was installed when the game broke, and that is a different list from
            // whatever was selected when the watcher started.
            _crashWatcher = new CrashWatcher(CrashLogs, () => (ModsForCrashReport(), LastInstalledAt()));
            _crashWatcher.Start();
        }
        catch (Exception exception)
        {
            // Not being able to watch costs the crash archive its history, not AIM its session.
            Logger.Log($"Could not start the crash watcher: {exception.Message}");
            _crashWatcher = null;
        }
    }

    /// <summary>The enabled mods in load order, as the crash archive records them.</summary>
    private IReadOnlyList<string> ModsForCrashReport() =>
        Mods.Where(model => model.Enabled)
            .Select(model => $"{model.Mod.GetId()} {model.Mod.GetVersion()}".Trim())
            .ToList();

    /// <summary>
    /// When the archive now on disk was published, which is what dates a crash against it.
    ///
    /// Read from the install state rather than remembered in this session: the interesting case is
    /// a crash from before AIM was even opened, and this session knows nothing about that install.
    /// </summary>
    private DateTimeOffset? LastInstalledAt()
    {
        if (string.IsNullOrEmpty(MistriaLocation)) return null;

        try
        {
            return new AssetsStore(MistriaLocation).GetRecordedInstallState()?.InstalledAtUtc;
        }
        catch (Exception exception)
        {
            Logger.Log($"Could not read when the archive was installed: {exception.Message}");
            return null;
        }
    }

    [RelayCommand]
    private async Task CheckCrashes()
    {
        if (App.TopLevel is not Window owner) return;

        EnsureCrashWatcher();

        try
        {
            var client = Nexus is null ? null : await Nexus.CreateApiClientAsync();
            await CrashAnalysisWindow.ShowAsync(owner, BuildCrashContext(), client);

            // Whatever the window proved, said on the rows. It marks the culprit itself as it
            // happens, but a rebuild inside the check replaces the rows underneath that, so the
            // marks are put back from the store once the window is closed.
            RefreshCrashMarks();
        }
        catch (Exception exception)
        {
            Logger.Log($"The crash check failed: {exception}");
            Exception = $"{Texts.GUICrashTitle}: {exception.Message}";
        }
    }

    /// <summary>
    /// Everything the crash window is allowed to do, and nothing else.
    ///
    /// Each capability is a method that already exists for some other part of AIM - the same
    /// disable the checkbox performs, the same install the button runs, the same restore the row's
    /// version dropdown offers. Nothing here is a second implementation that would have to be kept
    /// in step with the first, which matters more than usual for the ones that edit a mod.
    /// </summary>
    private CrashContext BuildCrashContext() =>
        new(Mods.Where(model => model.Enabled).Select(model => model.Mod).ToList(),
            MistriaLocation,
            LastInstalledAt())
        {
            Installed = SnapshotForResearch(),
            Disable = DisableModById,
            Enable = EnableModById,
            IsEnabled = IsModEnabledById,
            RefreshCrasherMark = RefreshCrashMarkFor,
            Trials = CrashTrials,
            Reinstall = ReinstallForCrashCheck,
            RunAndWatch = RunAndWatchGame,
            SetAside = BackupStore is null ? null : SetAsideModFiles,
            ReplaceLine = BackupStore is null ? null : ReplaceLineInMod,
            Repairs = RepairsFor,
            ApplyRepair = BackupStore is null ? null : ApplyRepairToMod,
            PutBack = PutBackModFiles,
            Versions = VersionsFor,
            RestoreVersion = RestoreVersionFor,
            RemoveMod = RemoveModById,
            EditHistory = modId => EditStore?.Edits(modId).Select(edit => edit.Describe()).ToList() ?? []
        };

    /// <summary>
    /// Reapplies the known-crasher marks from the verdicts on disk.
    ///
    /// Always recomputed, never set from the outside. A reload builds fresh rows, and a mark that
    /// only lived on the old ones would last until the mods folder changed - which is to say, until
    /// the next time the user installed anything, which is exactly when they are deciding what to
    /// tick. Recomputing is also what makes taking a mark back safe: a mod cleared for one crash may
    /// still be the proven culprit of another, and only the store knows that.
    ///
    /// The verdict is matched against the version now on disk, so an update clears the mark: a mod
    /// that has been fixed should not carry the previous version's crime, and a mark the user has
    /// learned to disbelieve is worse than none.
    /// </summary>
    private void RefreshCrashMarks()
    {
        foreach (var row in Mods) RefreshCrashMarkFor(row);
    }

    private void RefreshCrashMarkFor(string modId)
    {
        var row = Mods.FirstOrDefault(model =>
            string.Equals(model.Mod.GetId(), modId, StringComparison.OrdinalIgnoreCase));

        if (row is not null) RefreshCrashMarkFor(row);
    }

    private void RefreshCrashMarkFor(ModModel row)
    {
        var trials = CrashTrials;
        if (trials is null) return;

        CrashTrial? verdict;

        try
        {
            verdict = trials.GuiltyVerdict(row.Mod.GetId(), row.Mod.GetVersion());
        }
        catch (Exception exception)
        {
            Logger.Log($"Could not read the crash verdict for {row.Mod.GetId()}: {exception.Message}");
            return;
        }

        row.IsKnownCrasher = verdict is not null;

        // Said as the user's own call when it was one. AIM claiming to have proved something the
        // user simply asserted would make the badge worth less than it is on the rows where AIM did
        // prove it.
        row.KnownCrasherIsManual = verdict?.Manual == true;

        row.KnownCrasherSummary = verdict is null
            ? ""
            : string.Format(
                Texts.GUIModCrasherSummary,
                string.IsNullOrWhiteSpace(verdict.ModVersion) ? "?" : verdict.ModVersion,
                verdict.TestedAt.LocalDateTime.ToString("g", CultureInfo.CurrentCulture),
                verdict.Note);
    }

    /// <summary>Whether a mod is ticked right now, asked of the list rather than of a snapshot.</summary>
    private bool IsModEnabledById(string modId) =>
        Mods.Any(model =>
            string.Equals(model.Mod.GetId(), modId, StringComparison.OrdinalIgnoreCase) && model.Enabled);

    /// <summary>The mirror of <see cref="EnableModById"/>: switches a mod off by id.</summary>
    private bool DisableModById(string modId)
    {
        var row = Mods.FirstOrDefault(model =>
            string.Equals(model.Mod.GetId(), modId, StringComparison.OrdinalIgnoreCase));

        if (row is null || !row.Enabled) return false;

        row.Enabled = false;
        RefreshSelectedModConflicts();

        // The archive status is what decides whether the crash check bothers to rebuild, and this
        // is the moment it stopped being true. Leaving it stale would let a disable-and-check skip
        // the rebuild, launch a game that still contains the mod it just switched off, and report
        // that disabling it changed nothing.
        RefreshArchiveStatus();

        // Written to the profile now, not at the next install. The profile on disk is what a
        // reload reads the ticks back from, and a crash check reloads twice - once for the
        // rebuild, once when the folder watcher notices it - so a selection that lives only in
        // memory does not survive the run it was made for.
        SaveCurrentProfileState();
        return true;
    }

    /// <summary>
    /// Rebuilds the game archive so a change to the mod list is a change the game can see.
    ///
    /// A rebuild that is not needed is skipped rather than run: the archive is several hundred
    /// megabytes, and rebuilding it to test a change nobody made would cost the user a minute to
    /// learn nothing.
    /// </summary>
    private async Task<string?> ReinstallForCrashCheck()
    {
        if (IsInstalling) return Texts.GUICrashAlreadyInstalling;
        if (!InstallationNeedsRebuild) return null;
        if (string.IsNullOrEmpty(MistriaLocation) || !Mods.Any(model => model.Enabled))
            return Texts.GUICrashNothingToInstall;

        return await RunInstallAsync();
    }

    /// <summary>Starts the game with AIM watching, and reports whether the crash came back.</summary>
    private async Task<GameRunOutcome> RunAndWatchGame(TimeSpan window)
    {
        var archive = CrashLogs;

        // Taken before the run, so a crash file left over from last week cannot be mistaken for
        // this run's result.
        var before = await Task.Run(archive.Latest);

        return await GameRunRecorder.RunAsync(
            MistriaLocation, ModsForCrashReport(), LastInstalledAt(), before, archive, window);
    }

    /// <summary>
    /// Applies a one-line fix inside a mod, taking a full copy of it first.
    ///
    /// The change itself came from a bug thread and was typed by the user; everything AIM adds is
    /// the bookkeeping around it. Same store, same snapshot and same row marker as the set-aside
    /// edit, so a line change and a disabled file are undone by the same dropdown.
    /// </summary>
    private async Task<EditOutcome> ReplaceLineInMod(
        string modId, string path, int line, string replacement, string reason)
    {
        var backups = BackupStore;
        var store = EditStore;

        if (backups is null || store is null) return EditOutcome.Refused(Texts.GUICrashNoModsFolder);

        var row = Mods.FirstOrDefault(model =>
            string.Equals(model.Mod.GetId(), modId, StringComparison.OrdinalIgnoreCase));

        if (row is null) return EditOutcome.Refused(Texts.GUICrashModGone);

        var outcome = await Task.Run(() =>
            ModFileEditor.ReplaceLine(row.Mod, path, line, replacement, reason, backups, store));

        if (outcome.Applied) RefreshEditedState(row);

        return outcome;
    }

    /// <summary>
    /// The fixes AIM can justify for one mod without being told what to change.
    ///
    /// Reads the mod's data files, so the caller runs it off the UI thread.
    /// </summary>
    private IReadOnlyList<ModRepair> RepairsFor(string modId)
    {
        var row = Mods.FirstOrDefault(model =>
            string.Equals(model.Mod.GetId(), modId, StringComparison.OrdinalIgnoreCase));

        if (row is null) return [];

        try
        {
            return ModRepairPlanner.For(row.Mod);
        }
        catch (Exception exception)
        {
            // A scan that fails offers no fixes, which is the correct outcome and not a reason to
            // take the crash window down.
            Logger.Log($"Could not work out fixes for {row.Mod.GetName()}: {exception}");
            return [];
        }
    }

    /// <summary>
    /// Applies a fix AIM worked out itself, then holds the mod back from updates.
    ///
    /// The edit goes through exactly the same path as a fix the user typed in from a bug thread -
    /// same snapshot, same version-history entry, same marker on the row - because it carries
    /// exactly the same risk and deserves exactly the same way back.
    ///
    /// The freeze is the part that is specific to AIM having made the change. An update replaces a
    /// mod's folder wholesale, so the next routine update run would silently discard the fix and
    /// the crash would come back with nothing on screen connecting the two. Freezing stops that,
    /// and because the freeze carries a reason, the update check still looks at the mod and reports
    /// a new version as "this may fix what you patched" rather than going quiet on it forever.
    /// </summary>
    private async Task<EditOutcome> ApplyRepairToMod(string modId, ModRepair repair)
    {
        var backups = BackupStore;
        var store = EditStore;

        if (backups is null || store is null) return EditOutcome.Refused(Texts.GUICrashNoModsFolder);

        var row = Mods.FirstOrDefault(model =>
            string.Equals(model.Mod.GetId(), modId, StringComparison.OrdinalIgnoreCase));

        if (row is null) return EditOutcome.Refused(Texts.GUICrashModGone);

        var reason = $"AIM's own fix: {repair.Title}";

        var outcome = await Task.Run(() =>
            ModFileEditor.ReplaceLine(row.Mod, repair.Path, repair.Line, repair.Becomes, reason, backups, store));

        if (!outcome.Applied) return outcome;

        RefreshEditedState(row);

        try
        {
            UpdateService?.SetFrozen(row.Mod, true, reason);
            row.IsFrozen = true;
            row.FreezeReason = reason;
        }
        catch (Exception exception)
        {
            // The fix is applied and safe either way; failing to freeze means the user may lose it
            // to an update, which is worth a log line and not worth undoing the repair over.
            Logger.Log($"Could not freeze {row.Mod.GetName()} after fixing it: {exception.Message}");
        }

        // The archive still contains the file as it was, so the fix is not in the game until the
        // next install. Saying so is the difference between a fix that works and a user who tries
        // again and reports that AIM's fix did nothing.
        //
        // Forced rather than derived, and set after the refresh rather than before it.
        // RefreshArchiveStatus compares mod ids and versions against what the archive records, and
        // an edit inside a mod changes neither - so left to itself it would conclude, correctly by
        // its own lights and uselessly by ours, that nothing needs rebuilding.
        RefreshArchiveStatus();
        InstallationNeedsRebuild = true;
        InstallModsCommand.NotifyCanExecuteChanged();

        return outcome;
    }

    /// <summary>Puts back every file AIM set aside in a mod, and clears the row's marker.</summary>
    private async Task<EditOutcome> PutBackModFiles(string modId)
    {
        var store = EditStore;
        if (store is null) return EditOutcome.Refused(Texts.GUICrashNoModsFolder);

        var row = Mods.FirstOrDefault(model =>
            string.Equals(model.Mod.GetId(), modId, StringComparison.OrdinalIgnoreCase));

        if (row is null) return EditOutcome.Refused(Texts.GUICrashModGone);

        var outcome = await Task.Run(() => ModFileEditor.PutBack(row.Mod, store));

        if (outcome.Applied) RefreshEditedState(row);

        return outcome;
    }

    /// <summary>
    /// The copies of a mod AIM can put back, newest first, with the one taken immediately before
    /// AIM's own edit called out by name.
    ///
    /// "Restore the version from before you changed it" is what somebody undoing a fix means, and a
    /// list of timestamps does not answer it - especially not on a mod that has also been updated
    /// twice since.
    /// </summary>
    private IReadOnlyList<VersionChoice> VersionsFor(string modId)
    {
        var backups = BackupStore;

        var row = Mods.FirstOrDefault(model =>
            string.Equals(model.Mod.GetId(), modId, StringComparison.OrdinalIgnoreCase));

        if (backups is null || row is null) return [];

        var beforeEdits = (EditStore?.Edits(modId) ?? [])
            .Select(edit => edit.BackupPath)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return backups.List(ModBackupStore.ModNameFor(row.Mod.GetSourcePath()))
            .Select(backup =>
            {
                var preEdit = beforeEdits.Contains(backup.Path);

                return new VersionChoice(
                    preEdit
                        ? string.Format(Texts.GUICrashVersionBeforeEdit, backup.Describe())
                        : backup.Describe(),
                    backup,
                    preEdit);
            })
            .ToList();
    }

    private async Task<bool> RestoreVersionFor(string modId, VersionChoice choice)
    {
        var row = Mods.FirstOrDefault(model =>
            string.Equals(model.Mod.GetId(), modId, StringComparison.OrdinalIgnoreCase));

        return row is not null && await RestoreBackup(row, choice.Backup);
    }

    /// <summary>
    /// Scans for everything the issue report shows and prepares the answers its buttons need.
    ///
    /// Run once to open the report and again on every Refresh, so it has to be safe to repeat:
    /// nothing here touches the mod list, and the dismissal store is reused rather than rebuilt.
    /// </summary>
    private async Task<LoadOrderResultWindow.ReportContent> BuildIssueReportAsync()
    {
        var current = Mods.Select(model => model.Mod).ToList();
        var enabled = Mods.Where(model => model.Enabled).Select(model => model.Mod).ToList();

        // Rank shared-file conflicts against the order the user actually has, not the order the
        // planner would suggest. The report does not apply that suggestion, so naming its
        // winner would label a mod that does not currently win - and leave "make this one win"
        // with nothing to do.
        var plan = await Task.Run(() =>
            LoadOrderPlanner.Plan(current, enabled, rankConflictsBySuggestedOrder: false));
        var notes = plan.Notes
            .Where(note => note.Kind != LoadOrderNoteKind.DependencyMove)
            .Concat(await Task.Run(() => BuildDetailedSelectionNotes(enabled)))
            .ToList();

        var dismissed = await GetDismissedIssuesAsync();

        // "Nothing to report" has to mean nothing the user still cares about, or dismissing
        // the last issue would leave an empty window with no explanation in it.
        var outstanding = dismissed is null
            ? notes.Count
            : notes.Count(note => !dismissed.IsDismissed(note.StableKey));
        var summary = outstanding == 0 ? Texts.GUIConflictReportNothing : string.Empty;

        // Everything the report's buttons need to answer instantly. The mod list is snapshotted
        // here, on the UI thread, so the background scan never walks the live collection.
        var bySource = Mods
            .GroupBy(model => model.Mod.GetSourcePath(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Mod, StringComparer.OrdinalIgnoreCase);
        await Task.Run(() => PrepareRebindOptions(notes, enabled, bySource));

        return new LoadOrderResultWindow.ReportContent(summary, notes);
    }

    // Issues the user has already looked at and accepted. The store is per mods folder, so two
    // profiles pointing at different folders keep their own judgements - and it is held here
    // rather than rebuilt per scan so that a Refresh does not forget what was just ticked off.
    private DismissedIssueStore? _dismissedIssues;

    private async Task<DismissedIssueStore?> GetDismissedIssuesAsync()
    {
        if (string.IsNullOrEmpty(ModsLocation)) return null;
        if (_dismissedIssues is not null) return _dismissedIssues;

        var location = ModsLocation;
        return _dismissedIssues = await Task.Run(() =>
        {
            var store = new DismissedIssueStore(location);
            store.PruneOlderThan(TimeSpan.FromDays(365));
            return store;
        });
    }

    // ── Release notes ─────────────────────────────────────────────────────────────

    private ChangelogStore? _changelogStore;

    private ChangelogStore? ChangelogCache
    {
        get
        {
            if (string.IsNullOrEmpty(ModsLocation)) return null;
            return _changelogStore ??= new ChangelogStore(ModsLocation);
        }
    }

    /// <summary>
    /// Fetches a mod's release notes, from the cache when it can and from Nexus when it must.
    ///
    /// Returns null when there is no Nexus identity, no API key, or the call failed - all of which
    /// the caller shows as "nothing to read" rather than as an error, because a missing changelog
    /// is the author's choice as often as it is a fault.
    /// </summary>
    private async Task<List<ModChangelogEntry>?> FetchChangelogAsync(ModModel model)
    {
        var record = UpdateService?.Index.Get(model.Mod.GetSourcePath());
        if (record is null) return null;

        var cache = ChangelogCache;
        var cached = cache?.Get(record.ModId, record.Version);
        if (cached is not null) return cached;

        var client = Nexus is null ? null : await Nexus.CreateApiClientAsync();
        if (client is null) return null;

        try
        {
            var entries = await client.GetChangelogsAsync(record.Game, record.ModId);

            // Cached even when empty: a mod whose author writes no notes should be asked about
            // once, not on every hover for the rest of the week.
            cache?.Put(record.ModId, record.Version, entries);
            return entries;
        }
        catch (Exception exception)
        {
            Logger.Log($"Could not read the changelog for {model.Mod.GetName()}: {exception.Message}");
            return null;
        }
    }

    /// <summary>
    /// Fills in the hover tooltip with the newest version's notes.
    ///
    /// Triggered by the pointer entering the icon rather than by the list loading: doing this for
    /// every mod up front would be one Nexus call per mod on every launch, which no rate limit
    /// survives.
    /// </summary>
    private async Task LoadChangelogPreview(ModModel? model)
    {
        if (model is null || model.ChangelogRequested) return;
        model.ChangelogRequested = true;

        var entries = await FetchChangelogAsync(model);

        model.ChangelogPreview = entries is null || entries.Count == 0
            ? Texts.GUIChangelogNone
            : string.Format(Texts.GUIChangelogPreview, entries[0].Version, entries[0].Text);
    }

    /// <summary>Opens the full history: every version the author wrote notes for, newest first.</summary>
    private async Task ShowChangelog(ModModel? model)
    {
        if (model is null || App.TopLevel is not Window owner) return;

        var entries = await FetchChangelogAsync(model);
        await ChangelogWindow.ShowAsync(owner, model.Mod.GetName(), entries);
    }

    // ── Keybinds ──────────────────────────────────────────────────────────────────

    private BindingVault? _bindingVault;

    private BindingVault? BindingVault
    {
        get
        {
            if (string.IsNullOrEmpty(ModsLocation)) return null;
            return _bindingVault ??= new BindingVault(ModsLocation);
        }
    }

    [RelayCommand]
    private async Task ShowKeybinds()
    {
        if (App.TopLevel is not Window owner) return;

        var mods = Mods.Where(model => model.Enabled).Select(model => model.Mod).ToList();
        var store = await Task.Run(ModDataStore.Locate);

        await KeybindManagerWindow.ShowAsync(owner, mods, store, BindingVault);
    }

    /// <summary>
    /// Notices when a mod has changed a binding the user chose, and offers to put it back.
    ///
    /// This is the whole point of the vault. Mod settings live outside the mods folder, so an
    /// update does not lose a binding by itself - but a mod that bumps its config version and
    /// migrates to defaults does, silently. A setting that no longer exists is never offered:
    /// the feature it belonged to is gone, and rebinding a key to nothing helps nobody.
    /// </summary>
    private async Task OfferBindingRestore()
    {
        // A bulk update reloads the list once per mod it replaces, and each reload lands here. One
        // modal question stacked on top of a run of downloads is not a question anybody can answer.
        if (_bulkUpdating) return;

        var vault = BindingVault;
        if (vault is null || vault.Count == 0) return;

        var mods = Mods.Where(model => model.Enabled).Select(model => model.Mod).ToList();
        if (mods.Count == 0) return;

        var drift = await Task.Run(() =>
        {
            var store = ModDataStore.Locate();
            return store is null
                ? new List<BindingDrift>()
                : vault.FindDrift(BindingScanner.Scan(mods, store));
        });

        if (drift.Count == 0) return;

        var summary = string.Join("\r\n", drift
            .Take(8)
            .Select(item => $"• {item.ModName} — {item.Field}: {item.Now} → {item.Remembered}"));
        if (drift.Count > 8)
            summary += "\r\n" + string.Format(Texts.GUIBindingRestoreMore, drift.Count - 8);

        var confirm = await MessageBoxManager.GetMessageBoxStandard(
            Texts.GUIBindingRestoreTitle,
            string.Format(Texts.GUIBindingRestorePrompt, drift.Count, summary),
            ButtonEnum.YesNo).ShowAsync();
        if (confirm != ButtonResult.Yes) return;

        var restored = await Task.Run(() => drift.Count(vault.Restore));

        InstallStatus = string.Format(Texts.GUIBindingRestoreDone, restored);
    }

    // ── Acting on a reported issue ────────────────────────────────────────────────

    private ConflictReportActions? _conflictActions;

    private ConflictReportActions ConflictActions => _conflictActions ??= new ConflictReportActions(
        MakeModWin,
        ResearchConflict,
        InspectRebind,
        RebindHotkey);

    /// <summary>
    /// The row for a mod named in an issue.
    ///
    /// Matched on source path rather than id: a folder copy and an archive copy of the same mod
    /// share an id, and acting on the wrong one would silently reorder something the user was not
    /// looking at.
    /// </summary>
    private ModModel? RowFor(IssueParticipant participant) =>
        Mods.FirstOrDefault(model => string.Equals(
            model.Mod.GetSourcePath(), participant.SourcePath, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Moves a mod below every other mod in its conflict, which is what decides who wins: the
    /// installer writes shared files in load order, so the last one to write is the one that stays.
    /// </summary>
    private bool MakeModWin(LoadOrderNote note, IssueParticipant participant)
    {
        var winner = RowFor(participant);
        if (winner is null) return false;

        var others = note.Participants
            .Where(other => !ReferenceEquals(other, participant))
            .Select(RowFor)
            .OfType<ModModel>()
            .ToList();
        if (others.Count == 0) return false;

        var lowest = others.OrderByDescending(Mods.IndexOf).First();
        if (Mods.IndexOf(winner) > Mods.IndexOf(lowest)) return true;

        MoveMod(winner, lowest, insertBeforeTarget: false);
        RefreshPositions();
        RefreshSelectedModConflicts();
        return true;
    }

    /// <summary>
    /// Opens the research window for one conflict, with whatever AIM knows about where each mod
    /// came from so the shortcuts point at the right pages.
    /// </summary>
    private async Task<IssueVerdict?> ResearchConflict(Window owner, LoadOrderNote note)
    {
        var index = UpdateService?.Index;
        var subjects = note.Participants
            .Select(participant =>
            {
                var record = index?.Get(participant.SourcePath);
                return new ResearchSubject(participant.Name, record?.ModId, record?.PageUrl);
            })
            .ToList();

        var client = Nexus is null ? null : await Nexus.CreateApiClientAsync();

        return await ConflictResearchWindow.ShowAsync(
            owner, note.Message, subjects, client, BuildResearchContext(note));
    }

    /// <summary>
    /// What the research window needs to diagnose this conflict from the files and act on it.
    ///
    /// Null for issues that are not about files - a hotkey clash or a missing requirement has
    /// nothing to read - in which case the window falls back to reading the mod pages, which is
    /// what it always did.
    /// </summary>
    private ResearchContext? BuildResearchContext(LoadOrderNote note)
    {
        if (note.Kind != LoadOrderNoteKind.FileConflict || note.Details.Count == 0) return null;

        // In load order, so the diagnosis can say which mod wins as things stand rather than
        // recomputing it. RowFor matches on source path for the same reason it does elsewhere: two
        // copies of one mod share an id, and acting on the wrong one is silent and confusing.
        var rows = note.Participants.Select(RowFor).OfType<ModModel>().ToList();
        if (rows.Count < 2) return null;

        return new ResearchContext(
            rows.Select(row => row.Mod).ToList(),
            note.Details,
            winnerId => MakeModWinById(note, winnerId),
            OfferPatchDownload,
            BackupStore is null ? null : (modId, paths, reason) => SetAsideModFiles(modId, paths, reason))
        {
            Installed = SnapshotForResearch(),
            Enable = EnableModById,
            RemoveMod = RemoveModById
        };
    }

    /// <summary>
    /// Removes a mod by id on the research window's behalf, reporting whether it actually went.
    ///
    /// It goes through <see cref="RemoveMod(ModModel?)"/> rather than reimplementing removal, so
    /// the confirmation, the recycle bin and the Nexus index cleanup are the same ones the mod
    /// row's own menu uses. The answer is read off the disk rather than off the list, because the
    /// list is rebuilt by the removal and a row that has been replaced looks the same as one that
    /// has been deleted.
    /// </summary>
    private async Task<bool> RemoveModById(string modId)
    {
        var row = Mods.FirstOrDefault(model =>
            string.Equals(model.Mod.GetId(), modId, StringComparison.OrdinalIgnoreCase));

        if (row is null) return false;

        var source = row.Mod.GetSourcePath();
        await RemoveMod(row);

        return !string.IsNullOrEmpty(source) && !Directory.Exists(source) && !File.Exists(source);
    }

    /// <summary>
    /// The whole mod list as the research window needs to see it: in load order, with whatever AIM
    /// knows about which Nexus mod and file each one came from.
    ///
    /// The window uses this to check whether the user already has the patch it is about to offer.
    /// Nexus provenance is looked up per row rather than assumed: a mod installed by hand has none,
    /// and is still perfectly capable of being the patch - it just has to be recognised by name.
    /// </summary>
    private IReadOnlyList<InstalledModView> SnapshotForResearch()
    {
        var index = UpdateService?.Index;

        return Mods
            .Select((row, position) =>
            {
                var record = index?.Get(row.Mod.GetSourcePath());

                return new InstalledModView(row.Mod, position, row.Enabled)
                {
                    NexusModId = record?.ModId,
                    NexusFileId = record?.FileId,
                    PageUrl = row.NexusPageUrl ?? record?.PageUrl
                };
            })
            .ToList();
    }

    /// <summary>
    /// Moves a mod below every mod in the conflict.
    ///
    /// The id is usually one of the conflict's own participants, and that path is kept because it
    /// reuses the participant's source path and so picks the right row when two copies of a mod
    /// share an id. But a compatibility patch the user already has is not a participant - it is a
    /// third mod that has to end up below both - so an id that names no participant falls through
    /// to the mod list itself.
    /// </summary>
    private bool MakeModWinById(LoadOrderNote note, string modId)
    {
        var participant = note.Participants.FirstOrDefault(entry =>
            string.Equals(entry.ModId, modId, StringComparison.OrdinalIgnoreCase));

        if (participant is not null) return MakeModWin(note, participant);

        var mover = Mods.FirstOrDefault(model =>
            string.Equals(model.Mod.GetId(), modId, StringComparison.OrdinalIgnoreCase));

        var others = note.Participants.Select(RowFor).OfType<ModModel>().ToList();
        if (mover is null || others.Count == 0) return false;

        var lowest = others.OrderByDescending(Mods.IndexOf).First();
        if (Mods.IndexOf(mover) > Mods.IndexOf(lowest)) return true;

        MoveMod(mover, lowest, insertBeforeTarget: false);
        RefreshPositions();
        RefreshSelectedModConflicts();
        return true;
    }

    /// <summary>
    /// Switches a mod on by id, for the research window: a patch the user installed and then
    /// disabled is, to the game, a patch they do not have.
    /// </summary>
    private bool EnableModById(string modId)
    {
        var row = Mods.FirstOrDefault(model =>
            string.Equals(model.Mod.GetId(), modId, StringComparison.OrdinalIgnoreCase));

        if (row is null || row.InError) return false;

        row.Enabled = true;
        RefreshSelectedModConflicts();

        // Same reason as DisableModById: the archive on disk no longer matches the selection, and
        // the crash check reads exactly that to decide whether a rebuild is needed before the next
        // run. A cleared mod switched back on must not be missing from the game it is tested in.
        RefreshArchiveStatus();

        // And the tick has to reach the profile, for the reason DisableModById gives - with the
        // sharper edge here, because the disable half *was* saved: the install that rebuilt the
        // archive wrote the profile with this mod switched off. Leaving the re-enable in memory
        // means the next reload puts it back off, and the user who has just been told the mod is
        // ruled out has to go and tick it themselves.
        SaveCurrentProfileState();
        return true;
    }

    /// <summary>
    /// Installs a compatibility patch the researcher found.
    ///
    /// It tries to download it outright first. Nexus issues direct download links to Premium
    /// accounts only, so for a Premium user this finishes here - the patch lands in the downloads
    /// list, unpacks, and is registered for update checks like any other mod. Only when Nexus
    /// actually refuses to mint the link does AIM fall back to opening the mod page, where one
    /// click on "Mod Manager Download" fires the nxm:// handler AIM already owns and hands the file
    /// straight back.
    ///
    /// The fallback is chosen on Nexus's own refusal rather than on AIM checking the account tier.
    /// That matters: a Premium user whose key has been revoked hits the same wall, and telling them
    /// to go and buy what they already own would send them looking in exactly the wrong place.
    /// </summary>
    private async Task<string?> OfferPatchDownload(PatchCandidate patch)
    {
        if (Nexus is null)
            return "AIM's Nexus integration is not set up, so it cannot install this for you.";

        var outcome = await Nexus.InstallModAsync(patch.ModId, patch.Title, patch.FileId);

        if (outcome.Installed)
        {
            // The new mod has to appear in the list before the user can order it against the two it
            // was installed to reconcile.
            UpdateModlist(true);
            return null;
        }

        if (!outcome.NeedsWebsite) return outcome.Message;

        if (!ExternalUrl.IsAllowed(patch.Url))
            return "That patch's page is not a link AIM will open.";

        NexusDownloadsViewModel.OpenUrl($"{patch.Url}?tab=files");

        return "Nexus will not issue AIM a direct download link for this account, so the patch's " +
               "files are now open in your browser. Click \"Mod Manager Download\" on the one you " +
               "want and AIM will install it from there.";
    }

    /// <summary>
    /// Takes one mod's copy of the contested files out of play, then marks the row.
    ///
    /// The row is refreshed rather than the whole list reloaded: a reload would re-run the conflict
    /// scan under the report window that is still open, and the user has not finished with it.
    /// </summary>
    private async Task<EditOutcome> SetAsideModFiles(
        string modId, IReadOnlyList<string> paths, string reason)
    {
        var backups = BackupStore;
        if (backups is null || string.IsNullOrEmpty(ModsLocation))
            return EditOutcome.Refused("AIM does not know where your mods folder is.");

        var row = Mods.FirstOrDefault(model =>
            string.Equals(model.Mod.GetId(), modId, StringComparison.OrdinalIgnoreCase));

        if (row is null) return EditOutcome.Refused("That mod is no longer in the list.");

        var store = EditStore;
        if (store is null) return EditOutcome.Refused("AIM does not know where your mods folder is.");

        var outcome = await Task.Run(() =>
            ModFileEditor.SetAside(row.Mod, paths, reason, backups, store));

        if (outcome.Applied) RefreshEditedState(row);

        return outcome;
    }

    private AppliedEditStore? _editStore;

    private AppliedEditStore? EditStore =>
        string.IsNullOrEmpty(ModsLocation) ? null : _editStore ??= new AppliedEditStore(ModsLocation);

    /// <summary>
    /// Puts the "edited by AIM" marker on a row, and refreshes its version dropdown so the copy
    /// taken before the edit is immediately there to roll back to.
    /// </summary>
    private void RefreshEditedState(ModModel model)
    {
        var store = EditStore;

        model.WasEditedByAim = store?.WasEdited(model.Mod.GetId()) ?? false;
        model.AimEditSummary = store?.DescribeEdits(model.Mod.GetId()) ?? "";

        var archived = BackupStore?.List(ModBackupStore.ModNameFor(model.Mod.GetSourcePath())) ?? [];
        model.SetBackups(archived.Select(backup =>
            new ModBackupChoice(model, backup, RestoreChosenVersionCommand)));
    }

    // ── Rebinding a shortcut ──────────────────────────────────────────────────────

    // Working out whether a mod's shortcut can be moved means reading every .gml it ships, and
    // working out where to move it means reading every .gml of every enabled mod. The report window
    // asks for both while it is laying out rows, and re-lays out on every checkbox tick - so the
    // answers are computed once, off the UI thread, before the window opens.
    private readonly Dictionary<string, RebindCapability> _rebindCapabilities = new(StringComparer.Ordinal);
    private List<string> _freeHotkeys = [];

    private static string RebindCacheKey(LoadOrderNote note, IssueParticipant participant) =>
        $"{note.HotkeyKey}|{participant.SourcePath}";

    /// <summary>
    /// Reads what every hotkey conflict in the report would need to know, so the window itself can
    /// answer instantly.
    /// </summary>
    /// <param name="bySource">
    /// The mods keyed by source path, taken on the UI thread by the caller. This runs on a
    /// background thread and must not walk <see cref="Mods"/> itself.
    /// </param>
    private void PrepareRebindOptions(
        IReadOnlyList<LoadOrderNote> notes,
        IReadOnlyList<IMod> enabled,
        IReadOnlyDictionary<string, IMod> bySource)
    {
        _rebindCapabilities.Clear();
        _freeHotkeys = HotkeyRebinder.FreeKeys(enabled).ToList();

        foreach (var note in notes.Where(note => note.HotkeyKey is not null))
        foreach (var participant in note.Participants)
        {
            if (!bySource.TryGetValue(participant.SourcePath, out var mod)) continue;

            var capability = HotkeyRebinder.Inspect(mod, note.HotkeyKey!);

            // Offering a rebind that can only swap one clash for another is worse than not
            // offering it at all.
            if (capability.CanRebind && _freeHotkeys.Count == 0)
                capability = new RebindCapability(RebindBlocker.NoFreeKeys, capability.Bindings);

            _rebindCapabilities[RebindCacheKey(note, participant)] = capability;
        }
    }

    private RebindCapability InspectRebind(LoadOrderNote note, IssueParticipant participant) =>
        _rebindCapabilities.TryGetValue(RebindCacheKey(note, participant), out var capability)
            ? capability
            : new RebindCapability(RebindBlocker.NotADeclaredBinding, []);

    /// <summary>
    /// Moves one mod off a contested shortcut by rewriting its own binding.
    ///
    /// The user is told exactly which key it is moving to and that the change only reaches the game
    /// on the next install - GML is compiled into the rebuilt archive, so an edit that is not
    /// installed changes nothing in play.
    /// </summary>
    private async Task<string?> RebindHotkey(LoadOrderNote note, IssueParticipant participant)
    {
        var row = RowFor(participant);
        var key = note.HotkeyKey;
        if (row is null || key is null) return null;

        var free = _freeHotkeys;
        if (free.Count == 0)
        {
            await MessageBoxManager.GetMessageBoxStandard(
                Texts.GUIHotkeyRebindTitle, Texts.GUIHotkeyBlockedNoFreeKeys, ButtonEnum.Ok).ShowAsync();
            return null;
        }

        var target = free[0];
        var confirm = await MessageBoxManager.GetMessageBoxStandard(
            Texts.GUIHotkeyRebindTitle,
            string.Format(Texts.GUIHotkeyRebindConfirm, participant.Display, key, target),
            ButtonEnum.YesNo).ShowAsync();
        if (confirm != ButtonResult.Yes) return null;

        var backups = BackupStore;
        int changed;
        try
        {
            changed = await Task.Run(() => HotkeyRebinder.Rebind(row.Mod, key, target, backups));
        }
        catch (Exception exception)
        {
            Logger.Log($"Rebinding {participant.Display} failed: {exception}");
            await MessageBoxManager.GetMessageBoxStandard(
                Texts.GUIHotkeyRebindTitle,
                string.Format(Texts.GUIHotkeyRebindFailed, participant.Display, exception.Message),
                ButtonEnum.Ok).ShowAsync();
            return null;
        }

        if (changed == 0)
        {
            await MessageBoxManager.GetMessageBoxStandard(
                Texts.GUIHotkeyRebindTitle,
                string.Format(Texts.GUIHotkeyRebindFailed, participant.Display, Texts.GUIHotkeyBlockedNotDeclared),
                ButtonEnum.Ok).ShowAsync();
            return null;
        }

        // The mod now holds a different key, and the one it left is free again. Anything the window
        // asks about after this must reflect that, so the cached answers are rebuilt.
        _freeHotkeys.Remove(target);
        _rebindCapabilities.Remove(RebindCacheKey(note, participant));

        // Taking the last free key means every other rebind button in the report is now a dead end.
        // Grey them out rather than letting the user click one and meet a refusal.
        if (_freeHotkeys.Count == 0)
        {
            foreach (var cached in _rebindCapabilities.Where(entry => entry.Value.CanRebind).ToList())
                _rebindCapabilities[cached.Key] =
                    new RebindCapability(RebindBlocker.NoFreeKeys, cached.Value.Bindings);
        }

        await MessageBoxManager.GetMessageBoxStandard(
            Texts.GUIHotkeyRebindTitle,
            string.Format(Texts.GUIHotkeyRebindDone, participant.Display, target, changed),
            ButtonEnum.Ok).ShowAsync();

        return target;
    }

    // ── Nexus: per-mod actions ────────────────────────────────────────────────────

    private void OpenNexusPage(ModModel? model)
    {
        if (model?.NexusPageUrl is null) return;
        NexusDownloadsViewModel.OpenUrl(model.NexusPageUrl);
    }

    /// <summary>
    /// Adds the mod to the user's tracking centre on Nexus.
    ///
    /// AIM's own update check already tells the user when a tracked mod moves, so this is not about
    /// AIM knowing - it is about the site knowing. Tracking is what puts a mod on the user's Nexus
    /// notifications and on the tracking centre they read on their phone, and doing it from here
    /// saves opening the page, waiting for it to load, and finding the button.
    ///
    /// Needs a connected account, because tracking is something done *as* somebody.
    /// </summary>
    private async Task TrackOnNexus(ModModel? model)
    {
        if (model?.NexusPageUrl is null) return;

        if (!NexusInstallIndex.TryReadNexusUrl(model.NexusPageUrl, out var game, out var modId))
        {
            InstallStatus = string.Format(Texts.GUINexusTrackFailed, model.Mod.GetName(),
                Texts.GUINexusTrackNoModId);
            return;
        }

        var client = Nexus is null ? null : await Nexus.CreateApiClientAsync();
        if (client is null)
        {
            InstallStatus = string.Format(Texts.GUINexusTrackFailed, model.Mod.GetName(),
                Texts.GUINexusAccountNoKey);
            return;
        }

        try
        {
            var added = await client.TrackModAsync(game, modId);

            // "Already tracking it" is worth saying rather than hiding behind a generic success:
            // it answers the question the user was really asking, which is whether they are
            // covered, and stops them wondering whether the click did anything.
            InstallStatus = string.Format(
                added ? Texts.GUINexusTracked : Texts.GUINexusAlreadyTracked, model.Mod.GetName());
        }
        catch (Exception exception)
        {
            Logger.Log($"Tracking {model.Mod.GetName()} on Nexus failed: {exception}");
            InstallStatus = string.Format(Texts.GUINexusTrackFailed, model.Mod.GetName(), exception.Message);
        }
    }

    private async Task AssociateWithNexus(ModModel? model)
    {
        if (model is null || UpdateService is null || Nexus is null || App.TopLevel is not Window owner) return;

        var input = await NexusAssociationWindow.ShowAsync(owner);
        if (string.IsNullOrWhiteSpace(input)) return;

        string game;
        int modId;
        var fileId = 0;

        if (NxmLink.TryParse(input, out var nxm, out var linkError))
        {
            if (!nxm!.IsForMistria())
            {
                await MessageBoxManager.GetMessageBoxStandard(Texts.GUINexusAssociateTitle,
                    Texts.GUINexusAssociateWrongGame, ButtonEnum.Ok).ShowAsync();
                return;
            }

            // An NXM link is an actionable download, not merely a page reference. Use the same
            // path as the Nexus website handler so its temporary key/expires pair is honoured,
            // and let the normal overwrite prompt and provenance recording run as usual.
            if (await Nexus.HandleAssociatedLinkAsync(
                    input, model.Mod.GetSourcePath(), model.Mod.GetVersion()))
                model.NexusPageUrl = $"https://www.nexusmods.com/{nxm.Game}/mods/{nxm.ModId}";
            return;
        }
        else if (!NexusInstallIndex.TryReadNexusUrl(input, out game, out modId))
        {
            await MessageBoxManager.GetMessageBoxStandard(Texts.GUINexusAssociateTitle,
                string.Format(Texts.GUINexusAssociateInvalid, linkError ?? ""), ButtonEnum.Ok).ShowAsync();
            return;
        }

        UpdateService.Index.Record(model.Mod.GetSourcePath(), new NexusInstallRecord(
            game, modId, fileId, "", model.Mod.GetVersion(), DateTimeOffset.UtcNow));
        model.NexusPageUrl = $"https://www.nexusmods.com/{game}/mods/{modId}";
        await CheckModForUpdate(model);
    }

    private void OpenModFolder(ModModel? model)
    {
        var path = model?.Mod.GetSourcePath();
        if (string.IsNullOrEmpty(path)) return;

        var folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;

        try
        {
            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
        }
        catch (Exception e)
        {
            Logger.Log($"Could not open {folder}: {e.Message}");
        }
    }

    // ── Editing a mod's own files ─────────────────────────────────────────────────

    private void EditModManifest(ModModel? model)
        => OpenInTextEditor(ModEditableFiles.FindManifest(model?.Mod));

    private void EditModConfig(ModModel? model)
        => OpenInTextEditor(ModEditableFiles.FindConfig(model?.Mod));

    /// <summary>
    /// Hands a file to whatever the user has associated with it.
    ///
    /// .json and .toml frequently have no association at all on a fresh Windows install, and
    /// ShellExecute answers that with an exception rather than the "open with" dialog. Falling back
    /// to Notepad is better than a menu item that appears to do nothing.
    /// </summary>
    private void OpenInTextEditor(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            return;
        }
        catch (Exception e)
        {
            Logger.Log($"No default editor for {path}: {e.Message}");
        }

        if (!OperatingSystem.IsWindows()) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "notepad.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true
            });
        }
        catch (Exception e)
        {
            Logger.Log($"Could not open {path} in Notepad: {e.Message}");
            Exception = string.Format(Texts.GUIEditFileFailed, path, e.Message);
        }
    }

    // ── Removing a mod ────────────────────────────────────────────────────────────

    /// <summary>
    /// Takes a mod out of the mods folder entirely.
    ///
    /// The files go to the Recycle Bin rather than being erased: this is the only place AIM throws
    /// away something the user may have edited by hand, and a mis-click on the wrong row should be
    /// survivable. Where no recycle bin exists the confirmation says so plainly instead.
    /// </summary>
    private async Task RemoveMod(ModModel? model)
    {
        if (model is null) return;

        var source = model.Mod.GetSourcePath();
        if (string.IsNullOrEmpty(source) || (!Directory.Exists(source) && !File.Exists(source)))
        {
            await MessageBoxManager.GetMessageBoxStandard(
                Texts.GUIRemoveModTitle,
                string.Format(Texts.GUIRemoveModMissing, model.Mod.GetName()),
                ButtonEnum.Ok).ShowAsync();
            UpdateModlist(true);
            return;
        }

        var prompt = RecycleBin.IsSupported
            ? string.Format(Texts.GUIRemoveModConfirm, model.Mod.GetName(), model.Mod.GetVersion(), source)
            : string.Format(Texts.GUIRemoveModConfirmPermanent, model.Mod.GetName(), model.Mod.GetVersion(), source);

        var confirm = await MessageBoxManager
            .GetMessageBoxStandard(Texts.GUIRemoveModTitle, prompt, ButtonEnum.YesNo)
            .ShowAsync();
        if (confirm != ButtonResult.Yes) return;

        model.ContextActionsLocked = true;
        try
        {
            // A zipped mod is read through an open handle on its archive, and the shell will not
            // recycle a file this process still has open.
            (model.Mod as IDisposable)?.Dispose();

            var removed = await Task.Run(() => RemoveFromDisk(source));
            if (!removed)
            {
                await MessageBoxManager.GetMessageBoxStandard(
                    Texts.GUIRemoveModTitle,
                    string.Format(Texts.GUIRemoveModFailed, model.Mod.GetName(), Texts.GUIRemoveModRefused),
                    ButtonEnum.Ok).ShowAsync();
                return;
            }

            // The Nexus index would otherwise keep offering updates for a mod that is no longer
            // here, and would silently re-adopt an unrelated mod that later takes the same folder
            // name.
            UpdateService?.Index.Forget(source);
            model.Enabled = false;
        }
        catch (Exception e)
        {
            Logger.Log($"Removing {source} failed: {e}");
            await MessageBoxManager.GetMessageBoxStandard(
                Texts.GUIRemoveModTitle,
                string.Format(Texts.GUIRemoveModFailed, model.Mod.GetName(), e.Message),
                ButtonEnum.Ok).ShowAsync();
        }
        finally
        {
            model.ContextActionsLocked = false;
            UpdateModlist(true);
        }
    }

    /// <summary>
    /// Removes every ticked mod in one go.
    ///
    /// One confirmation naming all of them, rather than one per mod: a user clearing out a dozen
    /// mods should not have to answer the same question twice, and a list they can read before
    /// agreeing is a better safeguard than repetition.
    /// </summary>
    [RelayCommand]
    private async Task RemoveSelectedMods()
    {
        // A mod with no source path on disk has nothing to remove, so it is excluded here rather
        // than skipped in the loop - otherwise it would still count towards the "removed 3 of 5"
        // total and look like a silent failure.
        var selected = Mods
            .Where(model => model.Enabled && !string.IsNullOrEmpty(model.Mod.GetSourcePath()))
            .ToList();

        if (selected.Count == 0)
        {
            await MessageBoxManager.GetMessageBoxStandard(
                Texts.GUIRemoveModTitle, Texts.GUIRemoveSelectedNone, ButtonEnum.Ok).ShowAsync();
            return;
        }

        var names = string.Join("\r\n", selected.Take(15).Select(model => $"• {model.Mod.GetName()}"));
        if (selected.Count > 15)
            names += "\r\n" + string.Format(Texts.GUIRemoveSelectedMore, selected.Count - 15);

        var prompt = RecycleBin.IsSupported
            ? string.Format(Texts.GUIRemoveSelectedConfirm, selected.Count, names)
            : string.Format(Texts.GUIRemoveSelectedConfirmPermanent, selected.Count, names);

        var confirm = await MessageBoxManager
            .GetMessageBoxStandard(Texts.GUIRemoveModTitle, prompt, ButtonEnum.YesNo)
            .ShowAsync();
        if (confirm != ButtonResult.Yes) return;

        var removed = 0;
        var failed = new List<string>();

        foreach (var model in selected)
        {
            var source = model.Mod.GetSourcePath();

            try
            {
                (model.Mod as IDisposable)?.Dispose();

                if (await Task.Run(() => RemoveFromDisk(source)))
                {
                    UpdateService?.Index.Forget(source);
                    removed++;
                }
                else failed.Add(model.Mod.GetName());
            }
            catch (Exception exception)
            {
                Logger.Log($"Removing {source} failed: {exception}");
                failed.Add(model.Mod.GetName());
            }
        }

        UpdateModlist(true);

        var report = string.Format(Texts.GUIRemoveSelectedDone, removed, selected.Count);
        if (failed.Count > 0)
            report += "\r\n\r\n" + string.Format(Texts.GUIRemoveSelectedFailed, string.Join(", ", failed));

        await MessageBoxManager.GetMessageBoxStandard(
            Texts.GUIRemoveModTitle, report, ButtonEnum.Ok).ShowAsync();
    }

    private static bool RemoveFromDisk(string source)
    {
        if (RecycleBin.TryDelete(source)) return true;

        // No recycle bin on this platform - the user was told the delete is permanent, so honour
        // it. On Windows a failed recycle is reported rather than escalated to an erase.
        if (RecycleBin.IsSupported) return false;

        if (Directory.Exists(source)) Directory.Delete(source, true);
        else if (File.Exists(source)) File.Delete(source);

        return !Directory.Exists(source) && !File.Exists(source);
    }

    private void ToggleModFreeze(ModModel? model)
    {
        if (model is null || UpdateService is null) return;

        var frozen = !model.IsFrozen;

        // No reason, which is how AIM records "the user decided this". It also clears a reason AIM
        // had recorded: a user who freezes a mod by hand has taken the decision over, and AIM
        // should stop offering the update it was going to offer about its own patch.
        UpdateService.SetFrozen(model.Mod, frozen);
        model.IsFrozen = frozen;
        model.FreezeReason = "";
        model.UpdateMayFixEdit = false;

        // A frozen mod's pending update badge is noise: the user has said they do not want it.
        if (!frozen) return;
        model.UpdateAvailable = false;
        model.UpdateFileId = null;
        RefreshPendingUpdates();
    }

    private async Task CheckModForUpdate(ModModel? model)
    {
        if (model is null || UpdateService is null) return;
        if (Nexus is not null && !await Nexus.EnsureNexusAccountAsync()) return;

        model.IsCheckingUpdate = true;
        try
        {
            var status = await Task.Run(() => UpdateService.CheckAsync(model.Mod));
            // A page-URL association starts with fileId=0. Once Nexus confirms that the
            // installed version is current, persist the exact file identity without downloading
            // the archive or asking the user to confirm a replacement.
            UpdateService.RecordCurrentFileIdentity(model.Mod, status);
            ApplyUpdateStatus(model, status);
            await ReportUpdateStatusAsync(model, status);
        }
        finally
        {
            model.IsCheckingUpdate = false;
        }
    }

    private async Task UpdateModFromNexus(ModModel? model)
    {
        if (model is null || Nexus is null || UpdateService is null) return;

        // The badge may be from a check made minutes ago; ask Nexus again so the file id being
        // installed is the one the page offers right now.
        var status = await Task.Run(() => UpdateService.CheckAsync(model.Mod));
        ApplyUpdateStatus(model, status);

        if (!status.HasUpdate)
        {
            await ReportUpdateStatusAsync(model, status);
            return;
        }

        var confirm = await MessageBoxManager.GetMessageBoxStandard(
            Texts.GUIUpdateModTitle,
            string.Format(Texts.GUIUpdateModConfirm, model.Mod.GetName(),
                model.Mod.GetVersion(), status.LatestVersion ?? "?"),
            ButtonEnum.YesNo).ShowAsync();

        if (confirm != ButtonResult.Yes) return;

        if (await Nexus.RunUpdateAsync(UpdateService, model.Mod, status, ModsLocation))
            UpdateModlist(true);
    }

    private async Task RestorePreviousVersion(ModModel? model)
    {
        if (model is null || BackupStore is null) return;

        var backups = BackupStore.List(ModBackupStore.ModNameFor(model.Mod.GetSourcePath()));
        if (backups.Count == 0)
        {
            await MessageBoxManager.GetMessageBoxStandard(
                Texts.GUIRestoreVersionTitle, Texts.GUIRestoreVersionNone, ButtonEnum.Ok).ShowAsync();
            return;
        }

        await RestoreBackup(model, backups[0]);
    }

    /// <summary>
    /// Rolls a mod back to a specific archived copy, chosen from the row's version dropdown.
    ///
    /// The right-click menu restores the newest backup, which is the common case but not the
    /// interesting one: someone rolling back is usually after the version from before the one that
    /// broke, and only they know which that is.
    /// </summary>
    [RelayCommand]
    private async Task RestoreChosenVersion(ModBackupChoice? choice)
    {
        if (choice is null) return;
        await RestoreBackup(choice.Mod, choice.Backup);
    }

    /// <summary>
    /// Rolls one mod back to an archived copy. False when nothing happened - because the user said
    /// no, or because it failed - which the crash window needs in order not to claim a restore that
    /// the user declined.
    /// </summary>
    private async Task<bool> RestoreBackup(ModModel model, ModBackup backup)
    {
        if (BackupStore is null) return false;

        var source = model.Mod.GetSourcePath();
        var modName = ModBackupStore.ModNameFor(source);
        var newest = backup;

        var confirm = await MessageBoxManager.GetMessageBoxStandard(
            Texts.GUIRestoreVersionTitle,
            string.Format(Texts.GUIRestoreVersionConfirm, model.Mod.GetName(), newest.Describe()),
            ButtonEnum.YesNo).ShowAsync();

        if (confirm != ButtonResult.Yes) return false;

        try
        {
            var destination = Directory.Exists(source)
                ? source
                : Path.Combine(ModsLocation, modName);

            BackupStore.Restore(newest, destination);

            // The index still names the file that was just rolled back, which would make the
            // restored version look up to date for ever. Dropping the record falls back to
            // comparing versions, which is right for a copy AIM did not install.
            UpdateService?.Index.Forget(destination);

            // The restored copy is the one from before AIM touched it, so the "edited by AIM"
            // marker would now be claiming something that is no longer true.
            EditStore?.Forget(model.Mod.GetId());

            UpdateModlist(true);
            return true;
        }
        catch (Exception e)
        {
            await MessageBoxManager.GetMessageBoxStandard(
                Texts.GUIRestoreVersionTitle, e.Message, ButtonEnum.Ok).ShowAsync();
            return false;
        }
    }

    // ── Nexus: bulk checks ────────────────────────────────────────────────────────

    [RelayCommand]
    private Task CheckSelectedModsForUpdates() =>
        // Switched-off mods are not the user's current game and are not worth a Nexus call - with
        // one exception. A mod that is off because it was caught crashing is the mod most likely to
        // be fixed by its next release, and skipping it would leave the user waiting for news about
        // the one mod they are actually waiting on.
        CheckManyForUpdates(Mods.Where(model => model.Enabled || model.IsKnownCrasher).ToList());

    [RelayCommand]
    private Task CheckAllModsForUpdates() => CheckManyForUpdates(Mods.ToList());

    private async Task CheckManyForUpdates(List<ModModel> models)
    {
        if (models.Count == 0 || UpdateService is null) return;
        if (Nexus is not null && !await Nexus.EnsureNexusAccountAsync()) return;

        // Frozen mods are skipped, with one exception: a mod AIM froze because it had patched it.
        // That freeze exists to protect the fix, not to end the conversation, and the user is owed
        // the news when a version arrives that might make the fix unnecessary.
        var checkable = models
            .Where(model => !model.IsFrozen || UpdateService.FreezeReason(model.Mod) is not null)
            .ToList();

        foreach (var model in checkable) model.IsCheckingUpdate = true;

        try
        {
            var progress = new Progress<(int Done, int Total)>(step =>
                InstallStatus = string.Format(Texts.GUICheckingForUpdates, step.Done, step.Total));

            var statuses = await Task.Run(() =>
                UpdateService.CheckManyAsync(checkable.Select(model => model.Mod).ToList(), progress));

            foreach (var model in checkable)
                if (statuses.TryGetValue(model.Mod.GetId(), out var status))
                {
                    // Same adoption the single-mod check does: a mod associated by page URL starts
                    // with fileId=0, and while it stays that way every check answers "AIM cannot
                    // tell" and parks the mod in the attention filter for good. Nexus has just
                    // confirmed the installed version is current, so record which file that is.
                    UpdateService.RecordCurrentFileIdentity(model.Mod, status);
                    ApplyUpdateStatus(model, status);
                }

            var withUpdates = checkable.Count(model => model.CanUpdateFromNexus);
            var unavailable = statuses.Values.Count(status => status.State == NexusUpdateState.Unavailable);

            InstallStatus = "";
            RefreshPendingUpdates();

            // Said first and on its own, because it is the news the user has been waiting for and
            // it needs a decision they have to understand: taking one of these updates gives up a
            // fix they watched AIM make. Folding it into "7 mods have updates" would bury that.
            await ReportUpdatesThatMayFixEditsAsync(checkable, statuses);

            // Finding updates and then making the user right-click each mod in turn is a chore AIM
            // can simply do for them, so the result offers the next step rather than only reporting.
            if (withUpdates > 0)
            {
                var updateNow = await MessageBoxManager.GetMessageBoxStandard(
                    Texts.GUICheckForUpdatesTitle,
                    string.Format(Texts.GUICheckForUpdatesResultWithOffer,
                        checkable.Count, withUpdates, unavailable),
                    ButtonEnum.YesNo).ShowAsync();

                if (updateNow == ButtonResult.Yes)
                {
                    foreach (var model in checkable) model.IsCheckingUpdate = false;
                    await UpdateAllPending();
                }

                return;
            }

            await MessageBoxManager.GetMessageBoxStandard(
                Texts.GUICheckForUpdatesTitle,
                string.Format(Texts.GUICheckForUpdatesResult, checkable.Count, withUpdates, unavailable),
                ButtonEnum.Ok).ShowAsync();
        }
        finally
        {
            foreach (var model in checkable) model.IsCheckingUpdate = false;
        }
    }

    // ── Updating everything that has an update ────────────────────────────────────

    /// <summary>True when at least one mod is showing a pending Nexus update.</summary>
    [ObservableProperty] private bool _hasPendingUpdates;

    // Set while UpdateAllPending is walking its list. Each successful download raises ModsChanged,
    // which reloads the mod list mid-run; this keeps that reload from interrupting with prompts.
    private bool _bulkUpdating;

    private void RefreshPendingUpdates()
    {
        HasPendingUpdates = Mods.Any(model => model.CanUpdateFromNexus);

        // The "needs attention" filter reads exactly the flags a check has just changed, so the
        // visible list has to be rebuilt - but only when that filter is the one on screen.
        if (ShowOnlyUpdatable) RefreshVisibleMods();
    }

    /// <summary>
    /// Downloads and installs every pending update in turn.
    ///
    /// Each one goes through the same path as the single-mod update, so the previous version is
    /// still archived and still restorable. The run reports what it managed rather than stopping at
    /// the first refusal: a free Nexus account cannot be issued a direct download link, and one mod
    /// falling back to its web page should not abandon the rest.
    /// </summary>
    [RelayCommand]
    private async Task UpdateAllPending()
    {
        if (Nexus is null || UpdateService is null) return;

        var pending = Mods.Where(model => model.CanUpdateFromNexus).ToList();
        if (pending.Count == 0)
        {
            await MessageBoxManager.GetMessageBoxStandard(
                Texts.GUIUpdateAllTitle, Texts.GUIUpdateAllNothing, ButtonEnum.Ok).ShowAsync();
            return;
        }

        var confirm = await MessageBoxManager.GetMessageBoxStandard(
            Texts.GUIUpdateAllTitle,
            string.Format(Texts.GUIUpdateAllConfirm, pending.Count,
                string.Join("\r\n", pending.Take(10).Select(model =>
                    $"• {model.Mod.GetName()} {model.Mod.GetVersion()} → {model.LatestVersion ?? "?"}"))),
            ButtonEnum.YesNo).ShowAsync();
        if (confirm != ButtonResult.Yes) return;

        var updated = 0;
        var failed = new List<string>();

        _bulkUpdating = true;
        try
        {
            for (var index = 0; index < pending.Count; index++)
            {
                var model = pending[index];
                InstallStatus = string.Format(Texts.GUIUpdateAllProgress,
                    index + 1, pending.Count, model.Mod.GetName());

                try
                {
                    var status = await Task.Run(() => UpdateService.CheckAsync(model.Mod));
                    if (!status.HasUpdate) continue;

                    if (await Nexus.RunUpdateAsync(UpdateService, model.Mod, status, ModsLocation)) updated++;
                    else failed.Add(model.Mod.GetName());
                }
                catch (Exception exception)
                {
                    Logger.Log($"Updating {model.Mod.GetName()} failed: {exception}");
                    failed.Add(model.Mod.GetName());
                }
            }
        }
        finally
        {
            _bulkUpdating = false;
        }

        InstallStatus = "";
        if (updated > 0) UpdateModlist(true);

        var report = string.Format(Texts.GUIUpdateAllDone, updated, pending.Count);
        if (failed.Count > 0)
            report += "\r\n\r\n" + string.Format(Texts.GUIUpdateAllFailed, string.Join(", ", failed));

        await MessageBoxManager.GetMessageBoxStandard(
            Texts.GUIUpdateAllTitle, report, ButtonEnum.Ok).ShowAsync();
    }

    /// <summary>
    /// Folds one mod's Nexus answer into its row, and re-checks whether anything is now updatable.
    ///
    /// The refresh belongs here rather than at the call sites: every path that changes a row's
    /// update badge goes through this, and one that forgot to refresh would leave "Update
    /// everything" greyed out with updates sitting in plain sight.
    /// </summary>
    private void ApplyUpdateStatus(ModModel model, NexusUpdateStatus status)
    {
        ApplyUpdateStatusCore(model, status);
        RefreshPendingUpdates();
    }

    private static void ApplyUpdateStatusCore(ModModel model, NexusUpdateStatus status)
    {
        model.NexusPageUrl = status.Record?.PageUrl ?? model.NexusPageUrl;
        model.IsFrozen = status.State == NexusUpdateState.Frozen || model.IsFrozen;
        model.UpdateFileId = status.HasUpdate ? status.LatestFileId : null;

        // "AIM could not tell" is its own answer, distinct from "up to date", and the list can be
        // filtered down to exactly those mods.
        model.UpdateCheckFailed = status.State == NexusUpdateState.Unavailable;

        // A newer version of a mod AIM patched. It gets its own line on the row rather than the
        // ordinary update badge, because taking it means giving up the fix - a trade only the user
        // can make, and one "Update everything" must never make on their behalf.
        if (status.State == NexusUpdateState.UpdateMayFixEdit)
        {
            model.UpdateMayFixEdit = true;
            model.LatestVersion = status.LatestVersion ?? model.LatestVersion;
            return;
        }

        model.UpdateMayFixEdit = false;

        if (!status.HasUpdate) return;

        // The badge is shared with the manifest-based check that runs at startup, so a Nexus result
        // only ever adds to it. Clearing it here would hide a GitHub release the other check found.
        // When UpdateFileId is present, the row routes the badge through UpdateFromNexus rather than
        // opening the stored page URL, so the user gets AIM's confirmation/download/backup flow.
        model.UpdateAvailable = true;
        model.LatestVersion = status.LatestVersion ?? model.LatestVersion;
        model.UpdateDownloadUrl ??= status.Record is not null && status.LatestFileId is > 0
            ? status.Record.FilePageUrl(status.LatestFileId.Value)
            : status.Record?.FilesPageUrl;
    }

    /// <summary>
    /// Tells the user about new versions of the mods AIM has patched.
    ///
    /// A patch AIM applied is a workaround, and the point of holding the mod back from updates was
    /// never to keep it on that version forever - it was to stop an update quietly undoing the fix
    /// before anybody noticed. So when a new version turns up, the user hears about it, along with
    /// what they patched and what taking the update costs.
    ///
    /// Nothing is applied here. Updating means unfreezing, and unfreezing means deciding the fix is
    /// no longer wanted, which is not a decision to make inside a progress dialog.
    /// </summary>
    private async Task ReportUpdatesThatMayFixEditsAsync(
        List<ModModel> examined, Dictionary<string, NexusUpdateStatus> statuses)
    {
        var news = examined
            .Select(model => (Model: model,
                Status: statuses.GetValueOrDefault(model.Mod.GetId())))
            .Where(pair => pair.Status?.State == NexusUpdateState.UpdateMayFixEdit)
            .ToList();

        if (news.Count == 0) return;

        var lines = news.Select(pair => string.Format(Texts.GUIUpdateMayFix,
            pair.Model.Mod.GetName(),
            pair.Status!.LatestVersion ?? "?",
            pair.Model.Mod.GetVersion(),
            pair.Status.Message ?? ""));

        await MessageBoxManager.GetMessageBoxStandard(
            Texts.GUIUpdateMayFixHeader,
            string.Join("\r\n\r\n", lines) + "\r\n\r\n" + Texts.GUIUpdateMayFixTooltip,
            ButtonEnum.Ok).ShowAsync();
    }

    private async Task ReportUpdateStatusAsync(ModModel model, NexusUpdateStatus status)
    {
        var message = status.State switch
        {
            NexusUpdateState.UpdateAvailable => string.Format(Texts.GUIUpdateAvailableForMod,
                model.Mod.GetName(), status.LatestVersion ?? "?"),
            NexusUpdateState.UpdateMayFixEdit => string.Format(Texts.GUIUpdateMayFix,
                model.Mod.GetName(), status.LatestVersion ?? "?", model.Mod.GetVersion(),
                status.Message ?? ""),
            NexusUpdateState.UpToDate => string.Format(Texts.GUIModIsUpToDate, model.Mod.GetName()),
            NexusUpdateState.Frozen => string.Format(Texts.GUIModIsFrozen, model.Mod.GetName()),
            NexusUpdateState.NotFromNexus => string.Format(Texts.GUIModNotFromNexus, model.Mod.GetName()),
            _ => status.Message ?? Texts.GUICheckForUpdatesFailed
        };

        await MessageBoxManager.GetMessageBoxStandard(
            Texts.GUICheckForUpdatesTitle, message, ButtonEnum.Ok).ShowAsync();
    }

    [RelayCommand]
    private void EnableAllMods()
    {
        SetAllModSelection(true);
    }

    [RelayCommand]
    private void DisableAllMods()
    {
        SetAllModSelection(false);
    }

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private void UnInstallMods()
    {
        Exception     = "";
        IsInstalling  = true;
        InstallStatus = Resources.GUIUninstallingText;

        _ = BackgroundUninstall();
    }

    private async Task BackgroundUninstall()
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var request = new ArchiveWorkerRequest(
                "uninstall", MistriaLocation, ModsLocation, [], "", GateMode: "auto");
            await new ArchiveWorkerClient().RunAsync(
                request,
                status => Dispatcher.UIThread.Post(() =>
                {
                    if (IsInstalling) InstallStatus = status;
                }),
                CancellationToken.None);
            stopwatch.Stop();
            PerformanceDiagnostics.Log($"Uninstall completed: elapsed={stopwatch.ElapsedMilliseconds} ms");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                StopInstallUiDiagnostics();
                IsInstalling  = false;
                InstallStatus = Resources.GUIUninstallCompleteText;
                GameReady = IsLaunchableGameInstallation(MistriaLocation);
                InstallationNeedsRebuild = true;
                _archiveStatusKey = "GUIArchiveNoInstallation";
                _archiveStatusModCount = 0;
                RefreshCachedArchiveStatusText();
                // Nothing is installed any more; the outcome icons are stale, and the archive is
                // back to pristine, so nothing is in it.
                foreach (var mod in Mods)
                {
                    mod.SetInstallOutcome(ModInstallState.None);
                    mod.IsInGameArchive = false;
                }

                RefreshSelectionSummary();
                RefreshSelectedModConflicts();
            });
        }
        catch (Exception e)
        {
            var errorLogPath = WriteDiagnosticErrorLog("uninstall", e);
            Logger.Log($"{Resources.GUIUninstallFatalError}\r\n{e}");
            stopwatch.Stop();
            PerformanceDiagnostics.Log($"Uninstall failed: elapsed={stopwatch.ElapsedMilliseconds} ms, exception={e.GetType().Name}");

            // Keep the page recoverable. The archive transaction has already
            // aborted, so the user can inspect the error and retry.
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                StopInstallUiDiagnostics();
                IsInstalling  = false;
                InstallStatus = Resources.GUIUninstallFatalError;
                RefreshGameReady();
                Exception     = FormatErrorMessage(Resources.GUIUninstallFatalError, e, errorLogPath);
            });
        }
    }

    /// <summary>
    /// Runs the install and reports its own outcome: null when it worked, the reason when it did
    /// not. The Exception banner is still set on failure, for the user; the return value is for
    /// callers that need to know whether to carry on.
    /// </summary>
    private async Task<string?> BackgroundInstall()
    {
        var totalStopwatch = Stopwatch.StartNew();
        try
        {
            var modsToInstall = Mods.Where(m => m.Enabled).Select(m => m.Mod).ToList();

            PerformanceDiagnostics.Log($"Install worker requested: mods={modsToInstall.Count}");
            var request = new ArchiveWorkerRequest(
                "install",
                MistriaLocation,
                ModsLocation,
                modsToInstall.Select(mod => mod.GetSourcePath()).ToArray(),
                "",
                GateMode: "auto");
            var result = await new ArchiveWorkerClient().RunAsync(
                request,
                status =>
                {
                    PerformanceDiagnostics.Log($"Worker phase: {status}");
                    if (PerformanceDiagnostics.SuppressInstallProgressUi)
                    {
                        PerformanceDiagnostics.Log("Install UI phase callback skipped by AIM_DIAGNOSTICS_NO_PROGRESS_UI");
                        return;
                    }
                    var queuedAt = Stopwatch.GetTimestamp();
                    Dispatcher.UIThread.Post(() =>
                    {
                        var delay = Stopwatch.GetElapsedTime(queuedAt).TotalMilliseconds;
                        PerformanceDiagnostics.Log($"Install UI phase callback: delay={delay:0} ms, thread={Environment.CurrentManagedThreadId}");
                        if (IsInstalling) InstallStatus = status;
                    });
                },
                CancellationToken.None);

            totalStopwatch.Stop();
            PerformanceDiagnostics.Log($"Install completed: elapsed={totalStopwatch.ElapsedMilliseconds} ms, installed={result.Installed.Length}, skipped={result.Skipped.Length}");

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                StopInstallUiDiagnostics();
                IsInstalling  = false;
                InstallStatus = result.Summary;
                GameReady = IsLaunchableGameInstallation(MistriaLocation);
                InstallationNeedsRebuild = false;
                _archiveStatusKey = "GUIArchiveMatch";
                _archiveStatusModCount = result.Installed.Length;
                RefreshCachedArchiveStatusText();

                // Checkmark for what landed, red X with the reasons for what
                // was skipped; a skipped mod's reasons also landed as
                // validation errors, so refresh the expander bindings too
                var installed = result.Installed.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var skipped   = result.Skipped.ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var mod in Mods)
                {
                    if (skipped.Contains(mod.Mod.GetId()))
                        mod.SetInstallOutcome(ModInstallState.Skipped, Resources.GUIModSkipped);
                    else if (installed.Contains(mod.Mod.GetId()))
                        mod.SetInstallOutcome(ModInstallState.Installed, Resources.GUIModInstalled);

                    // An install rebuilds the archive from the pristine copy plus exactly the mods
                    // that landed, so this is the archive's contents, not a guess at them.
                    mod.IsInGameArchive = installed.Contains(mod.Mod.GetId());
                }

                RefreshSelectionSummary();
                RefreshSelectedModConflicts();
            });

            return null;
        }
        catch (Exception e)
        {
            // Write the diagnostic first so its Recent AIM log contains only
            // the progress leading up to the failure, not a second copy of the
            // same full exception.
            var errorLogPath = WriteDiagnosticErrorLog("install", e);
            Logger.Log($"{Resources.GUIInstallFatalError}\r\n{e}");
            var failedModId = (e as ModInstallationException)?.ModId;
            totalStopwatch.Stop();
            PerformanceDiagnostics.Log($"Install failed: elapsed={totalStopwatch.ElapsedMilliseconds} ms, exception={e.GetType().Name}");

            // Keep the page recoverable. The archive transaction has already
            // aborted, so the user can disable the failing mod and retry.
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                StopInstallUiDiagnostics();
                if (!string.IsNullOrEmpty(failedModId))
                {
                    var failedMod = Mods.FirstOrDefault(m =>
                        string.Equals(m.Mod.GetId(), failedModId, StringComparison.OrdinalIgnoreCase));
                    failedMod?.SetInstallOutcome(
                        ModInstallState.Failed,
                        e.Message + "\r\n" + Texts.GUIErrorReason + GetRootCauseMessage(e));
                }

                IsInstalling  = false;
                InstallStatus = Resources.GUIInstallFatalError;
                RefreshGameReady();
                Exception     = FormatErrorMessage(Resources.GUIInstallFatalError, e, errorLogPath);
            });

            return GetRootCauseMessage(e);
        }
    }

    private void StartInstallUiDiagnostics()
    {
        if (!PerformanceDiagnostics.Enabled) return;
        _installUiHeartbeat?.Stop();
        _installUiHeartbeat = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _lastHeartbeatTimestamp = Stopwatch.GetTimestamp();
        _installUiHeartbeat.Tick += (_, _) =>
        {
            var now = Stopwatch.GetTimestamp();
            var gap = Stopwatch.GetElapsedTime(_lastHeartbeatTimestamp).TotalMilliseconds;
            _lastHeartbeatTimestamp = now;
            PerformanceDiagnostics.Log($"UI heartbeat: gap={gap:0} ms, installing={IsInstalling}, thread={Environment.CurrentManagedThreadId}, {PerformanceDiagnostics.ProcessMetrics()}");
        };
        _installUiHeartbeat.Start();
        PerformanceDiagnostics.Log("UI heartbeat started");
    }

    private void StopInstallUiDiagnostics()
    {
        if (_installUiHeartbeat is null) return;
        _installUiHeartbeat.Stop();
        _installUiHeartbeat = null;
        PerformanceDiagnostics.Log("UI heartbeat stopped");
    }

    private static string FormatErrorMessage(string heading, Exception exception, string? errorLogPath)
    {
        var rootCause = GetRootCauseMessage(exception);
        var message = heading + "\n" + exception.Message;
        if (!string.Equals(rootCause, exception.Message, StringComparison.Ordinal))
            message += "\n\n" + Localized("GUIErrorReason") + rootCause;

        if (!string.IsNullOrEmpty(errorLogPath))
        {
            message += $"\n\n{Localized("GUIErrorDetailsSaved")}\n{errorLogPath}";
        }

        return message;
    }

    private static string GetRootCauseMessage(Exception exception)
    {
        while (exception.InnerException is not null)
            exception = exception.InnerException;

        return exception.Message;
    }

    private static string? WriteDiagnosticErrorLog(string operation, Exception exception)
    {
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var root = string.IsNullOrWhiteSpace(localAppData)
                ? AppContext.BaseDirectory
                : localAppData;
            var directory = Path.Combine(root, "AIM", "logs");
            Directory.CreateDirectory(directory);

            var path = Path.Combine(directory, $"aim-error-{DateTime.Now:yyyyMMdd-HHmmssfff}.txt");
            var contents = $"AIM diagnostic error log\r\n" +
                           $"Timestamp (UTC): {DateTime.UtcNow:O}\r\n" +
                           $"Application: {AppInfo.DisplayVersion}\r\n" +
                           $"Operation: {operation}\r\n\r\n" +
                           "Exception:\r\n" + exception + "\r\n\r\n" +
                           "Recent AIM log:\r\n" + string.Join("\r\n", Logger.GetLogs());
            File.WriteAllText(path, contents, new System.Text.UTF8Encoding(false));
            Logger.Log($"Diagnostic error log written to: {path}");
            return path;
        }
        catch (Exception logException)
        {
            Logger.Log($"Could not write diagnostic error log: {logException.Message}");
            return null;
        }
    }

    private bool CanRemove() =>
        !MistriaLocation.Equals("") && !IsInstalling &&
        new AssetsStore(MistriaLocation).HasMomiInstallation();

    [RelayCommand(CanExecute = nameof(CanLaunchGame))]
    private void LaunchGame()
    {
        // Arm the crash watcher on the way out. This is the moment a crash is most likely to be
        // about to happen, and the game keeps only its most recent one - so a session that crashes
        // twice has already lost the first report by the time anyone thinks to look.
        EnsureCrashWatcher();

        var executable = GameExecutableLocator.Find(MistriaLocation);

        if (_settings.LaunchGameDirectly && executable is not null)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = executable,
                    WorkingDirectory = MistriaLocation,
                    UseShellExecute = true
                });
                return;
            }
            catch
            {
                // Fall back to Steam if the direct executable cannot be started.
            }
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = AppInfo.GameLaunchUri,
                UseShellExecute = true
            });
            return;
        }
        catch
        {
            // Fall back to the installed executable when Steam URI handling is
            // unavailable on the current desktop environment.
        }

        if (executable is null) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = MistriaLocation,
                UseShellExecute = true
            });
        }
        catch { /* Launching the game must not crash MOMI. */ }
    }

    private bool CanLaunchGame() => GameReady && !IsInstalling;

    private void RefreshGameReady()
    {
        // Launching the game is independent from whether MOMI currently has
        // mods installed. After Uninstall, the pristine game archive should
        // still be launchable, so do not use HasMomiInstallation here.
        GameReady = IsLaunchableGameInstallation(MistriaLocation);
        RefreshArchiveStatus();
    }

    private static bool IsLaunchableGameInstallation(string? location)
    {
        if (string.IsNullOrWhiteSpace(location) || !Directory.Exists(location))
            return false;

        if (!File.Exists(Path.Combine(location, "Maybe.toml")))
            return false;

        var unpackedAssets = Path.Combine(location, "assets");
        if (Directory.Exists(unpackedAssets))
            return true;

        var archivePath = Path.Combine(location, "assets.zip");
        // The archive contents are validated by the archive worker during the
        // transaction. Do not reopen and enumerate a potentially 600+ MB ZIP
        // on the UI thread merely to refresh the Play button after completion.
        return File.Exists(archivePath);
    }

    private void RefreshArchiveStatus()
    {
        if (string.IsNullOrWhiteSpace(MistriaLocation))
        {
            InstallationNeedsRebuild = false;
            _archiveStatusKey = "GUIArchiveNoGameArchive";
            _archiveStatusModCount = 0;
            RefreshCachedArchiveStatusText();
            return;
        }

        var store = new AssetsStore(MistriaLocation);
        if (!store.HasMomiInstallation())
        {
            foreach (var mod in Mods) mod.SetAlreadyInstalled(false);
            InstallationNeedsRebuild = true;
            _archiveStatusKey = "GUIArchiveNoInstallation";
            _archiveStatusModCount = 0;
            RefreshCachedArchiveStatusText();
            return;
        }

        RecordedInstallState? recorded;
        try { recorded = store.GetRecordedInstallState(); }
        catch
        {
            foreach (var mod in Mods) mod.SetAlreadyInstalled(false);
            InstallationNeedsRebuild = true;
            _archiveStatusKey = "GUIArchiveStateUnavailable";
            _archiveStatusModCount = 0;
            RefreshCachedArchiveStatusText();
            return;
        }

        if (recorded is null)
        {
            foreach (var mod in Mods) mod.SetAlreadyInstalled(false);
            InstallationNeedsRebuild = true;
            _archiveStatusKey = "GUIArchiveVersionsUnavailable";
            _archiveStatusModCount = 0;
            RefreshCachedArchiveStatusText();
            return;
        }

        // A folder and an archive can represent the same logical mod.  The
        // duplicate-copy warning handles that situation in the list, but the
        // archive status is keyed by logical ID and must therefore not throw
        // when multiple physical copies are present.
        var desired = Mods
            .Where(mod => mod.Enabled)
            .GroupBy(mod => mod.Mod.GetId(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Mod.GetVersion(),
                StringComparer.OrdinalIgnoreCase);
        var actual = recorded.Mods
            .GroupBy(mod => mod.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Version,
                StringComparer.OrdinalIgnoreCase);

        foreach (var mod in Mods)
        {
            var alreadyInstalled = actual.TryGetValue(mod.Mod.GetId(), out var installedVersion) &&
                                    string.Equals(installedVersion, mod.Mod.GetVersion(), StringComparison.OrdinalIgnoreCase);

            // What the archive says, recorded whatever this session did to the mod. SetAlreadyInstalled
            // below only speaks for a mod AIM has not touched yet, which is why the summary cannot
            // be built from it.
            mod.IsInGameArchive = alreadyInstalled;
            mod.SetAlreadyInstalled(alreadyInstalled);
        }

        RefreshSelectionSummary();

        var matches = desired.Count == actual.Count &&
                      desired.All(pair => actual.TryGetValue(pair.Key, out var version) &&
                                          string.Equals(version, pair.Value, StringComparison.OrdinalIgnoreCase));

        _archiveStatusKey = matches ? "GUIArchiveMatch" : "GUIArchiveDifferent";
        _archiveStatusModCount = actual.Count;
        RefreshCachedArchiveStatusText();
        InstallationNeedsRebuild = !matches;
    }

    private static string Localized(string key) =>
        Resources.ResourceManager.GetString(key, Resources.Culture) ?? key;

    private DuplicateModGroup? FindSelectedDuplicateGroup()
    {
        var groups = DuplicateModDetector.Find(Mods.Select(model => model.Mod));
        return groups.FirstOrDefault(group =>
            group.Copies.Count(copy => Mods.Any(model =>
                ReferenceEquals(model.Mod, copy) && model.Enabled)) > 1);
    }

    private bool CanInstall() =>
        !MistriaLocation.Equals("") && !ModsLocation.Equals("") && Mods.Any(mod => mod.Enabled) &&
        !IsInstalling && InstallationNeedsRebuild;
}
