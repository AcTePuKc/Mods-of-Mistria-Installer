using CommunityToolkit.Mvvm.ComponentModel;
using System.Diagnostics;
using Garethp.ModsOfMistriaInstallerLib;

namespace Garethp.ModsOfMistriaGUI.Services;

/// <summary>
/// Bindable view-facing resource properties. Avalonia compiled bindings do
/// not reliably refresh an indexer when a ResourceManager culture changes, so
/// each visible string has a normal property and an explicit notification.
/// </summary>
public sealed class LocalizedTexts : ObservableObject
{
    public static LocalizedTexts Instance { get; } = new();

    private LocalizedTexts()
    {
        LocalizationService.Instance.LanguageChanged += (_, _) =>
        {
            // One broadcast is enough. Sending one notification per label
            // makes Avalonia measure and arrange the whole window repeatedly.
            var stopwatch = Stopwatch.StartNew();
            OnPropertyChanged((string?)null);
            PerformanceDiagnostics.Log($"Language refresh: global localized bindings={stopwatch.ElapsedMilliseconds} ms");
        };
    }

    private static string T(string key) => LocalizationService.Instance[key];

    public string GUIApplicationTitle => T("GUIApplicationTitle");
    public string GUIReloadModlist => T("GUIReloadModlist");
    public string GUISaveLogFile => T("GUISaveLogFile");
    public string GUIEnableAllMods => T("GUIEnableAllMods");
    public string GUIDisableAllMods => T("GUIDisableAllMods");
    public string GUIUninstallButtonText => T("GUIUninstallButtonText");
    public string GUIFieldsOfMistriaDetectedLocation => T("GUIFieldsOfMistriaDetectedLocation");
    public string GUIPlayButtonText => T("GUIPlayButtonText");
    public string GUIProfileLabel => T("GUIProfileLabel");
    public string GUILaunchGameDirectly => T("GUILaunchGameDirectly");
    public string GUIGitHubButtonText => T("GUIGitHubButtonText");
    public string GUIInstallFailedForMod => T("GUIInstallFailedForMod");
    public string GUILanguageMenu => T("GUILanguageMenu");
    public string GUILanguageSystem => T("GUILanguageSystem");
    public string GUILanguageEnglish => T("GUILanguageEnglish");
    public string GUILanguageBulgarian => T("GUILanguageBulgarian");
    public string GUILanguagePolish => T("GUILanguagePolish");
    public string GUILanguageGerman => T("GUILanguageGerman");
    public string GUILanguageFrench => T("GUILanguageFrench");
    public string GUILanguageDutch => T("GUILanguageDutch");
    public string GUILanguagePortugueseBrazil => T("GUILanguagePortugueseBrazil");
    public string GUILanguageRussian => T("GUILanguageRussian");
    public string GUILanguageIndonesian => T("GUILanguageIndonesian");
    public string GUILanguageSimplifiedChinese => T("GUILanguageSimplifiedChinese");
    public string GUILanguageTraditionalChinese => T("GUILanguageTraditionalChinese");
    public string GUILanguageKorean => T("GUILanguageKorean");
    public string GUILanguageJapanese => T("GUILanguageJapanese");
    public string GUILanguageSpanish => T("GUILanguageSpanish");
    public string GUILanguageUkrainian => T("GUILanguageUkrainian");
    public string GUINewProfile => T("GUINewProfile");
    public string GUIDeleteCurrentProfile => T("GUIDeleteCurrentProfile");
    public string GUIMoveUp => T("GUIMoveUp");
    public string GUIMoveDown => T("GUIMoveDown");
    public string GUIUpdateMod => T("GUIUpdateMod");
    public string GUIConfirmSaveProfileTitle => T("GUIConfirmSaveProfileTitle");
    public string GUIConfirmSaveProfileMessage => T("GUIConfirmSaveProfileMessage");
    public string GUIConfirmDeleteProfileTitle => T("GUIConfirmDeleteProfileTitle");
    public string GUIConfirmDeleteProfileMessage => T("GUIConfirmDeleteProfileMessage");
    public string GUIProfileName => T("GUIProfileName");
    public string GUIMissingRequirementsTitle => T("GUIMissingRequirementsTitle");
    public string GUIMissingRequirementsMessage => T("GUIMissingRequirementsMessage");
    public string GUIOpenExternalLinksTitle => T("GUIOpenExternalLinksTitle");
    public string GUIOpenExternalLinksMessage => T("GUIOpenExternalLinksMessage");
    public string GUIMissingRequirementsManual => T("GUIMissingRequirementsManual");
    public string GUIErrorReason => T("GUIErrorReason");
    public string GUIErrorDetailsSaved => T("GUIErrorDetailsSaved");
    public string GUIOpenGameExecutable => T("GUIOpenGameExecutable");
    public string GUIOpenModsFolder => T("GUIOpenModsFolder");
    public string GUISetupMistriaLocation => T("GUISetupMistriaLocation");
    public string GUISetupModsLocation => T("GUISetupModsLocation");
    public string GUICanCreateModsFolder => T("GUICanCreateModsFolder");
    public string GUIErrorWrongMistriaVersion => T("GUIErrorWrongMistriaVersion");
    public string GUIModHasWarnings => T("GUIModHasWarnings");
    public string GUIModDuplicateCopies => T("GUIModDuplicateCopies");
    public string GUIDuplicateModInstallBlocked => T("GUIDuplicateModInstallBlocked");
    public string GUIModConflicts => T("GUIModConflicts");
    public string GUIModFileConflicts => T("GUIModFileConflicts");
    public string GUIModLegacyGml => T("GUIModLegacyGml");
    public string GUIModLegacyGamePatch => T("GUIModLegacyGamePatch");
    public string GUIModLegacyGamePatchError => T("GUIModLegacyGamePatchError");
    public string GUIModHotkeyConflicts => T("GUIModHotkeyConflicts");
    public string GUIHotkeyRebindable => T("GUIHotkeyRebindable");
    public string GUIFileConflictReplacement => T("GUIFileConflictReplacement");
    public string GUIFileConflictMerge => T("GUIFileConflictMerge");
    public string GUIFileConflictLocalization => T("GUIFileConflictLocalization");
    public string GUIFileConflictShared => T("GUIFileConflictShared");
    public string GUIModHasErrors => T("GUIModHasErrors");
    public string GUIModSkipped => T("GUIModSkipped");
    public string GUIModInstalled => T("GUIModInstalled");
    public string GUIModAlreadyInstalled => T("GUIModAlreadyInstalled");

    // Nexus updates, freezing and backups
    public string GUIOpenNexusPage => T("GUIOpenNexusPage");
    public string GUIOpenModFolder => T("GUIOpenModFolder");
    public string GUICheckForUpdate => T("GUICheckForUpdate");
    public string GUIUpdateFromNexus => T("GUIUpdateFromNexus");
    public string GUINexusAssociate => T("GUINexusAssociate");
    public string GUINexusAssociateTitle => T("GUINexusAssociateTitle");
    public string GUINexusAssociateInvalid => T("GUINexusAssociateInvalid");
    public string GUINexusAssociateWrongGame => T("GUINexusAssociateWrongGame");
    public string GUIFreezeMod => T("GUIFreezeMod");
    public string GUIUnfreezeMod => T("GUIUnfreezeMod");
    public string GUIModFrozenTooltip => T("GUIModFrozenTooltip");
    public string GUIRestorePreviousVersion => T("GUIRestorePreviousVersion");
    public string GUIRestoreVersionTitle => T("GUIRestoreVersionTitle");
    public string GUIRestoreVersionNone => T("GUIRestoreVersionNone");
    public string GUIRestoreVersionConfirm => T("GUIRestoreVersionConfirm");
    public string GUICheckSelectedForUpdates => T("GUICheckSelectedForUpdates");
    public string GUICheckAllForUpdates => T("GUICheckAllForUpdates");
    public string GUICheckForUpdatesTitle => T("GUICheckForUpdatesTitle");
    public string GUICheckingForUpdates => T("GUICheckingForUpdates");
    public string GUICheckForUpdatesResult => T("GUICheckForUpdatesResult");
    public string GUICheckForUpdatesFailed => T("GUICheckForUpdatesFailed");
    public string GUIUpdateAvailableForMod => T("GUIUpdateAvailableForMod");
    public string GUIModIsUpToDate => T("GUIModIsUpToDate");
    public string GUIModIsFrozen => T("GUIModIsFrozen");
    public string GUIModNotFromNexus => T("GUIModNotFromNexus");
    public string GUIUpdateModTitle => T("GUIUpdateModTitle");
    public string GUIUpdateModConfirm => T("GUIUpdateModConfirm");
    public string GUINexusUpdateNeedsPage => T("GUINexusUpdateNeedsPage");
    public string GUINexusHandlerOffer => T("GUINexusHandlerOffer");

    // Mod list header and load order
    public string GUIModSelectionSummary => T("GUIModSelectionSummary");
    public string GUIToggleAllMods => T("GUIToggleAllMods");
    public string GUISuggestLoadOrder => T("GUISuggestLoadOrder");
    public string GUISuggestLoadOrderTooltip => T("GUISuggestLoadOrderTooltip");
    public string GUILoadOrderTitle => T("GUILoadOrderTitle");
    public string GUILoadOrderChanged => T("GUILoadOrderChanged");
    public string GUILoadOrderAlreadyGood => T("GUILoadOrderAlreadyGood");

    // Nexus "Mod Manager Download" support
    public string GUINexusMenu => T("GUINexusMenu");
    public string GUINexusHandlerMenuItem => T("GUINexusHandlerMenuItem");
    public string GUINexusApiKeyMenuItem => T("GUINexusApiKeyMenuItem");
    public string GUINexusPasteLinkMenuItem => T("GUINexusPasteLinkMenuItem");
    public string GUINexusDownloadsHeader => T("GUINexusDownloadsHeader");
    public string GUINexusClearFinished => T("GUINexusClearFinished");
}
