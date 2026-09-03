using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
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
        new AsyncRelayCommand<ModModel>(AssociateWithNexus),
        new AsyncRelayCommand<ModModel>(CheckModForUpdate),
        new AsyncRelayCommand<ModModel>(UpdateModFromNexus),
        new RelayCommand<ModModel>(ToggleModFreeze),
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
            foreach (var model in models)
            {
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
            // Only these two kinds become report issues at all; the merge and localisation kinds are
            // combined rather than overwritten, so there is nothing there to settle.
            if (conflict.Kind is not (ModFileConflictKind.HardReplacement or ModFileConflictKind.SharedDestination))
                return true;

            var ids = conflict.ModIds.Where(versions.ContainsKey).ToList();
            if (ids.Count < 2) return true;

            var key = string.Join(",", ids
                .Select(id => $"{id}@{versions[id]}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(entry => entry, StringComparer.OrdinalIgnoreCase));

            return !IsSettled(dismissed, LoadOrderNoteKind.FileConflict, key);
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
        // The Nexus build uses the user's personal API key for both NMD downloads and explicit
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
    public IReadOnlyList<ModModel> FilteredMods
    {
        get
        {
            IEnumerable<ModModel> visible = Mods;

            if (HasModSearch) visible = visible.Where(MatchesModSearch);
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

            return visible.ToList();
        }
    }

    public bool HasModSearch => !string.IsNullOrWhiteSpace(ModSearchQuery);

    /// <summary>True when the list is not showing every mod in its real order.</summary>
    public bool IsListReordered =>
        HasModSearch || ShowOnlyUpdatable || SortAlphabetically || SortByRecentlyUpdated;

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

    partial void OnShowOnlyUpdatableChanged(bool value) => RefreshVisibleMods();

    private void RefreshVisibleMods()
    {
        OnPropertyChanged(nameof(FilteredMods));
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

    private void RefreshFilteredMods() => OnPropertyChanged(nameof(FilteredMods));

    // ── Commands ──────────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private void InstallMods()
    {
        var duplicate = FindSelectedDuplicateGroup();
        if (duplicate is not null)
        {
            var message = Localized("GUIDuplicateModInstallBlocked");
            Exception = message;
            InstallStatus = message;
            return;
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
        _ = BackgroundInstall();
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
    /// Applies a bulk checkbox change as one logical selection update. The
    /// individual rows still notify their bindings immediately, but costly
    /// derived state (archive status and selected-mod scans) is recomputed once.
    /// </summary>
    private void SetAllModSelection(bool enabled)
    {
        _bulkSelectionChangeDepth++;
        try
        {
            foreach (var mod in Mods.Where(mod => !mod.InError))
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

            var plan = await Task.Run(() => LoadOrderPlanner.Plan(current, enabled));
            var orderNotes = plan.Notes
                .Where(note => note.Kind == LoadOrderNoteKind.DependencyMove)
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

        try
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

            // Issues the user has already looked at and accepted. The store is per mods folder,
            // so two profiles pointing at different folders keep their own judgements.
            var dismissed = string.IsNullOrEmpty(ModsLocation)
                ? null
                : await Task.Run(() =>
                {
                    var store = new DismissedIssueStore(ModsLocation);
                    store.PruneOlderThan(TimeSpan.FromDays(365));
                    return store;
                });

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

            if (App.TopLevel is Window owner)
            {
                await LoadOrderResultWindow.ShowAsync(
                    owner, summary, notes, Texts.GUIConflictReportTitle, true,
                    dismissedIssues: dismissed, actions: ConflictActions);

                // The user has just ticked issues off, or put some back. The rows read the same
                // store, so re-running the sweep is what makes their warning triangles agree with
                // the report they were looking at a moment ago.
                RefreshSelectedModConflicts();
            }
            else
                await MessageBoxManager.GetMessageBoxStandard(
                    Texts.GUIConflictReportTitle,
                    summary.Length > 0 ? summary : string.Join("\r\n\r\n", notes.Select(note => $"• {note.Message}")),
                    ButtonEnum.Ok).ShowAsync();
        }
        catch (Exception exception)
        {
            Logger.Log($"Conflict report failed: {exception}");
            Exception = $"{Texts.GUIConflictReportTitle}: {exception.Message}";
        }
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

        var client = Nexus?.CreateApiClient();
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

        return await ConflictResearchWindow.ShowAsync(
            owner, note.Message, subjects, Nexus?.CreateApiClient(), BuildResearchContext(note));
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
            BackupStore is null ? null : (modId, paths, reason) => SetAsideModFiles(modId, paths, reason));
    }

    private bool MakeModWinById(LoadOrderNote note, string modId)
    {
        var participant = note.Participants.FirstOrDefault(entry =>
            string.Equals(entry.ModId, modId, StringComparison.OrdinalIgnoreCase));

        return participant is not null && MakeModWin(note, participant);
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
        UpdateService.SetFrozen(model.Mod, frozen);
        model.IsFrozen = frozen;

        // A frozen mod's pending update badge is noise: the user has said they do not want it.
        if (!frozen) return;
        model.UpdateAvailable = false;
        model.UpdateFileId = null;
        RefreshPendingUpdates();
    }

    private async Task CheckModForUpdate(ModModel? model)
    {
        if (model is null || UpdateService is null) return;
        if (Nexus is not null && !await Nexus.EnsureApiKeyAsync()) return;

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

    private async Task RestoreBackup(ModModel model, ModBackup backup)
    {
        if (BackupStore is null) return;

        var source = model.Mod.GetSourcePath();
        var modName = ModBackupStore.ModNameFor(source);
        var newest = backup;

        var confirm = await MessageBoxManager.GetMessageBoxStandard(
            Texts.GUIRestoreVersionTitle,
            string.Format(Texts.GUIRestoreVersionConfirm, model.Mod.GetName(), newest.Describe()),
            ButtonEnum.YesNo).ShowAsync();

        if (confirm != ButtonResult.Yes) return;

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
        }
        catch (Exception e)
        {
            await MessageBoxManager.GetMessageBoxStandard(
                Texts.GUIRestoreVersionTitle, e.Message, ButtonEnum.Ok).ShowAsync();
        }
    }

    // ── Nexus: bulk checks ────────────────────────────────────────────────────────

    [RelayCommand]
    private Task CheckSelectedModsForUpdates() =>
        CheckManyForUpdates(Mods.Where(model => model.Enabled).ToList());

    [RelayCommand]
    private Task CheckAllModsForUpdates() => CheckManyForUpdates(Mods.ToList());

    private async Task CheckManyForUpdates(List<ModModel> models)
    {
        if (models.Count == 0 || UpdateService is null) return;
        if (Nexus is not null && !await Nexus.EnsureApiKeyAsync()) return;

        var checkable = models.Where(model => !model.IsFrozen).ToList();
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

        if (!status.HasUpdate) return;

        // The badge is shared with the manifest-based check that runs at startup, so a Nexus result
        // only ever adds to it. Clearing it here would hide a GitHub release the other check found,
        // and the badge needs somewhere to go: without a download url it opens nothing.
        model.UpdateAvailable = true;
        model.LatestVersion = status.LatestVersion ?? model.LatestVersion;
        model.UpdateDownloadUrl ??= status.Record is not null && status.LatestFileId is > 0
            ? status.Record.FilePageUrl(status.LatestFileId.Value)
            : status.Record?.FilesPageUrl;
    }

    private async Task ReportUpdateStatusAsync(ModModel model, NexusUpdateStatus status)
    {
        var message = status.State switch
        {
            NexusUpdateState.UpdateAvailable => string.Format(Texts.GUIUpdateAvailableForMod,
                model.Mod.GetName(), status.LatestVersion ?? "?"),
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

    private async Task BackgroundInstall()
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
