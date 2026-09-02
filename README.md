# AIM — Alternative Installer for Mistria 0.1.9

This is an independently maintained alternative installer for **Fields of Mistria 1.0.x**, based on the open-source **Mods of Mistria Installer (MOMI)** project.

AIM is a fork of MOMI. It was renamed to avoid confusion between the two applications while preserving the upstream history, attribution, and technical compatibility. AIM is not affiliated with or endorsed by the original MOMI project.

AIM is not intended to replace MOMI. It exists to provide capabilities that are currently needed by this fork while remaining compatible with the upstream project. If MOMI later adopts at least the capabilities that motivated this fork and fully meets the project's needs, AIM may be retired in favour of the upstream project.

The current AIM development line is `0.1.9`.

## Preview

![AIM preview](aim-preview.gif)

<sub>Visual preview of AIM: language switching, mod installation and removal, load-order management, mod selection, and installation status messages.</sub>

## Fork-specific improvements

Compared with the upstream 0.15.10 line, this fork focuses on Fields of Mistria 1.0.x support and safer everyday use:

- Rebuilds are staged from a verified pristine archive and validated before the live `assets.zip` is replaced.
- Failed installations keep the previous working archive and provide a mod-specific diagnostic log where possible.
- TOML validation, custom font installation and manual-load animation content are supported for current 1.0.x mods.
- The UI remembers profiles and load order, behaves better on high-DPI displays, and includes a guarded **Play** button.
- Update checks, release uploads and the GitHub link belong to this fork rather than the upstream repository.

## Nexus integration and mod list tools

Nexus account features are implemented with OAuth PKCE. Nexus has registered AIM as a public OAuth
client; AIM does not accept or fall back to personal Nexus API keys.

| What | Where it lives in the UI |
| --- | --- |
| Nexus **Vortex download button** (`nxm://`) links download and unpack straight into the mods folder after OAuth sign-in | Gear menu → **Nexus downloads** |
| Check one mod, the selected mods, or every mod for updates after OAuth sign-in | Right-click a mod, or gear menu → **Nexus downloads** |
| Update a mod from Nexus, keeping the previous version as a backup you can restore after OAuth sign-in | Right-click a mod |
| Freeze a mod so update checks leave it on the version it is on | Right-click a mod |
| Open a mod's Nexus page or its folder | Right-click a mod |
| Select or clear every mod at once, with a summary of what the selection means | Checkbox above the mod list |
| **Suggest order** — order mods so each loads after what it requires, and report what it cannot decide | Button above the mod list |
| Mods copied into the mods folder appear without reopening AIM | Automatic |

Full details are in [Downloading mods from Nexus](#downloading-mods-from-nexus-vortex-download-button)
and [Mod list tools](#mod-list-tools) below.

### What it does not change

- Installing still rebuilds `assets.zip` from the pristine backup using the mods that are ticked, so
  a ticked mod means "in the game" and nothing is unticked for you. Downloading a mod does not install
  it; it appears in the list and waits for **Install** like any other mod.
- ZIP and RAR mods are still read in place. A downloaded archive is unpacked because AIM knows it is
  a fresh download, but an archive you drop in yourself is left exactly as it is.
- No existing file format, profile or command-line flag changes. The new state lives in two new
  files: `aim_nexus.json` in the mods folder, and `nexus.json` in `%LOCALAPPDATA%\AIM`.

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

### Interrupted recovery

If publishing `assets.zip` succeeds but publishing AIM's installation state fails, AIM retains
`assets.momi.pending-state.json` beside the archive. Do not delete it: restarting or running AIM
again should complete recovery. If recovery fails, preserve the diagnostic log and use it when
reporting the problem.

## Downloading mods from Nexus (Vortex download button)

AIM uses OAuth PKCE for Nexus account access. The browser authorization flow, localhost callback,
state validation, authorization-code exchange, and token refresh are implemented. Nexus account
sign-in, the **Vortex download button**, downloads, and update checks are available after sign-in.

These Nexus operations are GUI-only. AIM CLI does not register `nxm://`, sign in to Nexus, download
mods, or check for mod updates.

### Setting it up

Open the gear menu → **Nexus downloads**, sign in through the browser, and choose **Handle Vortex
download links**. The line under that option shows whether AIM currently owns `nxm://` links. The
browser may ask once for permission to open AIM.

### What happens on a download

- The link is handed to the AIM window you already have open. A second window is never opened.
- The mod is downloaded from Nexus and unpacked into your mods folder, anchored on the mod's
  `manifest.toml`, so an archive with an extra wrapper folder still lands in the right place.
- Downloading a mod you already have asks before replacing it, and the previous copy is kept until
  the new one is written successfully.
- Downloading does not install mods into the game. The new mod appears in the list, and you still
  choose when to press **Install**.

### Notes and limits

- OAuth account access uses AIM's public Nexus client registration. No personal API key is
  requested, stored, or accepted by AIM.
- Registration is per-user and never needs administrator rights: `HKCU\Software\Classes\nxm` on
  Windows, a `~/.local/share/applications/aim-nxm-handler.desktop` entry plus `mimeapps.list` on
  Linux and the Steam Deck.
- If another mod manager already owns `nxm://`, AIM says so and asks before taking over. Turning the
  option off again only removes AIM's own registration.
- A browser installed as a Flatpak or Snap may not be able to launch a handler outside its sandbox.
  In that case, right-click the **Vortex download button**, copy the link address, and use gear menu →
  **Nexus downloads** → **Install from a copied nxm:// link**.
- You can also associate a manually installed mod with Nexus by right-clicking the mod's name or
  row and choosing **Associate with Nexus...**. A normal Nexus page URL enables version checks;
  a copied `nxm://` link identifies the exact Nexus file. If the same version is already present,
  AIM records the association without downloading it again. If you choose **Yes** when AIM asks
  whether to replace an existing file, it might download that file again.
- Nexus collections are not supported; download the mods in them individually.
- Downloaded archives are limited to 20,000 entries, 512 MiB per extracted entry, and 2 GiB total
  extracted data. Extraction can be cancelled. A Nexus archive containing several mods is applied
  as one bundle: if one mod fails, AIM restores earlier replacements. If a restore also fails, AIM
  reports the retained backup paths for manual recovery.

### Keeping mods up to date

AIM remembers which Nexus mod and file each download came from, in `aim_nexus.json` beside the
profiles. Mods installed by hand are recognised too, as long as their manifest points at a Nexus page.
You can also right-click the mod's name or row and choose **Associate with Nexus...**. Use a normal
Nexus page URL for version checks, or a copied `nxm://` link when the exact Nexus file must be
identified.

- Right-click a mod → **Check for an update**, or use gear menu → **Nexus downloads** → **Check
  selected mods for updates** / **Check all mods for updates**.
- A mod with an update shows the green badge. Click it, or right-click → **Update from Nexus**, to
  download and replace the mod through AIM.
- Free-account direct downloads may still need the short-lived token supplied by the website's
  **Vortex download button**. If Nexus refuses a page-based update download, AIM opens the latest
  file page so you can start it through Vortex instead.
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
- **Drag/drop load order.** Drag a mod by its grip to reorder it. When holding it near the top or
  bottom edge of the list, AIM scrolls automatically so long lists do not require repeated drags.
- **Search.** The search field filters the already discovered list by localized or original mod
  name, author, description, and version. It does not rescan archives or contact Nexus. Filtering
  does not change the saved selection or order; drag/drop is paused until the search is cleared.
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

- OAuth PKCE is implemented for Nexus downloads and update checks. AIM does not use personal Nexus
  API keys.
- **Known issue for older local installations:** mods installed manually or by an older AIM build
  before AIM 0.1.5 may not have a Nexus file identity recorded. To update one from AIM, right-click
  its name or row, choose **Associate with Nexus...**, and provide its Nexus page URL or an exact
  `nxm://` link.
- If clicking the **Vortex download button** does nothing after signing in,
  check gear menu → **Nexus downloads**: the line under **Handle Vortex download links** says who
  currently owns them. A browser installed as a Flatpak or Snap may be unable to launch any handler,
  in which case copy the link address and use **Install from a copied nxm:// link**.
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

## Licensing

This project is licensed under GPLv3 or later (see `LICENCE.txt`).

The MMAPI framework in `ModsOfMistriaInstallerLib/Seam/Payload/mmapi` is
copyright © 2026 AnnaNomoly and is licensed under GPLv3 or later with
additional terms under GPLv3 section 7. Those terms require preservation of
the copyright, licence, and attribution notices, prohibit misrepresentation
of origin, and grant no trademark rights to the MMAPI name or branding. They
are included in `ModsOfMistriaInstallerLib/Seam/Payload/mmapi/LICENSE`, and
the MMAPI source files retain their licence headers.

The MMAPI seam catalog contains excerpts of Fields of Mistria game code used
as anchor patterns. That content belongs to NPC Studio; see the notice at the
top of `ModsOfMistriaInstallerLib/Seam/Payload/seams.toml`.
