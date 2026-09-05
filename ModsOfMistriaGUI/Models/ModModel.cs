using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Garethp.ModsOfMistriaInstallerLib;
using Garethp.ModsOfMistriaInstallerLib.Generator;
using Garethp.ModsOfMistriaInstallerLib.Lang;
using Garethp.ModsOfMistriaGUI.Services;
using Garethp.ModsOfMistriaInstallerLib.ModTypes;

namespace Garethp.ModsOfMistriaGUI.Models;

// The outcome of this session's most recent install action. Resets to None
// when the mod list reloads or an uninstall runs; it is never persisted.
public enum ModInstallState
{
    None,
    Installed,
    Skipped,
    Failed,
    AlreadyInstalled,
}

public partial class ModModel : ObservableObject
{
    public LocalizationService Localization => LocalizationService.Instance;
    public LocalizedTexts Texts => LocalizedTexts.Instance;
    public readonly IMod Mod;
    private IReadOnlyList<IMod> _duplicateCopies = [];
    private IReadOnlyList<string> _conflictWarnings = [];
    private IReadOnlyList<string> _compatibilityWarnings = [];
    private string _full = string.Empty;
    private string _description = string.Empty;
    
    private bool _enabledBacking;

    [ObservableProperty] private int _position;

    public bool IsAlternateRow => Position % 2 == 0;

    partial void OnPositionChanged(int value)
        => OnPropertyChanged(nameof(IsAlternateRow));

    // Set by UpdateChecker after startup — true when a newer release is available
    [NotifyPropertyChangedFor(nameof(CanUpdateFromNexus))]
    [NotifyPropertyChangedFor(nameof(HasNexusUpdate))]
    [NotifyPropertyChangedFor(nameof(HasGenericUpdate))]
    [NotifyPropertyChangedFor(nameof(UpdateTooltip))]
    [ObservableProperty] private bool _updateAvailable;

    [NotifyPropertyChangedFor(nameof(UpdateTooltip))]
    [ObservableProperty] private string? _latestVersion;
    [ObservableProperty] private string? _updateDownloadUrl;

    // ── Nexus ────────────────────────────────────────────────────────────────────

    /// <summary>The mod's page, when AIM knows which Nexus mod this is.</summary>
    [NotifyPropertyChangedFor(nameof(IsFromNexus))]
    [ObservableProperty] private string? _nexusPageUrl;

    /// <summary>The file id an update would install. Null when the update cannot be fetched.</summary>
    [NotifyPropertyChangedFor(nameof(CanUpdateFromNexus))]
    [NotifyPropertyChangedFor(nameof(HasNexusUpdate))]
    [NotifyPropertyChangedFor(nameof(HasGenericUpdate))]
    [NotifyPropertyChangedFor(nameof(UpdateTooltip))]
    [ObservableProperty] private int? _updateFileId;

    [ObservableProperty] private bool _contextActionsLocked;

    // ── Release notes ────────────────────────────────────────────────────────────

    /// <summary>
    /// True when AIM knows which Nexus mod this is, so its release notes can be asked for. The
    /// icon is hidden entirely otherwise: an icon that can only ever say "nothing here" is worse
    /// than no icon.
    /// </summary>
    [NotifyPropertyChangedFor(nameof(CanShowChangelog))]
    [ObservableProperty] private bool _hasChangelogSource;

    public bool CanShowChangelog => HasChangelogSource && !ContextActionsLocked;

    /// <summary>
    /// What the hover tooltip says: the newest version's notes once they have been fetched, and an
    /// invitation to fetch them before that.
    ///
    /// Notes are not loaded for every mod up front - that would be one Nexus call per mod on every
    /// launch - so the first hover starts the fetch and this fills in underneath the open tooltip.
    /// </summary>
    [ObservableProperty] private string _changelogPreview = "";

    /// <summary>Stops a hover storm from queuing the same fetch a dozen times.</summary>
    public bool ChangelogRequested { get; set; }

    /// <summary>The user asked for this mod to be left on the version it is on.</summary>
    [ObservableProperty] private bool _isFrozen;

    /// <summary>
    /// Why AIM froze this mod, when AIM was the one that froze it. Empty for a freeze the user set.
    ///
    /// Kept apart from <see cref="IsFrozen"/> because the two mean opposite things about updates.
    /// The user's freeze says "stop offering"; AIM's says "this is patched, so an update is a
    /// decision rather than a habit".
    /// </summary>
    [ObservableProperty] private string _freezeReason = "";

    /// <summary>
    /// A newer version exists for a mod AIM patched, so the update may make the patch unnecessary.
    ///
    /// Not the ordinary update badge: updating replaces the mod folder and takes AIM's edit with
    /// it, so this is offered as a choice with that consequence spelled out rather than swept into
    /// "update everything".
    /// </summary>
    [ObservableProperty] private bool _updateMayFixEdit;

    [ObservableProperty] private bool _isCheckingUpdate;

    /// <summary>
    /// The last update check could not reach an answer for this mod - no Nexus association, a
    /// network failure, or a mod page that no longer exists.
    ///
    /// Worth keeping rather than only counting, because "AIM could not tell" is a different state
    /// from "up to date" and the user may want to look at exactly those mods.
    /// </summary>
    [ObservableProperty] private bool _updateCheckFailed;

    /// <summary>A previous version is in the backup store and can be restored.</summary>
    [ObservableProperty] private bool _hasBackup;

    /// <summary>
    /// Every archived copy of this mod, newest first, for the row's version dropdown.
    ///
    /// The right-click menu restores the newest and only the newest, which is the common case but
    /// not the one that matters: a user rolling back is usually looking for the version from
    /// *before* the one that broke.
    /// </summary>
    public ObservableCollection<ModBackupChoice> AvailableBackups { get; } = [];

    public bool CanChooseVersion => AvailableBackups.Count > 0 && !ContextActionsLocked;

    public void SetBackups(IEnumerable<ModBackupChoice> choices)
    {
        AvailableBackups.Clear();
        foreach (var choice in choices) AvailableBackups.Add(choice);

        HasBackup = AvailableBackups.Count > 0;
        OnPropertyChanged(nameof(CanChooseVersion));
    }

    // ── Edits AIM itself made ────────────────────────────────────────────────────

    /// <summary>
    /// AIM has changed a file inside this mod - currently only by setting aside a file that
    /// collided with another mod.
    ///
    /// This is on the row rather than buried in a log because the risk of AIM editing somebody
    /// else's mod is not that the edit goes wrong at the time; it is that it is silently forgotten.
    /// A user reporting a bug to the author weeks later has to be able to see, without going
    /// looking, that the files on disk are no longer the ones that were downloaded.
    /// </summary>
    [ObservableProperty] private bool _wasEditedByAim;

    /// <summary>What AIM changed, one line per edit, for the marker's tooltip.</summary>
    [ObservableProperty] private string _aimEditSummary = "";

    public string EditedTooltip =>
        string.IsNullOrWhiteSpace(AimEditSummary)
            ? Texts.GUIModEditedTooltip
            : $"{Texts.GUIModEditedTooltip}\n\n{AimEditSummary}\n\n{Texts.GUIModEditedUndoHint}";

    partial void OnAimEditSummaryChanged(string value) => OnPropertyChanged(nameof(EditedTooltip));

    // ── Caught crashing the game ─────────────────────────────────────────────────

    /// <summary>
    /// A supervised run proved this exact version of this mod crashes the game: it was switched
    /// off, the game was rebuilt and played, and the crash did not come back.
    ///
    /// It belongs on the row for the same reason the edited marker does. The verdict is earned in
    /// the crash window, over four minutes of watching a game, and then the user closes that window
    /// and is back at a list of two hundred checkboxes with nothing to say which one they just
    /// caught. Weeks later the obvious thing to do with a mod that is switched off for no visible
    /// reason is to switch it back on.
    ///
    /// It is tied to the version that was tested, so an update clears the mark - see
    /// <see cref="CrashTrialStore.GuiltyVerdict"/>.
    /// </summary>
    [ObservableProperty] private bool _isKnownCrasher;

    /// <summary>When it was caught and what the run said, for the marker's tooltip.</summary>
    [ObservableProperty] private string _knownCrasherSummary = "";

    /// <summary>
    /// The user marked this themselves rather than a run catching it.
    ///
    /// Worth distinguishing on the row and not only in the crash window. The badge is a claim about
    /// the mod, and "AIM watched this crash the game" and "I decided this crashes the game" are
    /// claims of very different strength - the user is entitled to know which one they are looking
    /// at before they act on it, particularly months later when they no longer remember marking it.
    /// </summary>
    [ObservableProperty] private bool _knownCrasherIsManual;

    public string KnownCrasherBadge =>
        KnownCrasherIsManual ? Texts.GUIModCrasherBadgeManual : Texts.GUIModCrasherBadge;

    public string KnownCrasherTooltip
    {
        get
        {
            var opening = KnownCrasherIsManual ? Texts.GUIModCrasherTooltipManual : Texts.GUIModCrasherTooltip;

            return string.IsNullOrWhiteSpace(KnownCrasherSummary)
                ? opening
                : $"{opening}\n\n{KnownCrasherSummary}\n\n{Texts.GUIModCrasherHint}";
        }
    }

    partial void OnKnownCrasherSummaryChanged(string value) => OnPropertyChanged(nameof(KnownCrasherTooltip));

    partial void OnKnownCrasherIsManualChanged(bool value)
    {
        OnPropertyChanged(nameof(KnownCrasherBadge));
        OnPropertyChanged(nameof(KnownCrasherTooltip));
    }

    // ── Editing the mod's own files ──────────────────────────────────────────────

    /// <summary>
    /// The mod is a folder on disk with a manifest AIM can hand to a text editor. False for
    /// archive-backed mods: there is no file to open, and editing an extracted copy would be
    /// thrown away by the next install.
    /// </summary>
    [ObservableProperty] private bool _hasEditableManifest;

    /// <summary>The mod ships a config file AIM recognised. Many mods have none.</summary>
    [ObservableProperty] private bool _hasEditableConfig;

    public bool CanEditManifest => HasEditableManifest && !ContextActionsLocked;
    public bool CanEditConfig => HasEditableConfig && !ContextActionsLocked;
    public bool CanRemoveMod => !ContextActionsLocked;

    partial void OnHasEditableManifestChanged(bool value)
        => OnPropertyChanged(nameof(CanEditManifest));

    partial void OnHasEditableConfigChanged(bool value)
        => OnPropertyChanged(nameof(CanEditConfig));

    /// <summary>The right-click actions, supplied by the page view model.</summary>
    public ModRowCommands? Commands { get; set; }

    /// <summary>
    /// When the mod's folder or archive last changed on disk, which is the closest thing to "when
    /// was this updated" that works for every mod - including ones AIM did not install.
    ///
    /// Read once when the list loads rather than on each sort: it is a filesystem call per mod, and
    /// re-sorting the view should not touch the disk at all.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.MinValue;

    public bool IsFromNexus => !string.IsNullOrEmpty(NexusPageUrl);

    public bool CanOpenNexusPage => IsFromNexus && !ContextActionsLocked;

    /// <summary>
    /// Tracking needs a page to track. A mod AIM has no Nexus provenance for has nothing to send,
    /// which is what "Associate with Nexus…" is for.
    /// </summary>
    public bool CanTrackOnNexus => IsFromNexus && !ContextActionsLocked;
    public bool CanAssociateWithNexus => !IsFromNexus && !ContextActionsLocked;
    public bool CanCheckForUpdate => !ContextActionsLocked;

    // Nexus update results use AIM's download flow. Other update sources retain the old behavior
    // of opening their declared URL.
    public bool HasNexusUpdate => UpdateAvailable && UpdateFileId is not null;
    public bool HasGenericUpdate => UpdateAvailable && UpdateFileId is null;

    public bool CanUpdateFromNexus => UpdateAvailable && UpdateFileId is not null && !ContextActionsLocked;
    public bool CanToggleFreeze => !ContextActionsLocked;

    /// <summary>
    /// Whether this row may be sent to either end of the load order.
    ///
    /// Deliberately not tied to the drag rule. Dragging is paused while a filter or an A-Z sort is
    /// on, because dropping a row between two others in a re-sorted list has no obvious meaning for
    /// the order underneath - but "put this at the top" means the same thing whatever the list is
    /// currently showing, so it stays available.
    /// </summary>
    public bool CanReorderThisMod => !ContextActionsLocked;
    public bool CanRestorePreviousVersion => HasBackup && !ContextActionsLocked;
    public bool CanOpenModFolder => !ContextActionsLocked;

    public string FreezeMenuHeader => IsFrozen ? Texts.GUIUnfreezeMod : Texts.GUIFreezeMod;

    partial void OnIsFrozenChanged(bool value) => OnPropertyChanged(nameof(FreezeMenuHeader));

    partial void OnContextActionsLockedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanOpenNexusPage));
        OnPropertyChanged(nameof(CanTrackOnNexus));
        OnPropertyChanged(nameof(CanAssociateWithNexus));
        OnPropertyChanged(nameof(CanCheckForUpdate));
        OnPropertyChanged(nameof(CanUpdateFromNexus));
        OnPropertyChanged(nameof(CanToggleFreeze));
        OnPropertyChanged(nameof(CanReorderThisMod));
        OnPropertyChanged(nameof(CanRestorePreviousVersion));
        OnPropertyChanged(nameof(CanOpenModFolder));
        OnPropertyChanged(nameof(CanEditManifest));
        OnPropertyChanged(nameof(CanEditConfig));
        OnPropertyChanged(nameof(CanRemoveMod));
        OnPropertyChanged(nameof(UpdateTooltip));
        OnPropertyChanged(nameof(CanChooseVersion));
        OnPropertyChanged(nameof(CanShowChangelog));
    }

    partial void OnNexusPageUrlChanged(string? value)
    {
        OnPropertyChanged(nameof(CanOpenNexusPage));
        OnPropertyChanged(nameof(CanTrackOnNexus));
        OnPropertyChanged(nameof(CanAssociateWithNexus));
    }

    partial void OnHasBackupChanged(bool value)
        => OnPropertyChanged(nameof(CanRestorePreviousVersion));

    public ModModel(IMod mod)
    {
        Mod = mod;
        _enabledBacking = mod.IsInstalled();
        _full = BuildFull();
        _description = mod.GetDisplayDescription(Localization.LanguageCode);
    }

    public ModModel()
    {
        Mod = new FolderMod();
        _full = BuildFull();
        _description = Mod.GetDisplayDescription(Localization.LanguageCode);
    }

    public bool Enabled
    {
        get => !InError && _enabledBacking;
        set
        {
            if (_enabledBacking == value) return;
            _enabledBacking = value;
            Mod.SetInstalled(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsPendingInstall));
            OnPropertyChanged(nameof(HasDuplicateWarning));
            OnPropertyChanged(nameof(InWarning));
            OnPropertyChanged(nameof(Warnings));
            OnPropertyChanged(nameof(WarningTooltip));
            OnPropertyChanged(nameof(ShowPlainRow));
            OnPropertyChanged(nameof(ShowStatusRow));
        }
    }

    /// <summary>
    /// Row warnings the user has ticked off in the issues report, by the text of the warning.
    ///
    /// The two warnings that live only on the row - the mod's own validation warnings, and having
    /// two copies of it installed - had no way to be settled, because they were never reported. Now
    /// that they are, a tick there has to reach here: a report that says nothing is outstanding
    /// while the row still shows a triangle is the contradiction this whole pass exists to remove,
    /// and it is no better in that direction than the other.
    ///
    /// Matched on message text rather than on an id because that is what the row has. The note in
    /// the report is built from the same string, so the two cannot drift apart without the drift
    /// being visible in one place.
    /// </summary>
    private HashSet<string> _settledInlineWarnings = new(StringComparer.Ordinal);

    /// <summary>The stand-in text for "two copies of this mod", which has no message of its own.</summary>
    public const string DuplicateWarningKey = "aim:duplicate-copies";

    public void SetSettledInlineWarnings(IReadOnlyCollection<string> settled)
    {
        _settledInlineWarnings = new HashSet<string>(settled, StringComparer.Ordinal);
        OnPropertyChanged(nameof(HasDuplicateWarning));
        OnPropertyChanged(nameof(HasInlineWarning));
        OnPropertyChanged(nameof(InWarning));
        OnPropertyChanged(nameof(Warnings));
        OnPropertyChanged(nameof(WarningTooltip));
    }

    /// <summary>The mod's own validation warnings, minus any the user has settled.</summary>
    public IReadOnlyList<string> OutstandingValidationWarnings =>
        Mod.GetValidation().Warnings
            .Select(warning => warning.Message)
            .Where(message => !string.IsNullOrWhiteSpace(message) && !_settledInlineWarnings.Contains(message))
            .ToList();

    public bool HasDuplicateWarning =>
        Enabled && _duplicateCopies.Count > 1 && !_settledInlineWarnings.Contains(DuplicateWarningKey);
    // Compatibility warnings describe the mod itself and must remain visible
    // before selection. Ordinary conflict warnings describe the current
    // selection and are shown only for enabled mods.
    public bool HasConflictWarning =>
        (Enabled && _conflictWarnings.Count > 0) || _compatibilityWarnings.Count > 0;
    public bool HasInlineWarning =>
        OutstandingValidationWarnings.Count > 0
        || HasDuplicateWarning
        || _compatibilityWarnings.Count > 0;
    public bool InWarning => HasInlineWarning || HasConflictWarning;
    public bool InError   => Mod.GetValidation().Status == ValidationStatus.Invalid;
    public bool IsValid   => Mod.GetValidation().Status == ValidationStatus.Valid;

    public string Warnings
    {
        get
        {
            var warnings = OutstandingValidationWarnings.ToList();
            if (HasDuplicateWarning)
            {
                var copies = string.Join("\r\n", _duplicateCopies
                    .Select(copy =>
                    {
                        var marker = ReferenceEquals(copy, Mod) ? "[selected] " : "";
                        return $"• {marker}{copy.GetVersion()} — {copy.GetSourcePath()}";
                    }));
                warnings.Add(string.Format(Texts.GUIModDuplicateCopies, copies));
            }
            warnings.AddRange(_compatibilityWarnings);
            // A warning status without a message must never create an empty
            // status expander. Keep the status visible, but provide a useful
            // fallback instead of rendering a blank panel.
            if (warnings.Count == 0 && Mod.GetValidation().Status == ValidationStatus.Warning)
                warnings.Add(Texts.GUIModHasWarnings);
            return string.Join("\r\n", warnings);
        }
    }
    public string ConflictWarnings => string.Join("\r\n", _conflictWarnings);

    /// <summary>
    /// The main list intentionally keeps warnings out of an expanded panel.
    /// Every warning source therefore feeds one hover tooltip: validation,
    /// duplicate copies, game compatibility, and selected-mod conflicts.
    /// </summary>
    public string WarningTooltip
    {
        get
        {
            var messages = new List<string>();
            if (!string.IsNullOrWhiteSpace(Warnings)) messages.Add(Warnings);
            messages.AddRange(_conflictWarnings.Where(message => !string.IsNullOrWhiteSpace(message)));
            return messages.Count == 0 ? Texts.GUIModHasWarnings : string.Join("\r\n\r\n", messages);
        }
    }
    public string Errors   => string.Join("\r\n", Mod.GetValidation().Errors.Select(w => w.Message));

    public void SetDuplicateCopies(IReadOnlyList<IMod> copies)
    {
        _duplicateCopies = copies;
        OnPropertyChanged(nameof(HasDuplicateWarning));
        OnPropertyChanged(nameof(HasConflictWarning));
        OnPropertyChanged(nameof(HasInlineWarning));
        OnPropertyChanged(nameof(InWarning));
        OnPropertyChanged(nameof(Warnings));
        OnPropertyChanged(nameof(ConflictWarnings));
        OnPropertyChanged(nameof(WarningTooltip));
        OnPropertyChanged(nameof(ShowPlainRow));
        OnPropertyChanged(nameof(ShowStatusRow));
    }

    public void SetConflictWarnings(IReadOnlyList<string> warnings)
    {
        _conflictWarnings = warnings;
        OnPropertyChanged(nameof(HasConflictWarning));
        OnPropertyChanged(nameof(HasInlineWarning));
        OnPropertyChanged(nameof(InWarning));
        OnPropertyChanged(nameof(Warnings));
        OnPropertyChanged(nameof(ConflictWarnings));
        OnPropertyChanged(nameof(WarningTooltip));
        OnPropertyChanged(nameof(ShowPlainRow));
        OnPropertyChanged(nameof(ShowStatusRow));
    }

    public void SetCompatibilityWarnings(IReadOnlyList<string> warnings)
    {
        _compatibilityWarnings = warnings;
        OnPropertyChanged(nameof(HasConflictWarning));
        OnPropertyChanged(nameof(HasInlineWarning));
        OnPropertyChanged(nameof(InWarning));
        OnPropertyChanged(nameof(Warnings));
        OnPropertyChanged(nameof(WarningTooltip));
        OnPropertyChanged(nameof(ShowPlainRow));
        OnPropertyChanged(nameof(ShowStatusRow));
    }

    // ── Install outcome ───────────────────────────────────────────────────────

    private ModInstallState _installState = ModInstallState.None;
    private bool _installDetailIsSuccessMessage;

    // What the expander says about the outcome: "Installed successfully." or
    // the skip reasons
    public string InstallDetail { get; private set; } = "";

    public bool WasInstalled      => _installState is ModInstallState.Installed or ModInstallState.AlreadyInstalled;
    public bool WasAlreadyInstalled => _installState == ModInstallState.AlreadyInstalled;
    public string InstallStatusTooltip => WasAlreadyInstalled ? Texts.GUIModAlreadyInstalled : Texts.GUIModInstalled;
    public bool WasSkipped        => _installState == ModInstallState.Skipped;
    public bool WasFailed         => _installState == ModInstallState.Failed;
    public bool HasInstallOutcome => _installState != ModInstallState.None;
    public bool ShowInstallDetail => HasInstallOutcome && !WasAlreadyInstalled;

    // A skipped mod's reasons also land as validation errors; the red X and
    // InstallDetail already carry them, so the error triangle and error text
    // stand down while the skip is showing
    public bool ShowErrorIcon => InError && !WasSkipped;

    // Warnings and successful installs are deliberately compact: their full
    // detail lives in an icon tooltip or the conflict report. The expandable
    // row is reserved for an error that needs the user's immediate attention.
    public bool ShowPlainRow  => !InError && !WasSkipped && !WasFailed;
    public bool ShowStatusRow => !ShowPlainRow;

    public void SetInstallOutcome(ModInstallState state, string detail = "")
    {
        _installState = state;
        InstallDetail = detail;
        _installDetailIsSuccessMessage = state == ModInstallState.Installed;
        OnPropertyChanged(nameof(WasInstalled));
        OnPropertyChanged(nameof(WasAlreadyInstalled));
        OnPropertyChanged(nameof(InstallStatusTooltip));
        OnPropertyChanged(nameof(WasSkipped));
        OnPropertyChanged(nameof(WasFailed));
        OnPropertyChanged(nameof(HasInstallOutcome));
        OnPropertyChanged(nameof(ShowInstallDetail));
        OnPropertyChanged(nameof(InstallDetail));
        OnPropertyChanged(nameof(ShowErrorIcon));
        OnPropertyChanged(nameof(ShowPlainRow));
        OnPropertyChanged(nameof(ShowStatusRow));
    }

    /// <summary>
    /// Whether this exact version of the mod is in the rebuilt game archive right now.
    ///
    /// Deliberately separate from <see cref="_installState"/>, which records what happened to the
    /// mod during *this session*. The two used to be read as one, so a mod installed a moment ago
    /// was still counted as "will be added" - the selection summary only told the truth after AIM
    /// was closed and reopened, because that reset the session state.
    /// </summary>
    [NotifyPropertyChangedFor(nameof(IsPendingInstall))]
    [ObservableProperty] private bool _isInGameArchive;

    /// <summary>Ticked, but not yet in the game archive.</summary>
    public bool IsPendingInstall => Enabled && !IsInGameArchive;

    public void SetAlreadyInstalled(bool value)
    {
        if (value && _installState == ModInstallState.None)
        {
            _installState = ModInstallState.AlreadyInstalled;
            InstallDetail = Texts.GUIModAlreadyInstalled;
        }
        else if (!value && _installState == ModInstallState.AlreadyInstalled)
        {
            _installState = ModInstallState.None;
            InstallDetail = "";
        }
        else return;

        OnPropertyChanged(nameof(WasInstalled));
        OnPropertyChanged(nameof(WasAlreadyInstalled));
        OnPropertyChanged(nameof(InstallStatusTooltip));
        OnPropertyChanged(nameof(HasInstallOutcome));
        OnPropertyChanged(nameof(InstallDetail));
        OnPropertyChanged(nameof(ShowPlainRow));
        OnPropertyChanged(nameof(ShowStatusRow));
    }

    // An install can add validation messages (a skipped mod's reasons land as
    // errors); the expander re-reads them when told
    public void RefreshValidation()
    {
        // A revalidation follows a reread of the manifest, so the version on the row may have moved
        // with it - after an update, a rollback, or the user editing the manifest by hand.
        RefreshVersion();
        OnPropertyChanged(nameof(InWarning));
        OnPropertyChanged(nameof(HasDuplicateWarning));
        OnPropertyChanged(nameof(HasConflictWarning));
        OnPropertyChanged(nameof(InError));
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(Warnings));
        OnPropertyChanged(nameof(WarningTooltip));
        OnPropertyChanged(nameof(Errors));
        OnPropertyChanged(nameof(Enabled));
        OnPropertyChanged(nameof(ShowErrorIcon));
        OnPropertyChanged(nameof(ShowPlainRow));
        OnPropertyChanged(nameof(ShowStatusRow));
    }

    // Only validation messages are rebuilt on a language switch. Conflict and
    // compatibility messages are regenerated by the background conflict pass;
    // treating every selected conflict as a validation target used to reopen
    // almost every archive and could stall the UI with a large mod list.
    public bool NeedsLocalizedValidation => Mod.GetValidation().Status != ValidationStatus.Valid;

    public void RefreshLocalizedText()
    {
        if (_installDetailIsSuccessMessage)
        {
            InstallDetail = Resources.GUIModInstalled;
            OnPropertyChanged(nameof(InstallDetail));
        }
        else if (WasAlreadyInstalled)
        {
            InstallDetail = Texts.GUIModAlreadyInstalled;
            OnPropertyChanged(nameof(InstallDetail));
        }
        OnPropertyChanged(nameof(InstallStatusTooltip));
        // Validation state does not change merely because the display
        // language changed. Avoid notifying every status binding here; with
        // a large mod list those redundant notifications force Avalonia to
        // measure and arrange every row repeatedly.
        var full = BuildFull();
        if (!string.Equals(_full, full, StringComparison.Ordinal))
        {
            _full = full;
            OnPropertyChanged(nameof(Full));
            // Title is the same text without the version, so it is stale for exactly the same
            // reasons Full is - and the row shows Title, not Full.
            OnPropertyChanged(nameof(Title));
        }

        var description = Mod.GetDisplayDescription(Localization.LanguageCode);
        if (!string.Equals(_description, description, StringComparison.Ordinal))
        {
            _description = description;
            OnPropertyChanged(nameof(Description));
            OnPropertyChanged(nameof(HasDescription));
        }
        if (UpdateAvailable)
            OnPropertyChanged(nameof(UpdateTooltip));

        // Duplicate and validation tooltip text is localized here. Conflict
        // and compatibility tooltip text is refreshed by the background pass
        // that owns those warning sources.
        if (HasDuplicateWarning || NeedsLocalizedValidation)
        {
            OnPropertyChanged(nameof(Warnings));
            OnPropertyChanged(nameof(WarningTooltip));
        }
    }

    public void RevalidateForLocalization()
    {
        Mod.Validate();
        ModInstaller.ValidateMods(new List<IMod> { Mod });
    }

    private string BuildFull() => string.Format(Resources.GUIModByAuthorWithVersion,
        Mod.GetDisplayName(Localization.LanguageCode), Mod.GetAuthor(), Mod.GetVersion());

    public string Full => _full;

    public string Description => _description;

    public bool HasDescription => !string.IsNullOrWhiteSpace(_description);

    /// <summary>Name and author. The version is shown separately so it can be spaced away from it.</summary>
    public string Title => string.Format(Texts.GUIModByAuthor,
        Mod.GetDisplayName(Localization.LanguageCode), Mod.GetAuthor());

    /// <summary>
    /// The version out of the mod's own manifest - what is actually on disk, which is not always
    /// what the mod's Nexus page calls the same release.
    ///
    /// Authors often prefix their own "v", so one is only added when it is missing; "vv3.0" reads
    /// like a typo in AIM rather than the mod's own numbering.
    /// </summary>
    public string InstalledVersion
    {
        get
        {
            var version = Mod.GetVersion();
            if (string.IsNullOrWhiteSpace(version)) return "";

            return version.StartsWith('v') || version.StartsWith('V')
                ? version
                : $"v{version}";
        }
    }

    /// <summary>
    /// Re-reads what the manifest says. Called after anything that can rewrite a mod in place - an
    /// update, a rollback, or the user editing the manifest from the row's own menu.
    /// </summary>
    public void RefreshVersion()
    {
        // Full is cached, so re-reading the manifest has to refill the cache rather than merely
        // announce it: a rollback that changed the version on disk would otherwise keep showing
        // the version the row was built with.
        _full = BuildFull();
        OnPropertyChanged(nameof(Full));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(InstalledVersion));
    }

    public string UpdateTooltip
    {
        get
        {
            var version = LatestVersion is null ? "" : $": v{LatestVersion}";

            // Say which of the two things the badge will actually do. They are very different -
            // one replaces the mod, the other opens a browser - and the button looks identical.
            return CanUpdateFromNexus
                ? $"{Texts.GUIUpdateMod}{version}\n{Texts.GUIUpdateBadgeInstalls}"
                : $"{Texts.GUIUpdateMod}{version}\n{Texts.GUIUpdateBadgeOpensPage}";
        }
    }

    /// <summary>
    /// What the update badge does.
    ///
    /// It used to always open the mod's web page, which made the one obvious button in the row the
    /// only path that could not install anything - the automatic download lived in the right-click
    /// menu, where nobody looked. When AIM knows which Nexus file the update is, the badge now runs
    /// that update. Opening the page stays the fallback for a mod AIM only has a URL for, such as a
    /// GitHub release found by the manifest check.
    /// </summary>
    [RelayCommand]
    private void ApplyUpdate()
    {
        if (CanUpdateFromNexus && Commands is not null)
        {
            Commands.UpdateFromNexus.Execute(this);
            return;
        }

        // Via ExternalUrl rather than ShellExecute directly: this is a synchronous command, and a
        // machine with no https handler registered throws a Win32Exception that would take the app
        // down rather than merely failing to open a page.
        ExternalUrl.Open(UpdateDownloadUrl ?? Mod.GetDownloadUrl());
    }
}
