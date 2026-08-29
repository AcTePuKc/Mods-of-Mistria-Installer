# AIM — Alternative Installer for Mistria

AIM is an independently maintained, open-source mod installer for **Fields of Mistria 1.0.x**. It is a fork of [Mods of Mistria Installer (MOMI)](https://github.com/Garethp/Mods-of-Mistria-Installer), kept under a separate name to make it clear which application and release line a player is using.

AIM retains upstream attribution and MMAPI compatibility. It is not affiliated with or endorsed by the MOMI project.

## Preview

![AIM preview](aim-preview.gif)

<sub>Language switching, profiles, mod selection, installation status, and load-order management.</sub>

## Highlights

| Everyday mod management | AIM additions |
| --- | --- |
| Profiles with saved selections and load order | Read ZIP, RAR, and 7z mods directly without manually extracting them |
| Automatic detection of the game and nearby `mods` / `Mods` / `MODS` folders | Nexus **Vortex** (`nxm://`) link support and update checks |
| Safe install and uninstall from a verified pristine archive, with rollback protection | Download-update backups, restore, version freezing, and Nexus association for manually installed mods |
| Drag/drop ordering, automatic scrolling, and local mod-list search | On-demand **Check issues** report with exact shared paths, requirements, shortcuts, hooks, and compatibility findings |
| Optional direct or Steam game launch | Localized interface and optional localized manifest names/descriptions |

Compatibility warnings are advisory unless AIM cannot safely combine the selected mods. They are shown compactly in the list and in full through **Check issues**.

## Install AIM

1. Download the appropriate ZIP from the [latest GitHub release](https://github.com/AcTePuKc/Mods-of-Mistria-Installer/releases/latest).
2. Extract it anywhere and start the executable for your system:

   ```text
   Windows: AIM.exe
   Linux / SteamOS: AIM-linux
   macOS: AIM-osx
   ```

3. Let AIM find the game and mods folder, or select either folder from **Settings**.
4. Select the mods you want active and choose **Install**.

> [!IMPORTANT]
> Extract the ZIP first. Do not run AIM from inside the archive.

AIM supports Windows x64, Linux/SteamOS x64, and macOS x64. It targets Fields of Mistria 1.0.4 and later 1.0.x patches; individual mods can still need a particular game patch or installer version.

## Add mods

Put a mod folder or ZIP/RAR/7z archive directly in the selected mods folder. AIM reads supported archives in place; extracting them is optional.

The manifest may be at the mod root or inside one wrapper folder:

```text
mods/
├─ MyMod/
│  └─ manifest.toml                 supported
├─ MyMod/
│  └─ Wrapper/
│     └─ manifest.toml              supported
└─ MyMod/
   └─ Wrapper/
      └─ AnotherFolder/
         └─ manifest.toml           too deeply nested
```

The same one-wrapper limit applies inside an archive. A mod can provide either `manifest.toml` or `manifest.json`.

Keep one copy of each mod in the selected folder. Do not keep the same release both extracted and archived.

### Mod list tools

- **Select all / Deselect all** selects or clears the complete list in one operation.
- **Search** filters already discovered mods by localized or original name, author, description, or version. It never scans archives or changes the saved order. Drag/drop is paused while filtering so hidden rows cannot move accidentally.
- **Suggest order** makes only safe dependency-order changes: a mod is moved below the mods it requires.
- **Check issues** opens a copyable report of exact shared files, missing requirements, dependency loops, hook and shortcut clashes, and compatibility warnings. It does not install or reorder anything.
- **Drag/drop** moves a mod using its grip. Holding it near the top or bottom of the list scrolls a long list automatically.
- **Automatic refresh** notices folders and archives copied into the selected mods folder while AIM is open.

Installing rebuilds `assets.zip` from AIM's verified pristine backup using the selected mods. A selected mod means it will be in the game after the next successful install. If installation fails, AIM preserves the live archive and writes a diagnostic log.

## Nexus downloads and updates

AIM can handle the Nexus **Vortex** (`nxm://`) link and download a mod into the selected mods folder.

1. Open the gear menu → **Nexus downloads** → **Nexus API key...**.
2. Open your [Nexus API key settings](https://www.nexusmods.com/settings/api-keys), create a personal key, and paste it into AIM.
3. In the same menu, choose **Handle "Vortex" links**. AIM shows whether it currently owns the link type.
4. On a Fields of Mistria Nexus page, click **Vortex**. Your browser may ask permission to open AIM the first time.

Downloads are unpacked into the selected mods folder, but are not installed into the game until you select them and choose **Install**. When a downloaded mod replaces a prior copy, AIM keeps the prior version in `.aim-backups`; you can restore it from the mod's right-click menu.

Right-click a mod to:

- associate a manually installed mod with its Nexus page;
- check or install an update;
- open its Nexus page or folder;
- freeze it at the current version; or
- restore the most recent backed-up version.

Free Nexus accounts receive a download token only when they click the website's **Vortex** button. If AIM cannot download an update directly, it opens the correct Nexus files page instead.

> [!NOTE]
> AIM currently uses a personal Nexus API key stored on the local computer. Public OAuth registration for AIM is awaiting Nexus approval.

> [!NOTE]
> Mods installed manually or before AIM 0.1.5 may not have an exact Nexus file identity. Use **Associate with Nexus...** before checking them for updates.

## Optional localized mod metadata

Mod authors can optionally localize their manifest name and description. Standard `name` and `description` are always the fallback, so existing mods remain compatible.

Supported suffixes: `en`, `bg`, `pl`, `de`, `fr`, `nl`, `pt-br`, `ru`, `id`, `zh-hans`, `zh-hant`, `ko`, `ja`, `es`, and `uk`.

```toml
name = "Example Mod"
name_bg = "Българска локализация"
description = "Adds a small example feature."
description_bg = "Добавя малка примерна функция."
```

```json
{
  "name": "Example Mod",
  "name_ja": "サンプル Mod",
  "description": "Adds a small example feature.",
  "description_fr": "Ajoute une petite fonctionnalité d'exemple."
}
```

When AIM uses a supported interface language, it prefers the matching suffix such as `name_bg` or `description_fr`, then falls back to the normal field. MOMI ignores these optional fields.

## Updating the game and troubleshooting

After a Fields of Mistria update, start AIM and reinstall the selected mods. If the new `assets.zip` is a valid vanilla archive and the game executable changed, AIM adopts it as the new pristine source and retains the previous backup with a timestamp. If the update cannot be validated, verify the game files through Steam before trying again.

- If the game is not detected, select its folder in **Settings**. `Maybe.toml` should be beside the game executable.
- If no mods appear, confirm the selected mods folder and manifest placement.
- If the game was modified outside AIM or the pristine backup is missing, verify game files through Steam before installing again.
- Close AIM before moving, replacing, or deleting an archive it may be reading.

For bugs and support, use the [AIM issue tracker](https://github.com/AcTePuKc/Mods-of-Mistria-Installer/issues).

## Contributors

See [Contributors.md](Contributors.md) for contributors and the areas they worked on.

## Development

Build and test with .NET 10:

```powershell
dotnet build ModsOfMistriaInstaller.sln --configuration Release
dotnet test ModsOfMistriaInstaller.sln --configuration Release
```

The build requires a SixLabors.ImageSharp 4.x license. Community licenses are available for qualifying open-source and non-commercial projects from [Six Labors](https://licensing.sixlabors.com). Never commit `sixlabors.lic` or its key.

```powershell
# File-based local license
Copy-Item path\to\sixlabors.lic ModsOfMistriaInstallerLib\sixlabors.lic

# Or one-session environment variable; provide the complete license file contents
$env:SixLaborsLicenseKey = Get-Content -Raw path\to\sixlabors.lic
```

CI uses the `SIXLABORS_LICENSE` repository secret. The `build-windows-exe.ps1` script publishes the same single-file Windows executable used for releases. Nexus publishing remains a separately enabled, manual workflow.

The repository does not include game archives or copyrighted game localization data.
