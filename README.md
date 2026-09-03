# AIM — Alternative Installer for Mistria 0.1.7

This is an independently maintained alternative installer for **Fields of Mistria 1.0.x**, based on the open-source **Mods of Mistria Installer (MOMI)** project.

AIM is a fork of MOMI. It was renamed to avoid confusion between the two applications while preserving the upstream history, attribution, and technical compatibility. AIM is not affiliated with or endorsed by the original MOMI project.

AIM is not intended to replace MOMI. It exists to provide capabilities that are currently needed by this fork while remaining compatible with the upstream project. If MOMI later adopts at least the capabilities that motivated this fork and fully meets the project's needs, AIM may be retired in favour of the upstream project.

The current AIM application version is `0.1.7`.

## Preview

![AIM preview](aim-preview.gif)

<sub>Visual preview of AIM: language switching, mod installation and removal, load-order management, mod selection, and installation status messages.</sub>

## Fork-specific improvements

Compared with the upstream 0.15.1 line, this fork focuses on Fields of Mistria 1.0.x support and safer everyday use:

- Rebuilds are staged from a verified pristine archive and validated before the live `assets.zip` is replaced.
- Failed installations keep the previous working archive and provide a mod-specific diagnostic log where possible.
- TOML validation, custom font installation and manual-load animation content are supported for current 1.0.x mods.
- The UI remembers profiles and load order, behaves better on high-DPI displays, and includes a guarded **Play** button.
- Update checks, release uploads and the GitHub link belong to this fork rather than the upstream repository.

## Nexus integration and mod list tools (added by this branch)

Everything above describes AIM as it already is. This section is the part added here, kept separate
so it is obvious what is new and what is not. Nothing in AIM's existing install, rebuild or profile
behaviour changes.

| What | Where it lives in the UI |
| --- | --- |
| Nexus **Mod Manager Download** (`nxm://`) links download and unpack straight into the mods folder | Gear menu → **Nexus downloads** |
| Check one mod, the selected mods, or every mod for updates on its Nexus page | Right-click a mod, or gear menu → **Nexus downloads** |
| Update a mod from Nexus, keeping the previous version as a backup you can restore | Right-click a mod |
| Freeze a mod so update checks leave it on the version it is on | Right-click a mod |
| Open a mod's Nexus page or its folder | Right-click a mod |
| Edit a mod's manifest or config file in your usual text editor | Right-click a mod |
| Remove a mod from the mods folder, via the Recycle Bin | Right-click a mod → **Remove mod…** |
| Mark a reported conflict as one you have checked and are happy with | **Check issues** → tick the box beside it |
| Remove every ticked mod at once | Gear menu → **Remove selected…** |
| Sort the list A–Z, or show only mods needing attention, without changing load order | Checkboxes above the mod list |
| Jump to the top or bottom of a long mod list | **↑** / **↓** buttons above the mod list |
| See and edit every keybind and controller button your mods use, with clashes in red | **Keybinds** button above the mod list |
| Keep the keybinds you chose when a mod resets its own settings | Automatic — AIM asks before restoring |
| Check for updates, and install everything that has one | **Check updates** button above the mod list |
| Install one mod's update in place, keeping the old version | Green **Update** badge on the mod's row |
| Roll a mod back to any earlier copy AIM kept, not just the newest | **Versions** dropdown on the mod's row |
| Read what changed in a mod, this version or any earlier one | Document icon after the mod's version |
| Decide which mod wins a shared file, from inside the conflict report | **Check issues** → expand a finding → **Make this one win** |
| Look up whether a conflict is known, patched, or harmless | **Check issues** → expand a finding → **Find a fix…** |
| Move a mod off a clashing keyboard shortcut | **Check issues** → expand a shortcut clash → **Rebind…** |
| Select or clear every mod at once, with a summary of what the selection means | Checkbox above the mod list |
| **Suggest order** — order mods so each loads after what it requires, and report what it cannot decide | Button above the mod list |
| Mods copied into the mods folder appear without reopening AIM | Automatic |

Full details are in [Downloading mods from Nexus](#downloading-mods-from-nexus-mod-manager-download)
and [Mod list tools](#mod-list-tools) below.

### What it does not change

- Installing still rebuilds `assets.zip` from the pristine backup using the mods that are ticked, so
  a ticked mod means "in the game" and nothing is unticked for you. Downloading a mod does not install
  it; it appears in the list and waits for **Install** like any other mod.
- ZIP and RAR mods are still read in place. A downloaded archive is unpacked because AIM knows it is
  a fresh download, but an archive you drop in yourself is left exactly as it is.
- No existing file format, profile or command-line flag changes. The new state lives in three new
  files: `aim_nexus.json` and `aim_dismissed_issues.json` in the mods folder, and `nexus.json` in
  `%LOCALAPPDATA%\AIM`.

## What this fork supports

- Fields of Mistria 1.0.x mod installations.
- Mod folders, ZIP archives, and RAR archives containing either `manifest.toml` or `manifest.json`.
- ZIP and RAR mods are read directly by AIM; extracting them first is optional. AIM can locate the mod manifest inside a supported wrapper folder, but it does not search through unlimited nested folders.
- TOML, JSON, image, outfit, furniture, item, object, store, shadow, font and manual-load mod content supported by the current AIM installer modules.
- GML mods using the MMAPI format documented in [`docs/MMAPI`](docs/MMAPI); MMAPI compatibility is retained from the upstream project.
- Profiles and persisted mod load order.
- Rebuilding `assets.zip` from a verified pristine backup, so disabled or removed mods are removed on the next successful rebuild.
- Staged installation diagnostics, archive validation and recovery when an installation fails.
- A Play button that is available when the game can be launched, including before any mod is installed.
- Play uses Steam by default. Enable **Launch game directly** from the gear menu to launch the detected `FieldsOfMistria.exe` instead; the preference is saved between launches and falls back to Steam if direct launching is unavailable.
- At startup, AIM performs an experimental advisory scan of discovered mods for known legacy GML, hook and loading-screen signatures. It does not block those mods automatically; the warning icon and its hover text explain the detected risk.
- Before installation, AIM also checks selected mods for shared destination files and detectable keyboard-shortcut conflicts. These checks are warnings unless the selected mods cannot safely be combined.

This project is intended for Fields of Mistria 1.0.4 and later 1.0.x patches. Individual mods may still require a specific AIM version or game patch; check the mod author's compatibility notes.

## Installation

1. Download the latest release from the [releases page](https://github.com/AcTePuKc/Mods-of-Mistria-Installer/releases).
2. Open AIM and choose a mods folder. AIM automatically checks for `mods`, `Mods`, `MODS`, or `MODs` next to the detected game installation and next to the AIM executable. It also checks the supported per-user Linux/Steam Deck locations. You can select or create another folder manually.
3. Put each mod directly in the selected folder. The manifest may be in the mod folder or in one supported wrapper level:

   ```text
   mods/
   ├─ MyMod/
   │  └─ manifest.toml                 ✅ supported
   ├─ MyMod/
   │  └─ Wrapper/
   │     └─ manifest.toml              ✅ supported
   └── MyMod/
       └─ Wrapper/
          └─ AnotherFolder/
             └─ manifest.toml           ❌ too deeply nested
   ```

   ZIP and RAR archives can be added directly. The same rule applies inside the archive:

   ```text
   MyMod.zip
   ├─ manifest.toml                     ✅ supported
   ├─ Wrapper/
   │  └─ manifest.toml                  ✅ supported
   └── Wrapper/
       └─ AnotherFolder/
          └─ manifest.toml               ❌ too deeply nested
   ```

   If the manifest is buried deeper than this, move the mod files up one or more folders before starting AIM.
4. Select the mods you want and click **Install**. You can add new mods at any time; you do not need to uninstall the other installed mods first.
5. Start the game with **Play**.

> [!IMPORTANT]
> Keep only one copy of each mod in the active folder. When updating a mod, remove its old copy first and leave only the new version. Do not keep the same mod both as a folder and as a ZIP/RAR archive.

> [!WARNING]
> Close AIM before moving, replacing, or deleting mod files. An open mod archive may be locked while AIM is running.

AIM preserves a pristine backup and writes a staged archive before replacing the live `assets.zip`. Do not delete the backup while AIM is managing the installation. Keep a separate game backup before testing unfamiliar mods.

## Downloading mods from Nexus ("Mod Manager Download")

AIM can register itself as the handler for `nxm://` links, which is what the **Mod Manager Download**
button on a Nexus Mods page uses. Once it is set up, clicking that button downloads the mod and
unpacks it straight into your mods folder, the way Vortex does for other games.

### Setting it up

1. Open the gear menu → **Nexus downloads** → **Nexus API key...**
2. Click **Open Nexus account settings**, scroll to **API Key**, generate a personal key and paste it
   into AIM. The key is checked immediately, and is stored on this computer only (encrypted with
   Windows DPAPI; on Linux in a file only your user can read).
3. Back in the gear menu, choose **Handle "Mod Manager Download" links**. The line underneath the menu
   item shows whether AIM currently owns those links.
4. Click **Mod Manager Download** on any Fields of Mistria mod page. Your browser will ask once
   whether to open the link with AIM.

### What happens on a download

- The link is handed to the AIM window you already have open. A second window is never opened.
- The mod is downloaded from Nexus and unpacked into your mods folder, anchored on the mod's
  `manifest.toml`, so an archive with an extra wrapper folder still lands in the right place.
- Downloading a mod you already have asks before replacing it, and the previous copy is kept until
  the new one is written successfully.
- Downloading does not install mods into the game. The new mod appears in the list, and you still
  choose when to press **Install**.

### Notes and limits

- A Nexus API key is required. Free accounts can only download through the website's **Mod Manager
  Download** button, because the download token lives in the link itself; an `nxm://` link typed by
  hand will be refused by Nexus for a non-premium account.
- Registration is per-user and never needs administrator rights: `HKCU\Software\Classes\nxm` on
  Windows, a `~/.local/share/applications/aim-nxm-handler.desktop` entry plus `mimeapps.list` on
  Linux and the Steam Deck.
- If another mod manager already owns `nxm://`, AIM says so and asks before taking over. Turning the
  option off again only removes AIM's own registration.
- A browser installed as a Flatpak or Snap may not be able to launch a handler outside its sandbox.
  In that case, right-click **Mod Manager Download**, copy the link address, and use gear menu →
  **Nexus downloads** → **Install from a copied nxm:// link**.
- You can also associate a manually installed mod with Nexus by right-clicking the mod's name or
  row and choosing **Associate with Nexus...**. A normal Nexus page URL enables version checks;
  a copied `nxm://` link identifies the exact Nexus file. If the same version is already present,
  AIM records the association without downloading it again. If you choose **Yes** when AIM asks
  whether to replace an existing file, it might download that file again.
- Nexus collections are not supported; download the mods in them individually.

### Keeping mods up to date

AIM remembers which Nexus mod and file each download came from, in `aim_nexus.json` beside the
profiles. Mods installed by hand are recognised too, as long as their manifest points at a Nexus page.
You can also right-click the mod's name or row and choose **Associate with Nexus...**. Use a normal
Nexus page URL for version checks, or a copied `nxm://` link when the exact Nexus file must be
identified.

- Right-click a mod → **Check for an update**, or use gear menu → **Nexus downloads** → **Check
  selected mods for updates** / **Check all mods for updates**.
- A mod with an update shows the green badge. Right-click → **Update from Nexus** downloads and
  replaces it.
- Checking works on any Nexus account. Downloading an update directly does not: Nexus only issues a
  download link to a non-premium account when the request carries the token from a website button
  click, so free accounts are offered the mod's files page instead.
- Update sweeps run a few mods at a time and stop early if Nexus reports a rate limit, keeping the
  results already gathered.

### Freezing a mod

Right-click a mod → **Freeze at this version** to hold it where it is. Frozen mods show a 🔒, are
skipped by update checks, and stay frozen if the mod is reinstalled. Mods AIM never downloaded can be
frozen too, which is how to protect a mod you have edited yourself.

### Backups and rolling back

Updating a mod moves the copy it replaces into `.aim-backups` inside the mods folder, keeping the
three most recent. Right-click → **Restore the previous version** puts the newest backup back, and
keeps the copy it replaces, so a rollback can itself be undone. The backup folder starts with a dot
so the installer's own scan of the mods folder ignores it.

## Mod list tools

- **Select all.** The checkbox above the list selects everything, or clears the selection when
  everything is selected. Beside it, a summary reads for example "4 of 5 selected — 3 already in the
  game, 1 will be added", because a tick means the mod will be in the game after the next install,
  not that it is queued to be added.
- **Suggest order.** Moves each mod below the mods it requires, using the smallest changes that
  satisfy those requirements, so mods you deliberately ordered stay where you put them. Load order
  is saved with the profile as before.
- **Check issues.** Opens an on-demand, copyable report with exact shared destination files,
  missing requirements, dependency loops, hook and keyboard-shortcut clashes, and compatibility
  warnings. It does not change the order or install anything.
- **Deciding who wins a shared file.** Expand a shared-file finding and each mod involved gets a
  **Make this one win** button. Clicking it moves that mod below the others in the load order, so
  its copies of those files are the ones installed. The list behind changes straight away.
- **Finding out whether a conflict matters.** **Find a fix…** reads each mod's Nexus description
  through the API, and its bug reports and comments by reading the public pages — there is no API
  for those two. It quotes anything that names one of the other mods or uses compatibility wording,
  then gives you one-click links to each mod's bug reports, comments and optional files, plus a web
  search naming both mods. Whatever you work out gets recorded on the issue: not a real conflict, a
  patch exists (paste its link), or genuinely incompatible.

  The page reading is best-effort and says so: AIM sees only the first page of each tab as a
  signed-out visitor would, and if Nexus changes its layout the result is no quotes rather than
  wrong ones. The links are always shown, so nothing depends on the scrape working.
- **Rebinding a clashing shortcut.** Shortcut clashes list the mod names, with the install path on
  hover. **Rebind…** moves one mod onto a key nothing else is using by editing its own binding —
  AIM backs the mod up first, and you can undo it from right-click → **Restore the previous
  version**. Once nothing shares the key the finding marks itself solved. Install again for the
  change to reach the game. The button is greyed out for mods still in a `.zip`/`.rar`, and for
  mods that build their bindings at runtime rather than declaring them; hover it for the reason.
- **Marking an issue as fine.** These checks are deliberately cautious — two mods writing the same
  sprite is reported because it *might* matter, not because it does. Tick the box beside a finding
  once you have looked at it and are happy with it. It moves to a dimmed, struck-through section at
  the bottom, which the *Show issues I have marked as fine* toggle hides or reveals, so you can
  always find it again and untick it. A dismissal is tied to the mods **and the versions** involved:
  if either mod updates, the issue comes back so you can judge the new version on its own. These
  judgements are stored in `aim_dismissed_issues.json` in the mods folder.
- **Drag/drop load order.** Drag a mod by its grip to reorder it. When holding it near the top or
  bottom edge of the list, AIM scrolls automatically so long lists do not require repeated drags.
- **Release notes.** The document icon after a mod's version opens what its author wrote about each
  release. Hover it for the newest version's notes; click it for the full history, every version
  under its own heading, newest first. It only appears for mods AIM knows the Nexus page for, since
  that is where the notes come from — and only mods with a Nexus API key set can fetch them.

  Notes load the first time you hover or click rather than for every mod at startup, which would be
  one Nexus request per mod on every launch. They are then cached in `aim_changelogs.json` in the
  mods folder, tied to the version installed, so updating a mod fetches the new release's notes and
  everything else costs nothing. Deleting that file loses nothing but the next fetch.
- **Selecting a range.** Click one mod's checkbox, then shift-click another, and every row between
  them takes whatever the clicked one just became — ticking or unticking a run of mods in two
  clicks. The range follows what is on screen, so a search or filter is respected.
- **Sort A–Z and filter.** Three checkboxes above the list: **Sort A–Z** puts it in name order so
  you can find a mod in a long list, **Sort by recently updated** brings whatever you just installed
  or updated to the top, and **Only mods needing attention** narrows it to mods with an
  update waiting plus mods the last check could not reach. The two sorts are alternatives; turning
  one on turns the other off. All are views only — your load order is
  untouched, each row still shows its real load-order number, and turning them off restores the true
  order. Dragging is paused while either is on, because reordering a filtered or re-sorted list
  would have no clear meaning for the order underneath. The **↑** and **↓** buttons jump to the top
  and bottom of the list.
- **Search.** The search field filters the already discovered list by localized or original mod
  name, author, description, and version. It does not rescan archives or contact Nexus. Filtering
  does not change the saved selection or order; drag/drop is paused until the search is cleared.
- **Edit a mod's files.** Right-click a mod → **Edit the mod's manifest** or **Edit the mod's
  config** opens that file in whatever text editor you have associated with it, falling back to
  Notepad on Windows where `.json` and `.toml` often have no association. Both items are greyed out
  when there is nothing to open — including for mods that are still `.zip` or `.rar`, because an
  edit there would be discarded the next time the archive is read. After editing, use gear menu →
  **Reload mod list** to pick up the change.
- **Remove a mod.** Right-click a mod → **Remove mod…** takes it out of the mods folder. AIM names
  the exact folder and asks first, then sends it to the Recycle Bin rather than erasing it. Files
  the mod has already installed into the game stay until the next install or uninstall rebuilds
  `assets.zip`.
- **Keybinds.** The **Keybinds** button lists every key and controller button your mods have bound.
  Click one to change it — press a key to capture it, or pick a `GAMEPAD_*` name from the list,
  since AIM cannot read a controller. Clashing bindings are red, and hovering one names the other
  mods on that input. The list holds only names the game accepts; a mod set to `ALT` or a numpad key
  silently falls back to its default, so AIM will not write one.

  Mods keep these settings in the game's own config folder (`%LOCALAPPDATA%\FieldsOfMistria\...\mod_data\`),
  not in the mods folder — which is why a mod update does not lose them. What does lose one is the
  mod resetting its own settings after a version bump. AIM remembers what you chose and offers to
  put it back, listing what changed. A setting the update removed is dropped instead of restored:
  the feature is gone, so the key would be bound to nothing.

  Editing is disabled while Fields of Mistria is running, because the game rewrites every mod's
  settings when it exits and would silently overwrite the change.
- **Check updates.** Checks the selected mods against their Nexus pages, with a dropdown for all
  mods or for installing every pending update in one go. Each mod's current version is kept as a
  backup you can restore.
- **The update badge.** The green badge on a mod's row downloads and installs the update in place
  when AIM knows which Nexus file it is. For a mod AIM only has a URL for — a GitHub release, or a
  Nexus mod you have not associated yet — it opens the download page instead; the tooltip says
  which. Use right-click → **Associate with Nexus…** to turn the second case into the first.

  Nexus only issues direct download links to Premium accounts. On a free account AIM opens the
  mod's page so you can use its **Mod Manager Download** button, which AIM picks up automatically
  when registered to handle those links (gear menu → Nexus downloads). If you have Premium and are
  still being sent to the page, AIM now checks the account and says so — that combination almost
  always means the API key has been revoked or regenerated.
- **Versions.** A mod AIM has updated keeps its earlier copies, and the **Versions** dropdown on its
  row rolls back to any one of them — not just the newest, which matters when the version you want
  is the one before whichever update broke things.
- **Automatic refresh.** The mods folder is watched while AIM is open, so a mod folder or archive
  copied in appears in the list a couple of seconds later.

## Optional localized mod metadata

AIM supports optional language-specific manifest fields for the mod name and description. The normal `name` and `description` fields remain the fallback, so existing mods do not need to change. The supported suffixes are:

`en`, `bg`, `pl`, `de`, `fr`, `nl`, `pt-br`, `ru`, `id`, `zh-hans`, `zh-hant`, `ko`, `ja`, `es`, and `uk`.

For example, in a TOML manifest:

```toml
name = "Bulgarian Localization"
name_bg = "Българска локализация"
description = "Adds Bulgarian Language to the game."
description_bg = "Добавя български език в играта."
```

The same optional fields can be used in a JSON manifest. The suffix can be
different for each field; for example, this uses a Japanese name and a
French description:

```json
{
  "name": "Example Mod",
  "name_ja": "サンプル Mod",
  "description": "Adds a small example feature.",
  "description_fr": "Ajoute une petite fonctionnalité d'exemple."
}
```

When the AIM interface is set to a supported language, it uses that language's suffix, such as `name_bg`/`description_bg` or `name_pl`/`description_pl`. If a language-specific field is missing, AIM uses the standard `name` and `description` fields instead. These optional fields are ignored by MOMI and do not change the normal manifest format.

## Updating the game

After a Fields of Mistria update, start AIM and reinstall the enabled mods. When the new `assets.zip` is a valid vanilla archive and the game executable also changed, AIM automatically adopts it as the new pristine source; no manual `assets.bak.zip` creation is required. AIM keeps the previous backup with a timestamped name until the update is accepted. If the archive is damaged or the update cannot be verified, AIM preserves the existing backup and asks you to verify the game files through Steam. Mods made for an older game or installer version may still need to be updated by their authors.

## Troubleshooting

- If the game location is not detected, place AIM next to `Maybe.toml` or select the game directory in Settings.
- If no mods appear, check that AIM is looking at the intended mods folder, that the manifest is at the mod root, and that the mod supports Fields of Mistria 1.0.x. The folder may be next to the game, next to AIM, or selected manually.
- If installation fails, AIM keeps the previous live archive, shows the failing mod when available, and writes a diagnostic log under the AIM local data directory.
- If the game was modified outside AIM or the pristine backup is missing, restore/verify the game files through Steam before trying again.

Nexus downloads and updates:

- **Known issue for older local installations:** mods installed manually or by an older AIM build
  before AIM 0.1.5 may not have a Nexus file identity recorded. To update one from AIM, right-click
  its name or row, choose **Associate with Nexus...**, and provide its Nexus page URL or an exact
  `nxm://` link.
- AIM currently uses each user's personal Nexus API key for Nexus downloads and update checks.
  OAuth support for AIM is not available yet and depends on Nexus application approval.
- If clicking **Mod Manager Download** does nothing, check gear menu → **Nexus downloads**: the line under **Handle "Mod Manager Download" links** says who currently owns them. A browser installed as a Flatpak or Snap may be unable to launch any handler, in which case copy the link address and use **Install from a copied nxm:// link**.
- If an update check says AIM cannot tell which Nexus mod something is, that mod was not downloaded through AIM and its manifest has no Nexus link. Downloading it once through AIM records the connection.
- If an update refuses to download and offers the mod's page instead, the account is not premium. Nexus only issues download links to free accounts through the website button; the page it opens is the supported route.
- Previous versions live in `.aim-backups` inside the mods folder. If a rollback is not offered, no backup exists yet — they start being kept the first time a mod is updated through AIM.

For bugs and fork-specific support, use the [fork issue tracker](https://github.com/AcTePuKc/Mods-of-Mistria-Installer/issues). The upstream project and its documentation remain available at [Garethp/Mods-of-Mistria-Installer](https://github.com/Garethp/Mods-of-Mistria-Installer).

## Contributors

See [Contributors.md](Contributors.md) for the people who have contributed to
AIM and the areas they worked on.

## Development

Build the solution with .NET 10:

```powershell
dotnet build ModsOfMistriaInstaller.sln --configuration Release
dotnet test ModsOfMistriaInstaller.sln --configuration Release
```

The build depends on SixLabors.ImageSharp 4.x, which refuses to compile without a Six Labors license
key. Community licenses are free for open-source and non-commercial projects and can be requested at
[licensing.sixlabors.com](https://licensing.sixlabors.com); they are valid for one year, so an
expired key produces the same build error as a missing one.

**Never commit `sixlabors.lic` or a license key.** Keys are personal to the license holder, and
`**/sixlabors.lic` is git-ignored for that reason. Supply yours in one of two ways:

```powershell
# A file the build finds on its own
Copy-Item path\to\sixlabors.lic ModsOfMistriaInstallerLib\sixlabors.lic

# Or the key itself, for one session - use the whole file, not just the Key field
$env:SixLaborsLicenseKey = Get-Content -Raw path\to\sixlabors.lic
```

CI writes the file from the `SIXLABORS_LICENSE` repository secret. GitHub does not expose secrets to
workflows triggered by a pull request from a fork, so that CI run stops at the license step and the
maintainer has to build the branch themselves.

`build-windows-exe.ps1` publishes the single-file Windows executable the same way the release
workflow does, and checks for the key before it starts.

The release workflow builds the GUI and CLI for the supported desktop targets and uploads artifacts only to releases in this fork. Nexus publishing is manual and is not triggered by a normal GitHub release.

The repository does not include game archives or copyrighted game localization data.
