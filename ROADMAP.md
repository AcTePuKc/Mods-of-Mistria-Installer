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

## 0.1.8 — focused conflict reporting

* [x] Keep the normal mod-list view compact: show conflict indicators and a
      short summary instead of rendering every conflicting path inline.
* [x] Keep the default `Suggest Order` path fast and free of detailed TOML
      analysis.
* [x] Keep load-order suggestion separate from the detailed conflict report.
      `Suggest order` handles ordering; `Report conflicts` lists exact shared
      paths and other selected-mod warnings on demand.
* [x] Reuse the existing self-worker/background-operation infrastructure for
      the detailed read-only analysis without adding a user-visible executable.
* [ ] Cache detailed conflict results until the selected mods or their source
      files change.
* [x] Add a copyable conflict report for mod authors and bug reports.
* [x] Add and verify only the new strings required by this feature in every
      supported interface language.
* [x] Test folders, ZIP/RAR archives, large mod lists, hard replacements,
      mergeable metadata and shared localization files.
* [x] Add local mod-list search without reopening archives, rechecking Nexus,
      or changing the profile's selected set or load order.
* [x] Pause drag/drop only while search filtering is active, so a visible
      subset cannot accidentally reorder hidden mods.
* [x] Batch Select all / Deselect all to prevent per-row archive and conflict
      work from freezing the UI on large mod lists.
* [x] Validate legacy `momi/outfit` cosmetics against the same generator slot,
      UI-sprite and frame-size rules used during installation.
* [x] Exclude the shared legacy cosmetic category icon from file-conflict
      detection: AIM registers each cosmetic's player assets and store entry
      independently, so the icon is not a load-order conflict.

## 0.1.9 — post-0.1.8 UI fixes

* [x] Replace the automatic description tooltip with a small, pointer-driven
      description popup. This prevents the repeated tooltip open/close loop
      seen after focusing the Search field, while retaining the description on
      hover.
* [x] Make the detailed Issues window non-modal so users can keep it open
      while changing the mod selection or load order in AIM.
* [ ] Replace the personal Nexus API-key flow with a public OAuth 2.0
      Authorization Code + PKCE flow. The client ID remains an empty
      registration placeholder until Nexus Mods has reviewed the source and
      registered AIM; no client secret is used or shipped.
* [ ] Open browser authorization through a fixed loopback callback, validate
      `state`, exchange the code with its matching PKCE verifier, and use
      refresh tokens only through the documented public-client flow.
* [ ] Replace the API-key dialog and Settings actions with a single Nexus
      account connection surface. It must offer connect, connected status,
      disconnect and a clear explanation when AIM has not yet been registered.
* [ ] Migrate or remove legacy personal-key data from `nexus.json`; never send
      it to Nexus, write it to logs, or include it in a build. Keep the
      existing `nxm://` handler opt-in unrelated to account connection.
* [ ] Add focused OAuth request, callback-state, token-expiry, refresh and
      disconnect tests. Verify every supported UI language and scan the final
      source/package for API keys, access tokens, refresh tokens and client
      secrets.
* [ ] Send Nexus Mods the review branch, public source link and final callback
      URI; add the client ID only after their registration response.

### Compatibility-warning clarification

* Generic GML warnings and concrete shared-file conflicts are separate checks.
  A cosmetic mod can be marked for legacy GML without appearing in the exact
  shared-file list; the list only contains destinations detected as written by
  multiple selected mods.
* Keep the warning non-blocking until a narrow, reproducible game-breaking
  signature is confirmed.

### Runtime cosmetic interoperability matrix (pending isolation)

The 2026-08-26 archive inspection confirms that the legacy `momi/outfit`
cosmetic mods are all written into `player_assets.toml` and their separate
`fiddle/stores.toml` category entries are appended. The shared
`spr_ui_store_category_icon_moddedcosmetic` sprite is therefore a common
category icon, not evidence that only one cosmetic mod was installed.

The following in-game observations are mapped to their source mods, but still
need a reproducible compatibility rule before AIM marks anything as
incompatible:

* Hair entries appear in the character UI. Curly Mini Buns and Dread Buns
  render correctly; the other tested modded hairs are selectable but do not
  render correctly on the player.
* Tested hair accessories, glasses, and face accessories do not render.
* Tested facial-hair mods render correctly.
* Tested Reina summer sleeveless/short-sleeve tops, Celine long-sleeve tops,
  Summer Outfit's misc top, Reina pants, and Celine/Shortest Skirt/Summer
  skirts do not render.
* The current installed archive contains valid `player_asset_parts.json`
  entries for Curly Mini Buns, Even Longer Fringe, More Accessories, More
  Glasses, Shugar's Scarf, Celine/Reina clothes, Shortest Skirt, and Summer
  Outfit. Their failure is therefore not caused by a missing generated
  registration or a malformed image-strip width.
* The current install state does **not** include Adriana Hair, so it cannot
  explain the present result. Keep that test separate from the current set.
* Anthro Player Mod directly replaces `spr_player_base_base_head.png`; More
  Skin Colors directly replaces `spr_player_base_lut.png`. These are the only
  installed mods found to replace the player base and are the leading suspects
  for cross-layer rendering behaviour.

First follow-up: compare the generated atlas entries and player-layer data
against a known working cosmetic before deciding whether AIM needs a narrow
compatibility warning or an installer fix. Do not treat the shared category
icon as a conflict.

## Later / deferred
* [ ] Allow all "localised" text in easy JSON structures to be multi-lingual
* [ ] Investigate optional per-mod localization selection. This should only be
      added if a real mod exposes language-specific resources; AIM must not
      guess or replace a mod's own UI language files.
* [ ] Add Validators for Simple Conversations
* [ ] Automatic updating
* [ ] `player_tools.json` installer
* [ ] `farms.json` installer
* [ ] `hyper_points.json` installer
* [ ] `t2_input.json` installer
* [ ] Sounds installer
* [ ] Improve translations for validations (the prefixes are not pulled from localisations)
* [ ] Add translations for exceptions
* [ ] Render mod names as coloured inline text inside warnings and errors
* [ ] Add a JSON browser
* [ ] Scramble JSON automatically on install
* [ ] Cutscene generator
* [ ] In the GUI, skip mods that fail `CanInstall` instead of disabling install
