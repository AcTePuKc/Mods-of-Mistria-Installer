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

- **"Find a fix" now answers the question itself instead of only pointing at pages.** Before reading
  anybody's mod page it opens the files the two mods actually share and works out what AIM's own
  installer will do with them: files with identical bytes cannot disagree about anything; TOML and
  JSON that set different keys are merged, so both mods survive intact; keys both mods set are named
  individually, and the one that wins is the mod that loads last; anything under `images/replace/`
  is a straight replacement where load order decides. Most reported conflicts turn out to be the
  first two cases, so the answer is now "these do not actually conflict, and here is why" rather
  than a list of tabs to go and read. A file it cannot parse makes the whole verdict uncertain and
  it says so — it will not guess.
- **Fixed "find a fix" missing the most common way authors write the answer.** The keyword list
  looked for "compatible", "compatibility" and "incompatible". Authors overwhelmingly write the
  plural — *"No known compatibilities / incompatibilities at this time. It is a standalone mod"* —
  and none of the three words is a substring of either plural, so on pages that answered the
  question outright AIM reported that it had found nothing. Matching is now on the stem, which
  catches every inflection.
- Findings are now sorted by **which way they point**. A sentence clearing a pairing and one
  condemning it are opposite answers and no longer sit in the same undifferentiated list: evidence
  against the conflict, evidence for it, and everything else are separate sections, and quotes that
  name the other mod come first within each.
- Research reads **more of each mod's Nexus presence**: the release notes (where "fixed a conflict
  with X" is written far more often than in a description) and the file list, on top of the
  description, comments and bug tracker it already read. Everything stays best-effort: a failure
  means fewer quotes, never an error.
  - The **Docs tab** is now read as well. Nexus publishes a mod's readme as a plain text file, so
    unlike everything else read off the site there is no markup to get wrong — and a careful author
    documents installation order and what the mod takes over there rather than in the description.
  - **Comment threads are now searched rather than sampled.** Comments are not paginated by URL —
    the pager's links are literally `href="javascript:;"` — so reading them at all meant calling the
    same `CommentContainer` widget the site's own pager does. That widget takes a **search term**,
    which changes the problem completely: instead of reading the newest twenty comments and hoping,
    AIM searches each mod's entire thread for the *other mod's name* and gets back the handful of
    comments actually about that pairing, in one request. On a mod with 529 comments across 24
    pages, the one answering "does this work with X?" is typically months old and twelve pages back.
    It needs no account — the site hides the search box behind a login prompt, but the endpoint
    answers a signed-out request identically.
  - **Bug reports are read properly, with the author's ruling on each.** The tracker used to be a
    flat list of complaints, so *"crashes with X"* and *"crashes with X — closed, not a bug"* counted
    the same. They are opposite answers, and the second is the more useful: somebody already
    investigated that exact pairing and found nothing wrong. AIM now reads each report's status and
    weighs it — *not a bug* argues against the conflict, *known issue* and *won't fix* argue for it,
    *fixed* and *duplicate* argue neither way — and the status outranks the words in the report. It
    also opens the **reply threads**, which are not on the tab at all and are where an
    incompatibility usually gets pinned down. Only reports whose title or ruling looks relevant are
    opened, so a mod with fifty bugs does not cost fifty requests.
  - The signed-in reader is plumbed through but **not connected to anything yet**: there is nowhere
    to put a session cookie, so every read is still anonymous.
- Mentions of a mod are matched on its **distinctive words** rather than its exact Nexus title, so
  "does this work with Witchy Weapons?" and a folder called `suushiico_witchy_weapons_tools` both
  find *Sushi's Witchy Weapons and Tools*, while a stray "tools" no longer matches anything.
- **Compatibility patches are found rather than searched for.** A patch declares both mods as
  requirements, so Nexus lists it on both their pages — a mod linked from every page in a conflict
  is, in practice, the patch. Optional files on the mods' own pages are checked too.
- The report can now **act**: close the issue with AIM's own reasoning attached, install a patch it
  found, reorder the mods so the right one wins, or set aside one mod's copy of the contested files.
  Each is spelled out in terms of what changes and applied only when chosen.
- A found patch is **downloaded and installed outright on a Premium account** — through the ordinary
  download path, so it appears in the downloads strip with progress and is registered for update
  checks like any other mod. Only when Nexus actually refuses to issue the link does AIM open the
  patch's files, where one click on "Mod Manager Download" hands it back. The fallback is chosen on
  Nexus's refusal rather than on AIM checking the account tier, so a Premium user with a revoked key
  is told about the key rather than told to buy what they already have.
- **Approve and apply now finishes the job.** Reordering, installing a patch or setting a file aside
  closes the window and records the issue as resolved with what AIM did attached, so it moves into
  the report's resolved list instead of leaving the user to close the dialog and answer "what did
  you find?" about a fix they had just watched happen. Only a fix that did not complete keeps the
  window open, with the reason on screen — including a free Nexus account being sent to the download
  button, where the work genuinely is not finished yet.
- **Fixed dialogs opening with their top edge off the screen.** A window with a fixed height and
  `CenterOwner` placement assumes it fits; centring a 700-tall dialog on a display whose working
  area is 693 device-independent pixels — 1080p at 150% scaling, which is a very ordinary setup —
  puts the title bar and the first controls above the top of the screen, reachable only by
  maximising. "Find a fix", the issues report, the changelog and the keybind manager now shrink to
  the working area and nudge themselves back inside it as they open.
- **AIM never edits a mod without a way back.** Setting a file aside copies the whole mod into the
  same backup store an update uses, so it appears in the row's existing **Versions** dropdown
  labelled *"2.1.0 before AIM's fix"* and is undone with the same click as any rollback. Files are
  renamed, never deleted. The mod's row carries an **Edited by AIM** badge listing what changed —
  the risk was never the edit itself but nobody remembering it was made — and the badge is dropped
  when an update or a rollback replaces the folder.

- Added **Remove mod** to the mod row's right-click menu. It asks for confirmation, names the exact
  folder, and sends the mod to the Recycle Bin rather than erasing it, so a mis-click on the wrong
  row is recoverable. The mod's Nexus provenance record is dropped at the same time, so a later mod
  that happens to reuse the folder name is not mistaken for it.
- Added **Edit the mod's manifest** and **Edit the mod's config** to the same menu. Both open the
  file in whatever editor the user has associated with it, falling back to Notepad on Windows where
  `.json` and `.toml` often have no association at all. Both are greyed out when there is nothing to
  open — including for mods that are still `.zip` or `.rar`, where an edit would be discarded by the
  next install.
- Added per-issue dismissal to the **Check issues** report. Each finding gets a checkbox that marks
  it as one the user has looked at and accepted; dismissed findings move to a dimmed, struck-through
  section behind a *Show issues I have marked as fine* toggle, so they can always be found and
  reversed. A dismissal is keyed to the mods **and their versions**, so updating either mod brings
  the issue back for a fresh judgement. Judgements are stored in `aim_dismissed_issues.json` beside
  the profiles in the mods folder, and dismissals for issues that no longer exist are pruned when the
  report is opened.
- Fixed mods being marked "requires a newer version of the installer" and then refused. AIM's own
  release line is 0.1.x, but mod authors write `minInstallerVersion` against upstream MOMI, which is
  past 0.15 — and 1 sorts below 15. The comparison already used a separate compatibility constant;
  it was simply behind, at 0.15.7, so anything targeting 0.15.10 was blocked. The constant is now
  0.15.10, **and the mismatch is a warning rather than an error**: an error forces the mod off so it
  cannot even be ticked, on the strength of a guess that it might not work. The message now names
  both versions instead of leaving the user to work out which two disagree.
- Fixed permanent false "update available" badges. The comparison used the mod's **manifest**
  version against the Nexus page's, and authors number those separately — one mod here calls itself
  `1.0.2` in its manifest and `3` on its page, so no amount of updating could satisfy it. AIM now
  compares the version it **recorded from Nexus** when it took the file, which is the same numbering
  the page uses, and treats these as *not* updates: the same file id, a re-upload at an unchanged
  version, and a page whose files no longer include the one installed (authors routinely delete old
  files — assuming that meant "update" listed a dozen mods whose versions had not moved). It also
  compares a file against others in **its own category**, so a folder installed from an optional or
  miscellaneous file is no longer measured against the main file.

  A mod associated by pasting a page URL has no file id, so the only version AIM holds for it comes
  from the mod's manifest — a different numbering scheme from the page's. Those are now reported as
  **could not be checked**, with instructions, rather than as a permanent update. Downloading the
  mod once through AIM, or associating it with a specific file, moves it onto the reliable path.

  The deliberate trade-off: an author who ships a new file without changing the version on the Nexus
  file itself will not be reported. Silence is the better failure here — a false alarm that no
  action can clear teaches people to ignore the badge entirely.
- Split the version out of each mod row. The name and the manifest version are now separate, with
  space between them and the version dimmed, and the version is re-read whenever the mod is updated,
  rolled back, or has its manifest edited from the row's own menu.
- Added **Remove selected…** to the gear menu, beside Enable/Disable all, removing every ticked mod
  in one pass after showing the full list for confirmation.
- Taught **Find a fix** to read Nexus bug reports and comments. There is no API for either, so this
  reads the public pages directly — best-effort by design: a layout change yields no posts rather
  than wrong ones, and the links to the real pages are always shown regardless. Only posts naming
  one of the other mods, or using explicit compatibility wording, are kept; a comment thread is
  mostly not about compatibility.
- Added release notes to each mod row. A small document icon sits after the version for any mod AIM
  knows the Nexus page for: hovering it shows what changed in the newest version, and clicking it
  opens the full history — every version the author wrote notes for, newest first, each under its
  own heading. Notes are fetched from Nexus the first time you look rather than for all 150 mods on
  every launch, and cached in `aim_changelogs.json` keyed to the installed version, so an update
  fetches the new release's notes and everything else is free.
- Fixed the selection summary not updating after an install. "147 already in the game, 1 will be
  added" stayed put until AIM was restarted, because the count was read from *this session's install
  outcomes* rather than from the game archive — and a mod installed a moment ago is not "already
  installed" by that measure. Those are two different questions and are now tracked separately, so
  the summary corrects itself the moment an install or uninstall finishes.
- Added **shift-click range selection**. Clicking one mod's checkbox and shift-clicking another sets
  every row between them to whatever the clicked one just became, as in a file manager. Ranges
  follow the visible list, so a search or filter is respected rather than silently sweeping up rows
  hidden between the two ends, and mods AIM cannot install are skipped rather than force-ticked.
- Added **Sort by recently updated**, newest first by when each mod's folder last changed on disk,
  so anything just installed or updated comes to the top. It and **Sort A–Z** are alternatives —
  turning one on turns the other off, since a list cannot be in two orders at once.
- Added view-only list controls above the mod list: **Sort A–Z** and **Only mods needing attention**
  (a pending update, or one the last check could not reach), plus buttons to jump to the top and
  bottom of the list. None of these touches the load order — they change which rows are shown and in
  what order, and each row keeps its real load-order number so the true order stays readable.
  Drag-and-drop is paused while any of them is on, for the same reason it already was during a
  search: reordering a filtered or re-sorted view has no clear meaning for the order underneath.
- Fixed the update badge opening a browser instead of installing the update. The green badge on a
  mod row was wired to "open the download page", so the one obvious button in the row was the only
  path that could not install anything — the automatic download lived in the right-click menu, where
  nobody looked. The badge now runs the update when AIM knows which Nexus file it is, and falls back
  to opening the page only for a mod it has just a URL for, such as a GitHub release. Its tooltip
  says which of the two it will do.
- Stopped blaming the user's Nexus account tier for every failed download. Any failure — a dead CDN
  mirror, a corrupt archive, an unwritable folder — was answered with "open the mod's page and use
  Mod Manager Download", sending people off to install by hand for problems a retry would have
  fixed. Only an actual refusal to issue a download link now offers that, and it asks Nexus what
  tier the account really is before suggesting Premium is the problem: a Premium user hitting this
  usually has a revoked API key, not a missing subscription.
- Added a **Versions** dropdown to any mod with archived copies, so a rollback can pick the version
  from *before* the one that broke rather than only the newest backup.
- Added a **Keybinds** button beside the mod list: every key and controller button your mods have
  bound, in one list, editable in place, with clashes in red and a hover naming the other mods on
  that input. Mod settings live in the game's own config folder rather than in the mods folder, so
  answering "what is F1 doing?" previously meant opening a dozen JSON files by hand.
- Added persistence for the keybinds you choose. Mod settings are not touched by a mod update — they
  live outside the mods folder — but a mod that bumps its config version and migrates to defaults
  resets them silently. AIM now remembers what you chose and offers to put it back, listing exactly
  what changed. A setting the update **removed** is forgotten rather than restored: the feature it
  belonged to is gone, so re-applying the key would bind it to nothing.
- Pointed the shortcut-clash check at what mods are actually bound to instead of the defaults
  compiled into their source. It was reporting clashes between two mods' defaults — including pairs
  the user had separated in the game's own settings — and missing every clash between two keys the
  user had chosen. It now also covers controller buttons, letters and digits, and chords like
  `SHIFT+F5`, none of which the old F-key scan could see. The inline warning on a mod row uses the
  same scan, so a clash resolved in the report or the keybind manager stops nagging from the list.
- Added **Check updates** beside the mod list, with a dropdown for selected mods, all mods, or
  installing everything that has a pending update. Finding updates now offers to install them
  instead of only reporting the count; each mod's current version is still kept as a restorable
  backup. Update checking was previously reachable only from the gear menu.
- Split keyboard-shortcut clashes into one report entry per shortcut. They were previously lumped
  into a single note, which could only have been dismissed all or nothing.
- Rewrote how the **Check issues** report names the mods in a finding. It showed each mod's full
  install path inline, which turned a three-way shortcut clash into six wrapped lines of directory
  names and buried the one thing the reader needed. Findings now list mod names, with the path on
  hover.
- Added **Make this one win** to shared-file conflicts. Each mod in the conflict gets a button that
  moves it below the others in the load order, so its copies of the shared files are the ones
  installed. The list behind updates immediately and the finding rewrites itself to confirm.
  Shared-file conflicts are now ranked by the load order you actually have rather than the one
  **Suggest order** would produce, so the mod labelled as winning is the one that really does.
- Added **Find a fix** to conflicts involving two or more mods. AIM reads what each mod's Nexus page
  says — the public API exposes descriptions and summaries — and quotes any sentence naming one of
  the other mods, or mentioning patches, compatibility or load order. Bug reports and comments are
  not available through the API, so those are one-click links to the exact tab, alongside a web
  search naming both mods. Whatever you conclude is recorded against the issue: not a real conflict,
  a patch exists (with its link), or genuinely incompatible.
- Added **Rebind** to keyboard-shortcut clashes. AIM moves one mod onto a key nothing else uses by
  editing its own `#macro` binding, backing the mod up first, and the clash disappears once nothing
  shares the key. Only declared bindings are rewritten — raw `vk_f1` constants are left alone,
  because the same token can appear in a comparison or a lookup and a mod that stops compiling is
  worse than a shortcut clash. Archive-backed mods and dynamically built bindings show a greyed
  button explaining why. The change reaches the game on the next install.
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
