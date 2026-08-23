# AIM — Alternative Installer for Mistria 0.1.4

This is an independently maintained alternative installer for **Fields of Mistria 1.0.x**, based on the open-source **Mods of Mistria Installer (MOMI)** project.

AIM is a fork of MOMI. It was renamed to avoid confusion between the two applications while preserving the upstream history, attribution, and technical compatibility. AIM is not affiliated with or endorsed by the original MOMI project.

AIM is not intended to replace MOMI. It exists to provide capabilities that are currently needed by this fork while remaining compatible with the upstream project. If MOMI later adopts at least the capabilities that motivated this fork and fully meets the project's needs, AIM may be retired in favour of the upstream project.

The current AIM application version is `0.1.4`.

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
- Nexus **Mod Manager Download** links (`nxm://`) can be handled directly, so mods download and unpack into the mods folder without a manual extract step.

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
- Nexus collections are not supported; download the mods in them individually.

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

For bugs and fork-specific support, use the [fork issue tracker](https://github.com/AcTePuKc/Mods-of-Mistria-Installer/issues). The upstream project and its documentation remain available at [Garethp/Mods-of-Mistria-Installer](https://github.com/Garethp/Mods-of-Mistria-Installer).

## Development

Build the solution with .NET 10:

```powershell
dotnet build ModsOfMistriaInstaller.sln --configuration Release
dotnet test ModsOfMistriaInstaller.sln --configuration Release
```

The build depends on SixLabors.ImageSharp 4.x, which refuses to compile without a license key. CI
writes one from the `SIXLABORS_LICENSE` secret; for a local build, obtain your own key (free for
open-source and non-commercial use) from [licensing.sixlabors.com](https://licensing.sixlabors.com)
and save it as `ModsOfMistriaInstallerLib/sixlabors.lic`, which is already git-ignored. Note that a
pull request from a fork cannot read the repository secret, so its CI run will stop at that step.

`build-windows-exe.ps1` publishes the single-file Windows executable the same way the release
workflow does, and checks for the license key before it starts. Passing `-UseImageSharp3` builds
against ImageSharp 3.1.11 instead, for a local build without a key: the 3.x line is still covered by
the Six Labors Split License and the key requirement begins at 4.0.0. 3.1.11 is also the version that
fixes CVE-2025-54575, so do not pin anything older.

The release workflow builds the GUI and CLI for the supported desktop targets and uploads artifacts only to releases in this fork. Nexus publishing is manual and is not triggered by a normal GitHub release.

The repository does not include game archives or copyrighted game localization data.
