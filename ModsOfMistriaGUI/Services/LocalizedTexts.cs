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
    public string GUIModSearch => T("GUIModSearch");
    public string GUIClearModSearch => T("GUIClearModSearch");
    public string GUIToggleAllMods => T("GUIToggleAllMods");
    public string GUISuggestLoadOrder => T("GUISuggestLoadOrder");
    public string GUISuggestLoadOrderTooltip => T("GUISuggestLoadOrderTooltip");
    public string GUIReportConflicts => T("GUIReportConflicts");
    public string GUIConflictReportTitle => T("GUIConflictReportTitle");
    public string GUILoadOrderTitle => T("GUILoadOrderTitle");
    public string GUILoadOrderChanged => T("GUILoadOrderChanged");
    public string GUILoadOrderAlreadyGood => T("GUILoadOrderAlreadyGood");
    public string GUIShowDetails => T("GUIShowDetails");
    public string GUIClose => T("GUIClose");
    public string GUIConflictDetailsHint => T("GUIConflictDetailsHint");
    public string GUICopyReport => T("GUICopyReport");

    // Nexus "Mod Manager Download" support
    public string GUINexusMenu => T("GUINexusMenu");
    public string GUINexusHandlerMenuItem => T("GUINexusHandlerMenuItem");
    public string GUINexusApiKeyMenuItem => T("GUINexusApiKeyMenuItem");
    public string GUINexusPasteLinkMenuItem => T("GUINexusPasteLinkMenuItem");
    public string GUINexusDownloadsHeader => T("GUINexusDownloadsHeader");
    public string GUINexusClearFinished => T("GUINexusClearFinished");

    // Settings page
    public string GUISettingsMenu => T("GUISettingsMenu");
    public string GUISettingsBack => T("GUISettingsBack");
    public string GUISettingsGeneral => T("GUISettingsGeneral");
    public string GUISettingsGeneralDescription => T("GUISettingsGeneralDescription");
    public string GUISettingsGameFolder => T("GUISettingsGameFolder");
    public string GUISettingsModsFolder => T("GUISettingsModsFolder");
    public string GUISettingsChooseModsFolder => T("GUISettingsChooseModsFolder");
    public string GUISettingsModsFolderNote => T("GUISettingsModsFolderNote");
    public string GUISettingsNexus => T("GUISettingsNexus");
    public string GUISettingsNexusDescription => T("GUISettingsNexusDescription");
    public string GUISettingsApiKeyStatus => T("GUISettingsApiKeyStatus");
    public string GUISettingsApiKeyButton => T("GUISettingsApiKeyButton");
    public string GUISettingsNexusNote => T("GUISettingsNexusNote");
    public string GUISettingsSelectModsFolderTitle => T("GUISettingsSelectModsFolderTitle");

    // Row context menu: editing a mod's own files, and removing it
    public string GUIEditManifest => T("GUIEditManifest");
    public string GUIEditManifestTooltip => T("GUIEditManifestTooltip");
    public string GUIEditConfig => T("GUIEditConfig");
    public string GUIEditConfigTooltip => T("GUIEditConfigTooltip");
    public string GUIEditFileFailed => T("GUIEditFileFailed");
    public string GUIRemoveMod => T("GUIRemoveMod");
    public string GUIRemoveModTooltip => T("GUIRemoveModTooltip");
    public string GUIRemoveModTitle => T("GUIRemoveModTitle");
    public string GUIRemoveModConfirm => T("GUIRemoveModConfirm");
    public string GUIRemoveModConfirmPermanent => T("GUIRemoveModConfirmPermanent");
    public string GUIRemoveModFailed => T("GUIRemoveModFailed");
    public string GUIRemoveModRefused => T("GUIRemoveModRefused");
    public string GUIRemoveSelected => T("GUIRemoveSelected");
    public string GUIRemoveSelectedTooltip => T("GUIRemoveSelectedTooltip");
    public string GUIRemoveSelectedNone => T("GUIRemoveSelectedNone");
    public string GUIRemoveSelectedConfirm => T("GUIRemoveSelectedConfirm");
    public string GUIRemoveSelectedConfirmPermanent => T("GUIRemoveSelectedConfirmPermanent");
    public string GUIRemoveSelectedMore => T("GUIRemoveSelectedMore");
    public string GUIRemoveSelectedDone => T("GUIRemoveSelectedDone");
    public string GUIRemoveSelectedFailed => T("GUIRemoveSelectedFailed");
    public string GUIRemoveModMissing => T("GUIRemoveModMissing");

    // Conflict report: marking an issue as one the user has checked and accepted
    public string GUIIssueDismissTooltip => T("GUIIssueDismissTooltip");
    public string GUIIssueShowDismissed => T("GUIIssueShowDismissed");
    public string GUIIssueDismissedHeader => T("GUIIssueDismissedHeader");
    public string GUIIssueDismissedMarker => T("GUIIssueDismissedMarker");

    // Conflict report: deciding which mod wins a shared-file conflict
    public string GUIConflictSharedFilesHeader => T("GUIConflictSharedFilesHeader");
    public string GUIConflictCurrentWinner => T("GUIConflictCurrentWinner");
    public string GUIConflictMakeThisWin => T("GUIConflictMakeThisWin");
    public string GUIConflictMakeThisWinTooltip => T("GUIConflictMakeThisWinTooltip");
    public string GUIConflictWinnerNow => T("GUIConflictWinnerNow");

    // Conflict report: rebinding a contested keyboard shortcut
    public string GUIHotkeyRebindButton => T("GUIHotkeyRebindButton");
    public string GUIHotkeyRebindTooltip => T("GUIHotkeyRebindTooltip");
    public string GUIHotkeyRebindTitle => T("GUIHotkeyRebindTitle");
    public string GUIHotkeyRebindConfirm => T("GUIHotkeyRebindConfirm");
    public string GUIHotkeyRebindDone => T("GUIHotkeyRebindDone");
    public string GUIHotkeyRebindFailed => T("GUIHotkeyRebindFailed");
    public string GUIHotkeyRebound => T("GUIHotkeyRebound");
    public string GUIHotkeyReboundResolved => T("GUIHotkeyReboundResolved");
    public string GUIHotkeyBlockedArchive => T("GUIHotkeyBlockedArchive");
    public string GUIHotkeyBlockedNotDeclared => T("GUIHotkeyBlockedNotDeclared");
    public string GUIHotkeyBlockedNoFreeKeys => T("GUIHotkeyBlockedNoFreeKeys");
    public string GUIHotkeyBlockedUnreadable => T("GUIHotkeyBlockedUnreadable");

    // Conflict report: researching whether a conflict actually matters
    public string GUIConflictFindAFix => T("GUIConflictFindAFix");
    public string GUIConflictFindAFixTooltip => T("GUIConflictFindAFixTooltip");
    public string GUIResearchTitle => T("GUIResearchTitle");
    public string GUIResearchWorking => T("GUIResearchWorking");
    public string GUIResearchNoApiKey => T("GUIResearchNoApiKey");
    public string GUIResearchNothingFound => T("GUIResearchNothingFound");
    public string GUIResearchFoundCount => T("GUIResearchFoundCount");
    public string GUIResearchFailed => T("GUIResearchFailed");
    public string GUIResearchFindingsHeader => T("GUIResearchFindingsHeader");
    public string GUIResearchLinksHeader => T("GUIResearchLinksHeader");
    public string GUIResearchLinksHint => T("GUIResearchLinksHint");
    public string GUIResearchOpenPage => T("GUIResearchOpenPage");
    public string GUIResearchOpenPatch => T("GUIResearchOpenPatch");
    public string GUIResearchVerdictHeader => T("GUIResearchVerdictHeader");
    public string GUIResearchVerdictHint => T("GUIResearchVerdictHint");
    public string GUIResearchPatchLinkLabel => T("GUIResearchPatchLinkLabel");
    public string GUIResearchNotAnIssue => T("GUIResearchNotAnIssue");
    public string GUIResearchNotAnIssueTooltip => T("GUIResearchNotAnIssueTooltip");
    public string GUIResearchPatchExists => T("GUIResearchPatchExists");
    public string GUIResearchPatchExistsTooltip => T("GUIResearchPatchExistsTooltip");
    public string GUIResearchIncompatible => T("GUIResearchIncompatible");
    public string GUIResearchIncompatibleTooltip => T("GUIResearchIncompatibleTooltip");
    public string GUIResearchUndecided => T("GUIResearchUndecided");
    public string GUIResearchPatchConfirm => T("GUIResearchPatchConfirm");
    public string GUIResearchPatchLinkInvalid => T("GUIResearchPatchLinkInvalid");
    public string GUIConflictReportNothing => T("GUIConflictReportNothing");

    public string GUIRowBindingClash => T("GUIRowBindingClash");
    public string GUIModByAuthor => T("GUIModByAuthor");

    // Viewing the list: sorting, filtering and jumping. None of these touches the load order.
    public string GUISortAlphabetically => T("GUISortAlphabetically");
    public string GUISortAlphabeticallyTooltip => T("GUISortAlphabeticallyTooltip");
    public string GUISortRecentlyUpdated => T("GUISortRecentlyUpdated");
    public string GUISortRecentlyUpdatedTooltip => T("GUISortRecentlyUpdatedTooltip");
    public string GUIShowOnlyUpdatable => T("GUIShowOnlyUpdatable");
    public string GUIShowOnlyUpdatableTooltip => T("GUIShowOnlyUpdatableTooltip");
    public string GUIListReorderedNote => T("GUIListReorderedNote");
    public string GUIScrollToTop => T("GUIScrollToTop");
    public string GUIScrollToBottom => T("GUIScrollToBottom");

    // A mod's release notes
    public string GUIChangelogTitle => T("GUIChangelogTitle");
    public string GUIChangelogLoading => T("GUIChangelogLoading");
    public string GUIChangelogPreview => T("GUIChangelogPreview");
    public string GUIChangelogNone => T("GUIChangelogNone");
    public string GUIChangelogUnavailable => T("GUIChangelogUnavailable");
    public string GUIChangelogVersionCount => T("GUIChangelogVersionCount");
    public string GUIChangelogVersionHeading => T("GUIChangelogVersionHeading");

    // Rolling back to an archived copy
    public string GUIVersionsBadge => T("GUIVersionsBadge");

    // Edits AIM made inside a mod, and the research window's diagnosis and fixes.
    public string GUIModEditedBadge => T("GUIModEditedBadge");
    public string GUIModEditedTooltip => T("GUIModEditedTooltip");
    public string GUIModEditedUndoHint => T("GUIModEditedUndoHint");
    public string GUIModUndoAimEdits => T("GUIModUndoAimEdits");
    public string GUIResearchDiagnosisHeader => T("GUIResearchDiagnosisHeader");
    public string GUIResearchDiagnosisCertain => T("GUIResearchDiagnosisCertain");
    public string GUIResearchDiagnosisUncertain => T("GUIResearchDiagnosisUncertain");
    public string GUIResearchFixesHeader => T("GUIResearchFixesHeader");
    public string GUIResearchFixesHint => T("GUIResearchFixesHint");
    public string GUIResearchPatchesHeader => T("GUIResearchPatchesHeader");
    public string GUIResearchApply => T("GUIResearchApply");
    public string GUIResearchApplied => T("GUIResearchApplied");
    public string GUIResearchApplyFailed => T("GUIResearchApplyFailed");
    public string GUIResearchEvidenceAgainst => T("GUIResearchEvidenceAgainst");
    public string GUIResearchEvidenceFor => T("GUIResearchEvidenceFor");
    public string GUIResearchEvidenceOther => T("GUIResearchEvidenceOther");
    public string GUIResearchSetAsideWarning => T("GUIResearchSetAsideWarning");
    public string GUIVersionsTooltip => T("GUIVersionsTooltip");
    public string GUIVersionsHeader => T("GUIVersionsHeader");

    // The update badge, and explaining a refused download honestly
    public string GUIUpdateBadgeInstalls => T("GUIUpdateBadgeInstalls");
    public string GUIUpdateBadgeOpensPage => T("GUIUpdateBadgeOpensPage");
    public string GUINexusAccountPremium => T("GUINexusAccountPremium");
    public string GUINexusAccountFree => T("GUINexusAccountFree");
    public string GUINexusAccountNoKey => T("GUINexusAccountNoKey");
    public string GUINexusAccountUnknown => T("GUINexusAccountUnknown");

    // Keybind manager
    public string GUIKeybindsButton => T("GUIKeybindsButton");
    public string GUIKeybindsButtonTooltip => T("GUIKeybindsButtonTooltip");
    public string GUIKeybindsTitle => T("GUIKeybindsTitle");
    public string GUIKeybindsIntro => T("GUIKeybindsIntro");
    public string GUIKeybindsSummary => T("GUIKeybindsSummary");
    public string GUIKeybindsOnlyOverlaps => T("GUIKeybindsOnlyOverlaps");
    public string GUIKeybindsRefresh => T("GUIKeybindsRefresh");
    public string GUIKeybindsScanning => T("GUIKeybindsScanning");
    public string GUIKeybindsScanFailed => T("GUIKeybindsScanFailed");
    public string GUIKeybindsNone => T("GUIKeybindsNone");
    public string GUIKeybindsNoOverlaps => T("GUIKeybindsNoOverlaps");
    public string GUIKeybindsNoConfigDirectory => T("GUIKeybindsNoConfigDirectory");
    public string GUIKeybindsGameRunning => T("GUIKeybindsGameRunning");
    public string GUIKeybindsAlsoUsedBy => T("GUIKeybindsAlsoUsedBy");
    public string GUIKeybindsClashHeader => T("GUIKeybindsClashHeader");
    public string GUIKeybindsDefault => T("GUIKeybindsDefault");
    public string GUIKeybindsDefaultTooltip => T("GUIKeybindsDefaultTooltip");
    public string GUIKeybindsUnbound => T("GUIKeybindsUnbound");
    public string GUIKeybindsUnrecognised => T("GUIKeybindsUnrecognised");
    public string GUIKeybindsUnrecognisedTooltip => T("GUIKeybindsUnrecognisedTooltip");
    public string GUIKeybindsWriteFailed => T("GUIKeybindsWriteFailed");

    // Choosing one binding
    public string GUIBindingEditorTitle => T("GUIBindingEditorTitle");
    public string GUIBindingEditorTrigger => T("GUIBindingEditorTrigger");
    public string GUIBindingEditorCapture => T("GUIBindingEditorCapture");
    public string GUIBindingEditorCapturing => T("GUIBindingEditorCapturing");
    public string GUIBindingEditorPreview => T("GUIBindingEditorPreview");
    public string GUIBindingEditorNote => T("GUIBindingEditorNote");
    public string GUIBindingEditorSave => T("GUIBindingEditorSave");
    public string GUIBindingEditorClear => T("GUIBindingEditorClear");
    public string GUIBindingEditorClearTooltip => T("GUIBindingEditorClearTooltip");

    // Keeping the user's bindings across a mod's own settings reset
    public string GUIBindingIsDefault => T("GUIBindingIsDefault");
    public string GUIBindingRestoreTitle => T("GUIBindingRestoreTitle");
    public string GUIBindingRestorePrompt => T("GUIBindingRestorePrompt");
    public string GUIBindingRestoreMore => T("GUIBindingRestoreMore");
    public string GUIBindingRestoreDone => T("GUIBindingRestoreDone");

    // Update checking from the mod list header
    public string GUICheckUpdatesButton => T("GUICheckUpdatesButton");
    public string GUICheckUpdatesButtonTooltip => T("GUICheckUpdatesButtonTooltip");
    public string GUICheckForUpdatesResultWithOffer => T("GUICheckForUpdatesResultWithOffer");
    public string GUIUpdateAllNow => T("GUIUpdateAllNow");
    public string GUIUpdateAllNowTooltip => T("GUIUpdateAllNowTooltip");
    public string GUIUpdateAllTitle => T("GUIUpdateAllTitle");
    public string GUIUpdateAllNothing => T("GUIUpdateAllNothing");
    public string GUIUpdateAllConfirm => T("GUIUpdateAllConfirm");
    public string GUIUpdateAllProgress => T("GUIUpdateAllProgress");
    public string GUIUpdateAllDone => T("GUIUpdateAllDone");
    public string GUIUpdateAllFailed => T("GUIUpdateAllFailed");
    // Folders watched for mods downloaded by hand
    public string GUIDropFolderMenu => T("GUIDropFolderMenu");
    public string GUIDropFolderExplain => T("GUIDropFolderExplain");
    public string GUIDropFolderLink => T("GUIDropFolderLink");
    public string GUIDropFolderPickTitle => T("GUIDropFolderPickTitle");
    public string GUIDropFolderScanNow => T("GUIDropFolderScanNow");
    public string GUIDropFolderNone => T("GUIDropFolderNone");
    public string GUIDropFolderUnlinkTooltip => T("GUIDropFolderUnlinkTooltip");
    public string GUIDropFolderTitle => T("GUIDropFolderTitle");
    public string GUIDropFolderNothingFound => T("GUIDropFolderNothingFound");
    public string GUIDropFolderImported => T("GUIDropFolderImported");
    public string GUIDropFolderImportedDetail => T("GUIDropFolderImportedDetail");
    public string GUIDropFolderNoModsFolder => T("GUIDropFolderNoModsFolder");
    public string GUIDropFolderCannotOpen => T("GUIDropFolderCannotOpen");
    public string GUIOpenModsFolderButton => T("GUIOpenModsFolderButton");
    public string GUIOpenModsFolderButtonTooltip => T("GUIOpenModsFolderButtonTooltip");

    public string GUIVerdictNotAnIssue => T("GUIVerdictNotAnIssue");
    public string GUIVerdictPatchExists => T("GUIVerdictPatchExists");
    public string GUIVerdictIncompatible => T("GUIVerdictIncompatible");
    public string GUIVerdictRebound => T("GUIVerdictRebound");
}
