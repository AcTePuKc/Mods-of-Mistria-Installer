# Roadmap

## Project identity

- Next release branding: **AIM — Alternative Installer for Mistria**, starting at `0.1.0`.
- Do not retroactively rename the published `0.15.7` release.
- Do not add an `AI` suffix to AIM version labels.
- Ukrainian localization support is complete for the first AIM release.
- Keep user-facing AIM branding distinct while retaining the MOMI fork attribution, technical namespaces, MMAPI compatibility, and migration compatibility.

## 0.15.3 AI fork release
* [x] Keep the four focused MMAPI additions with explicit event/lifecycle contracts.
* [x] Add focused MMAPI hooks for fishing selection, museum donation attempts, pet rewards and crop harvest lifecycle.
* [x] Update the shipped MMAPI catalog to 103 hooks and 112 seams.
* [x] Validate the new catalog entries with focused and full test coverage.

## 0.15.2 AI fork release
* [x] Target Fields of Mistria 1.0.x archive and localization workflows
* [x] Stage and validate `assets.zip` rebuilds before replacing the live archive
* [x] Preserve the previous working archive when installation fails
* [x] Add TOML validation, custom font installation and manual-load support
* [x] Add installation diagnostics, high-DPI UI sizing and guarded game launch
* [x] Point update checks and release tooling at the maintained fork

## 0.15.1
* [x] Rebuild `assets.zip` transactionally from a verified pristine archive
* [x] Validate staged archives before replacing the live game archive
* [x] Detect game updates and unknown external archive changes
* [x] Restore the pristine archive transactionally during uninstall
* [x] Provide improved installation diagnostics and adaptive UI sizing

## 0.2.0
* [x] Add Aurie Integration

## 0.3.0
* [x] Enable installing from `.zip` files
* [x] Enable installing from `.rar` files
* [x] Add an uninstall button

## 0.4.0
* [x] Add some user-information when installing/uninstalling Aurie mods
* [x] Warn people when they are running the 32-bit version
* [ ] Automatically update Aurie
* [x] Select the Mistria/Mods folders in a setup screen if not found
* [x] Allow creating a mods folder automatically
* [ ] Add converting old sprite mods

## 0.1.3 — development branch

* [x] Detect duplicate physical copies of the same logical mod across folders,
      ZIP archives and RAR archives.
* [x] Support optional language-specific manifest names and descriptions with
      fallback to the standard `name` and `description` fields.
* [x] Block installation when multiple copies of the same mod are selected.
* [x] Persist the selected physical source for duplicate mods in profiles.
* [x] Restore the installed state after restarting AIM without a full archive
      rescan.
* [x] Run archive work through a worker, with a self-hosted fallback for
      single-file builds and a separate worker for portable packages.
* [x] Detect shared destination files between selected folder, ZIP and RAR
      mods without opening or modifying the game archive.
* [x] Show non-blocking warnings for likely keyboard-shortcut conflicts in GML
      mods, including configurable/default bindings.
* [x] Ignore README, text and license files when reporting shared destinations.
* [x] Recalculate conflict warnings after startup and archive operations without
      blocking the GUI.
* [x] Detect known pre-1.0.3 GML and loading-screen compatibility signatures
      as non-blocking warnings, excluding the Bulgarian localization package.
* [x] Translate the new duplicate-copy, already-installed and exception
      messages for every supported interface language.
* [x] Detect and present incompatible or conflicting selected mods before
      installation, with the experimental compatibility scan running during
      mod-list startup and selection changes.
* [x] Decide how to report mods that do not support the current game/archive
      version: use narrow, known signatures and a non-blocking warning rather
      than guessing from a mod's own version number.
* [x] Catch GUI exceptions and provide a consistent user-facing error log.

## Image replacement documentation

* [x] Document the atlas-backed and atlas-less ImageInstaller paths,
      filename matching rules, 1.0.4 examples and current limitations in
      [`docs/MMAPI/IMAGE_REPLACEMENTS.md`](docs/MMAPI/IMAGE_REPLACEMENTS.md).

## 0.1.7 technical hardening

* [x] Use a physical source key, not only the logical mod ID, for drag/drop,
      load-order persistence and duplicate-copy UI state. Two folder/ZIP/RAR
      copies can share one ID; moving one must never move or select the other.
      Quick fix: drag/drop now identifies the physical source path.
* [x] Fix automatic `nxm://` re-registration after replacing or moving a
      portable build. Windows now compares the normalized full executable path;
      matching only `AIM.exe` could incorrectly treat an older copy as current.
* [x] Add drag/drop auto-scroll near the top and bottom edges of the mod list so
      a mod can be moved across a long list without releasing it repeatedly.

## 0.1.7 current backlog

* [x] Complete and verify translations for the newer Settings, Nexus,
      update, context-menu and mod-description strings in every supported UI
      language.
* [x] Test the drag/source fix with two physical copies sharing one manifest
      ID, including a folder plus ZIP/RAR and two different versions.
* [x] Verify that drag/drop does not leave a stale duplicate visual row in the
      Avalonia `ItemsRepeater` after moving a mod. Confirmed with a removed and
      re-added Bulgarian folder/archive copy; positions remained stable.
* [x] Review profile load-order persistence so duplicate physical copies are
      not collapsed back to one logical ID after restarting AIM.
* [x] Re-test NXM downloads and folder-watcher reloads for transient duplicate
      rows after a download or move. NXM startup/closed-app handling is verified;
      duplicate-row handling is verified separately.
* [x] Finish the remaining user-facing documentation and release notes only
      after the above behavior is verified.

## 0.1.8 or later — larger features

* [ ] Design a real per-mod translation-pack contract: target mod identity,
      target version range, language, priority, fallback and safe ownership
      rules. Do not implement it as an undocumented `l10n.meta.toml` trick.
* [ ] Evaluate additional MMAPI hooks and seams individually with a working
      mod example before adding them to the default catalog.
* [ ] Revisit the remaining automatic-update and mod-source improvements after
      the 0.1.7 Nexus/NXM behavior is stable.
* [x] Reduce the existing nullable-reference warnings across the installer,
      ImageInstaller, OutfitInstaller, furniture generation and mod-loading
      paths. Verified with a clean Release build and runtime smoke test.
* [x] Add explicit validation and user-facing handling for missing or invalid
      TOML tables, atlas metadata and required file paths, especially for
      malformed or partially supported mods.
* [x] Resolve nullable initialization warnings for `FilePath`, `LogAdded`,
      `_fileModifier`, `SpriteToml.Id` and related model members.
* [x] Align the `FolderMod.ReadFile` nullable contract with `IMod`.
* [x] Review nullable returns and comparisons in `Utils/Toml.cs`.
* [x] Review nullable paths in `CompactFurnitureGenerator`,
      `FurnitureDefinition`, `FurnitureInstaller` and `LocationInstaller`.
* [x] Replace the intentional legacy `OutfitFile` alias auto-properties with
      explicit compatibility setters and remove the corresponding compiler
      warnings.
* [x] Remove framework-provided `Microsoft.Win32.Registry` and
      `System.IO.Compression.ZipFile` package references after a clean restore,
      build and test run.

## Future/Unknown
* [ ] Allow all "localised" text in easy JSON structures to be multi-lingual
* [ ] Investigate optional per-mod localization selection. This should only be
      added if a real mod exposes language-specific resources; AIM must not
      guess or replace a mod's own UI language files.
* [ ] Add Validators for Simple Conversations
* [x] Store selected/deselected mods in the Mods folder
* [x] Allow load order modifying
* [x] Allow mods to declare dependencies on other mods
* [ ] Automatic updating
* [ ] `player_tools.json` installer
* [ ] `farms.json` installer
* [ ] `hyper_points.json` installer
* [ ] `t2_input.json` installer
* [ ] Sounds installer
* [ ] Improve translations for validations (the prefixes are not pulled from localisations)
* [ ] Add translations for exceptions
* [ ] Catch all exceptions in the GUI
* [ ] Add a error_log file for the GUI
* [ ] Render mod names as coloured inline text inside warnings and errors
* [ ] Add a JSON browser
* [ ] Scramble JSON automatically on install
* [ ] Cutscene generator
* [ ] Automatically refresh mods when a change has been made
* [ ] In the GUI, skip mods that fail `CanInstall` instead of disabling install
