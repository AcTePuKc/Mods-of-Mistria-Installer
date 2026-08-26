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
    
    private bool _enabledBacking;

    [ObservableProperty] private int _position;

    public bool IsAlternateRow => Position % 2 == 0;

    partial void OnPositionChanged(int value)
        => OnPropertyChanged(nameof(IsAlternateRow));

    // Set by UpdateChecker after startup — true when a newer release is available
    [NotifyPropertyChangedFor(nameof(CanUpdateFromNexus))]
    [ObservableProperty] private bool _updateAvailable;
    [ObservableProperty] private string? _latestVersion;
    [ObservableProperty] private string? _updateDownloadUrl;

    // ── Nexus ────────────────────────────────────────────────────────────────────

    /// <summary>The mod's page, when AIM knows which Nexus mod this is.</summary>
    [NotifyPropertyChangedFor(nameof(IsFromNexus))]
    [ObservableProperty] private string? _nexusPageUrl;

    /// <summary>The file id an update would install. Null when the update cannot be fetched.</summary>
    [NotifyPropertyChangedFor(nameof(CanUpdateFromNexus))]
    [ObservableProperty] private int? _updateFileId;

    [ObservableProperty] private bool _contextActionsLocked;

    /// <summary>The user asked for this mod to be left on the version it is on.</summary>
    [ObservableProperty] private bool _isFrozen;

    [ObservableProperty] private bool _isCheckingUpdate;

    /// <summary>A previous version is in the backup store and can be restored.</summary>
    [ObservableProperty] private bool _hasBackup;

    /// <summary>The right-click actions, supplied by the page view model.</summary>
    public ModRowCommands? Commands { get; set; }

    public bool IsFromNexus => !string.IsNullOrEmpty(NexusPageUrl);

    public bool CanOpenNexusPage => IsFromNexus && !ContextActionsLocked;
    public bool CanAssociateWithNexus => !IsFromNexus && !ContextActionsLocked;
    public bool CanCheckForUpdate => !ContextActionsLocked;

    public bool CanUpdateFromNexus => UpdateAvailable && UpdateFileId is not null && !ContextActionsLocked;
    public bool CanToggleFreeze => !ContextActionsLocked;
    public bool CanRestorePreviousVersion => HasBackup && !ContextActionsLocked;
    public bool CanOpenModFolder => !ContextActionsLocked;

    public string FreezeMenuHeader => IsFrozen ? Texts.GUIUnfreezeMod : Texts.GUIFreezeMod;

    partial void OnIsFrozenChanged(bool value) => OnPropertyChanged(nameof(FreezeMenuHeader));

    partial void OnContextActionsLockedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanOpenNexusPage));
        OnPropertyChanged(nameof(CanAssociateWithNexus));
        OnPropertyChanged(nameof(CanCheckForUpdate));
        OnPropertyChanged(nameof(CanUpdateFromNexus));
        OnPropertyChanged(nameof(CanToggleFreeze));
        OnPropertyChanged(nameof(CanRestorePreviousVersion));
        OnPropertyChanged(nameof(CanOpenModFolder));
    }

    partial void OnNexusPageUrlChanged(string? value)
    {
        OnPropertyChanged(nameof(CanOpenNexusPage));
        OnPropertyChanged(nameof(CanAssociateWithNexus));
    }

    partial void OnHasBackupChanged(bool value)
        => OnPropertyChanged(nameof(CanRestorePreviousVersion));

    public ModModel(IMod mod)
    {
        Mod = mod;
        _enabledBacking = mod.IsInstalled();
    }

    public ModModel()
    {
        Mod = new FolderMod();
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
            OnPropertyChanged(nameof(HasDuplicateWarning));
            OnPropertyChanged(nameof(InWarning));
            OnPropertyChanged(nameof(Warnings));
            OnPropertyChanged(nameof(WarningTooltip));
            OnPropertyChanged(nameof(ShowPlainRow));
            OnPropertyChanged(nameof(ShowStatusRow));
        }
    }

    public bool HasDuplicateWarning => Enabled && _duplicateCopies.Count > 1;
    // Compatibility warnings describe the mod itself and must remain visible
    // before selection. Ordinary conflict warnings describe the current
    // selection and are shown only for enabled mods.
    public bool HasConflictWarning =>
        (Enabled && _conflictWarnings.Count > 0) || _compatibilityWarnings.Count > 0;
    public bool HasInlineWarning =>
        Mod.GetValidation().Warnings.Any(w => !string.IsNullOrWhiteSpace(w.Message))
        || HasDuplicateWarning
        || _compatibilityWarnings.Count > 0;
    public bool InWarning => HasInlineWarning || HasConflictWarning;
    public bool InError   => Mod.GetValidation().Status == ValidationStatus.Invalid;
    public bool IsValid   => Mod.GetValidation().Status == ValidationStatus.Valid;

    public string Warnings
    {
        get
        {
            var warnings = Mod.GetValidation().Warnings.Select(w => w.Message).ToList();
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
        OnPropertyChanged(nameof(Full));
        OnPropertyChanged(nameof(Description));
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

    public string Full => string.Format(Resources.GUIModByAuthorWithVersion,
        Mod.GetDisplayName(Localization.LanguageCode), Mod.GetAuthor(), Mod.GetVersion());

    public string Description => Mod.GetDisplayDescription(Localization.LanguageCode);

    public string UpdateTooltip =>
        LatestVersion is null
            ? Texts.GUIUpdateMod
            : $"{Texts.GUIUpdateMod}: v{LatestVersion}";

    [RelayCommand]
    private void OpenUpdateUrl()
    {
        var url = UpdateDownloadUrl ?? Mod.GetDownloadUrl();
        if (!ExternalUrl.IsAllowed(url)) return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName        = url,
            UseShellExecute = true
        });
    }
}
