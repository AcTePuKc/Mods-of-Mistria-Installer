# Changelog

## 0.1.9 — 2026-09-02

### Nexus integration

- Replaced the application-side personal Nexus API-key path with OAuth 2.0 Authorization Code +
  PKCE. AIM uses Nexus's registered public client `alternative_installer_for_mistria`; no personal
  Nexus API key or OAuth client secret is included in the application.
- Added account connect, disconnect, token refresh, loopback callback validation, and OAuth state
  validation.
- Kept `nxm://` handler takeover opt-in and separate from account sign-in. AIM can ask before
  taking links from Vortex or another mod manager and forwards links to an already-running AIM
  instance.
- Fixed Nexus update badges so they start AIM's update flow instead of opening a file page and
  requiring a second manual click on the Vortex button.
- Added localized guidance for free Nexus accounts when Nexus refuses a direct API download:
  those accounts must use the website's **Mod Manager Download** / **Vortex** button, which supplies
  the short-lived download token.

### Installation safety and compatibility

- Added recoverable `assets.zip` state publication. If the archive is replaced but state publication
  fails, AIM retains a pending-state journal and completes recovery on the next AIM operation.
- Hardened downloaded Nexus archive extraction with entry and size limits, active cancellation, and
  all-or-nothing rollback for multi-mod bundles. If rollback cannot fully restore a mod, AIM reports
  the retained backup path for manual recovery.
- Fixed nested TOML tables and inline-array merging used by current content mods, including the
  previously failing `common.small_roll` shape.
- Added safer null handling for image metadata and generated asset information without weakening
  invalid-mod diagnostics.
- Updated support for the current MOMI manifest compatibility version 0.15.10; older compatible
  mods remain supported.

### Fields of Mistria 1.0.x and MMAPI

- Reconciled the MMAPI catalog with the stable upstream catalog for the current 1.0.4 assets:
  129 hooks, 141 seams, 3 engine fixes, and 1 call rewrite.
- Added stable animal production, breeding, adoption-variant, date cooldown/cutscene, preset-layout,
  crafting-refresh, and backplate-sprite compatibility points.
- Added fully wired `npc.created`, `pet.created`, and `animal.created` entity-creation events.
- Added current cosmetic compatibility for `legs_top`, corrected `back_gear` mappings, and
  refreshed cosmetic frame-count handling.
- Preserved MMAPI attribution and license notices in the release artifacts. Wheedle's bundled
  vanilla files remain outside AIM's supported compatibility scope.

### Testing

- Verified old-GML, MMAPI/GML, content-mod, and NXM workflows against the current 1.0.4 assets.
- Full release checks pass: 492 library tests, 7 GUI tests, seam/catalog validation, and Windows
  self-contained publish.

## 0.1.8

- Added immediate local mod-list search by localized or original name, author, description, and
  version. Filtering never rescans archives or contacts Nexus; it preserves the selected mods and
  their saved order. Drag-and-drop is temporarily paused while a filter is active so hidden rows
  cannot be reordered accidentally.
- Split load-order tools into **Suggest order** and **Check issues**. Suggest order now performs
  only safe dependency ordering; Check issues opens an on-demand, copyable report with exact shared
  paths, hook and keyboard-shortcut clashes, missing requirements, and compatibility warnings.
- Kept the normal mod list compact: warnings and successful-install status are shown as icons with
  hover details, while failed installs still show their full error inline.
- Removed redundant "Warning" prefixes from legacy compatibility details; the warning icon already
  provides that status.
- Fixed Select all / Deselect all for large mod lists. AIM now batches the selection change and
  performs the archive-state and conflict refresh once, instead of once per row.
- Added read-only validation for legacy `momi/outfit` cosmetics using AIM's generator contract.
  The installer reports missing or malformed generated cosmetic assets without modifying the mod.
- Stopped reporting the shared legacy cosmetic category icon as a file conflict. AIM registers the
  individual cosmetic assets and store entries independently, so that icon does not decide which
  cosmetic mod works.

## 0.1.7

- Fixed automatic `nxm://` re-registration when a portable AIM build is moved or replaced. Windows
  now compares the normalized full executable path instead of treating every `AIM.exe` as the
  same installation.
- Changed hard asset replacements from a blocking error to a warning. The selected load order now
  determines which cosmetic, house or farm replacement is written last.
- Added defensive null handling for several generated models, SDK metadata tables and TOML merge
  paths without changing the handling of invalid mod data.
- Cleared the remaining nullable/compiler warnings in the installer, archive parsers, TOML/atlas
  handling, JavaScript compiler/decompiler and test helpers. Removed two framework-provided package
  references that were no longer needed. Release build and runtime smoke-tested locally.
- Added drag-and-drop auto-scroll near the top and bottom edges of the mod list, so long load orders
  can be rearranged without repeatedly releasing and restarting the drag operation.

## 0.1.6 — 2026-08-24

- Updated all supported interface languages with the current Nexus, update, conflict and UI/UX
  messages.
- Removed leftover Nexus test controls, simulated update badges and other test-only UI experiments
  from the normal application build.
- Added manual Nexus association for locally installed mods, including exact file identification,
  same-version detection without a duplicate download, and confirmation before replacing another
  version.
- Fixed `nxm://` handler state, migration between AIM builds, and disabling Nexus/context actions
  while installation or uninstallation is running.
- Improved duplicate-source handling and load-order suggestions when a mod exists both as a folder
  and as an archive.
- Added atlas-less PNG replacement support with focused regression coverage and documentation.
- Documented the known limitation for older local installations that have no recorded Nexus file
  identity. Users can associate those mods manually before using update checks.
- When a manually associated mod is already on the current Nexus version, AIM now records the exact
  Nexus file identity without downloading the archive. Declining a replacement for a different
  version leaves the existing association unchanged.

## 0.1.5 — 2026-08-24

- Added support for the Nexus Mods **Mod Manager Download** button. AIM can register itself as the
  `nxm://` protocol handler on Windows and Linux, and a clicked link is handed to the window that is
  already open instead of starting a second one.
- Added Nexus download and update management: downloads show progress, can be cancelled, and are
  unpacked relative to the mod's own manifest, including archives with wrapper folders or several mods.
- Added per-user Nexus API-key storage and validation, plus an option to install a copied `nxm://` link
  for browsers that cannot launch an external handler.
- Added update checks for one mod, selected mods, or every mod, with update badges, freezing, rollback,
  previous-version backups, Nexus-page links, and mod-folder links.
- Added automatic watching of the selected mods folder so newly copied mods appear without restarting AIM.
- Added a select-all summary and a **Suggest order** tool that respects declared requirements and reports
  shared files, missing requirements, and dependency loops.
- Added the initial disabled Nexus OAuth/API distribution boundary for testing. Nexus update checks use
  the Nexus integration path rather than GitHub release checks until the application registration and
  authentication flow are approved.
- Updated `Tmds.DBus.Protocol` from the vulnerable `0.20.0` release to patched `0.21.3` to address
  CVE-2026-39959 / GHSA-xrw6-gwf8-vvr9 involving D-Bus signal spoofing, file-descriptor exhaustion,
  and malformed-message crashes.
- Updated Six Labors ImageSharp from `3.1.5` to `4.0.0`; the older release was reported with
  `GHSA-rxmq-m78w-7wmc`. ImageSharp 4.x requires the documented open-source license handling for
  local and CI builds.
- Updated the dungeon floor compatibility seam for Fields of Mistria 1.0.4.
- Kept the 0.1.3 compatibility, warning, archive-worker, hotkey, conflict-detection, and localized
  metadata changes unchanged.
- Fixed manual Nexus association: AIM can resolve the exact Nexus file ID and filename through the
  API, record an already installed matching version without downloading it again, and ask before
  replacing a different version.

## 0.1.4 — 2026-08-20

- Updated the dungeon floor compatibility seam for Fields of Mistria 1.0.4.

## 0.1.3 — 2026-08-17

- Added duplicate-source detection for the same logical mod when it exists as
  an extracted folder and as a ZIP/RAR archive; installation is blocked until
  only one copy is selected.
- Profiles now remember the physical source selected for duplicate mods, so
  AIM does not select both the folder and archive copy after restarting.
- Added an installed-state indicator that survives restart and follows the
  actual installed source; removing that source no longer causes AIM to mark a
  different copy as installed.
- Reduced post-install archive work by reusing the recorded installation state
  instead of performing an unnecessary full archive rescan.
- Added a self-hosted archive worker fallback. Single-file builds can perform
  archive operations without shipping a second executable, while portable
  packages may still use the separate worker.
- Improved worker discovery from the actual application directory and stopped
  the worker when AIM closes.
- Added translated duplicate-copy, already-installed, conflict, hotkey and
  legacy-GML messages for all supported interface languages.
- Clarified that legacy GML is only a compatibility warning: it may prevent the
  game from starting or cause a crash, but AIM does not block it automatically.
- Added an experimental, non-blocking compatibility scan at mod-list startup
  for GML, hook and loading-screen signatures associated with older game
  versions. It is advisory only and may need updates after future game patches.
- Compatibility warnings are calculated off the UI thread and are visible
  before installation; hovering the warning icon shows the full reason.
- The startup compatibility scan covers every discovered mod, not only the
  currently selected mods. It reports known legacy signatures as advisory
  warnings and does not treat a warning as a validation error.
- Legacy GML warnings are shown for all discovered mods without disabling
  selection or installation; selected-mod conflicts remain a separate check.
- Fixed warning and error rows so compatibility messages display their icons
  and details instead of remaining hidden in the plain mod row.
- Compatibility-signature checks are now independent of the slower shared-file
  and hotkey scans, so their warning can appear immediately after selection.
- Added selection-time asset conflict detection for folders, ZIPs and RARs;
  mods that replace the same destination files now show a warning before
  installation.
- Added non-blocking warnings for detectable keyboard-shortcut conflicts across
  selected GML mods, including F1-F12 bindings and the Auxiliary Bag default
  F1-F7 bindings. F6 and F8 are examples, not special-case-only checks.
- Documentation files such as README, text and license files are ignored when
  checking shared destinations.
- Clarified that shared localization metadata does not automatically mean that
  only one language mod can be installed; duplicate entries may still follow
  load order.
- Added optional language-specific manifest fields for all AIM interface
  languages (`en`, `bg`, `pl`, `de`, `fr`, `nl`, `pt-br`, `ru`, `id`,
  `zh-hans`, `zh-hant`, `ko`, `ja`, `es`, and `uk`), with the standard fields
  retained as a fallback.
- Recomputed conflict warnings after startup, installation and uninstallation
  on a background task instead of persisting potentially stale warnings.

Compatibility note: `enabledSources` is an AIM profile extension. AIM can read
older MOMI profiles, but an older MOMI version may ignore or remove this field
when it rewrites the profile. In that case AIM falls back to selecting one
physical copy deterministically.

## 0.1.2 — AIM

- Updated the application and CLI projects to .NET 10 with a shared version source.
- Updated core dependencies, including SharpCompress, Newtonsoft.Json, and ImageSharp; removed the unused Magick.NET dependency.
- Added the required ImageSharp license handling for local and CI builds.
- Added portable Windows, Linux, and macOS package workflows while keeping single-file builds available.
- Added Nexus version-check and upload workflow support, pending a public Nexus application API key.
- Hardened archive processing and added a maximum archive-entry limit to prevent pathological archives from locking up or exhausting resources.
- Improved archive recovery and validation diagnostics, including safer handling of malformed or modified game archives.
- Fixed UI status-row outlines appearing during installation and uninstallation.
- Replaced the inherited GUI and CLI icons with consistent AIM artwork and regenerated valid multi-resolution ICO files.
- Merged human-reviewed Russian and Ukrainian translations into the current resource sets while preserving newer AIM keys.
- Completed Ukrainian translations for all interface-language names instead of falling back to other languages.
- Verified GUI one-file, GUI portable, and CLI one-file Windows builds with the new icons embedded.
- Updated MMAPI compatibility for Fields of Mistria 1.0.3: adapted the dungeon runner seam and removed the statue engine fix that is now included in the game itself.
- Verified all 115 seams and the remaining 3 engine fixes against the installed 1.0.3 `assets.zip`.

## 0.1.1 — AIM

- Added Polish as a selectable AIM interface language.
- Added a complete Polish resource set for the GUI, CLI, archive recovery,
  validation diagnostics, profiles, mod dependencies, and cosmetic-mod errors.
- Added the Polish language name to every existing interface-language menu.
- Replaced repetitive load-order arrows with drag-and-drop reordering, including
  an insertion line and a short confirmation flash on the moved mod.
- Prevented mod selection and load-order changes while installation or
  uninstallation is in progress.
- Consolidated Settings, language selection, status, and actions into one compact
  toolbar to show more mods at once.
- Made the update notice dismissible per version; a later update appears again.

## 0.1.0 — AIM

- Renamed the user-facing application to **AIM — Alternative Installer for Mistria**.
- Reset the new AIM release line to version `0.1.0`; the published `0.15.7` history remains unchanged.
- Added Ukrainian as a selectable interface language.
- Added the first Ukrainian translations for the main window, profiles, installation flow, archive state, phases, and location detection.
- Documented direct ZIP/RAR mod reading; archives no longer need to be extracted before installation.
- Documented the gear-menu **Launch game directly** toggle, which starts the detected game executable and falls back to Steam when needed.
- Documented the current duplicate-version limitation: remove the older copy before adding a newer version of the same mod.
- Documented that AIM should be closed before moving or replacing mod archives, which may otherwise remain locked while the application is open.
- Kept technical MOMI namespaces, state paths, manifest keys, and migration compatibility unchanged.
- Clarified that AIM is an independently maintained fork of MOMI and retains MMAPI compatibility and upstream attribution.

## 0.15.7 AI

- Added runtime UI language switching without restarting MOMI.
- Persisted the selected UI language between launches.
- Added GUI resources for Bulgarian, German, French, Dutch, Brazilian Portuguese, Russian, Indonesian, Simplified Chinese, Traditional Chinese, Korean, Japanese, and Spanish.
- Added localized language names in their respective languages.
- Localized setup diagnostics, profile dialogs, missing-dependency dialogs, external-link prompts, file-picker titles, error details, update labels, and installation progress phases.
- Corrected archive-status wording so it reports detected installed mods instead of implying that a MOMI installation is being created.
- Kept archive status and installed-mod counts synchronized with the selected profile.
- Reduced language-switch refresh work by consolidating UI notifications and avoiding unnecessary `assets.zip` rescans.
- Added resource validation coverage for duplicate keys and malformed `.resx` files.
- Added a language-menu checkmark showing the currently selected UI language.
- Updated the application and CLI version metadata to 0.15.7.
- Known limitation: legacy cosmetic mods that use the old 49-frame `back_gear`
  format are not automatically converted to the current 59-frame animation
  layout. Such mods need an updated release from their author; changing only
  the TOML validation or offset is not sufficient to make them compatible.

## 0.15.6

- Fixed the Install button state after creating, switching, or deleting profiles.
- Install is now disabled when no mods are selected, including after all mods are deselected.
- Install state now refreshes immediately when profile selection changes.

## 0.15.5

- Added direct support for ZIP and RAR mods without requiring them to be extracted first.
- Added support for archive-backed mods containing either `manifest.toml` or `manifest.json` at the mod root.
- Added duplicate-mod guidance so the same mod is not kept both as a folder and as an archive in the active mods folder.
- Updated the per-mod installation status panel with theme-aware colours, including a readable dark-theme status and error state.
- Install is now disabled when the selected profile exactly matches the installed mod IDs and versions, and is re-enabled when the set changes or a rebuild is required.
- Play remains available for a valid clean game installation even when no MOMI mods are installed.
- Reduced the default GUI size and header image height for better use on high-DPI displays.
- Updated the release metadata and documentation for MOMI 0.15.5 AI.

This release does not include game archives or copyrighted localization data.
