# Add Nexus "Mod Manager Download" (`nxm://`) support

Branch: `feature/nexus-mod-manager-downloads` → `AcTePuKc/Mods-of-Mistria-Installer:main`

## What this adds

Clicking **Mod Manager Download** on a Fields of Mistria mod page now downloads the mod and unpacks
it into the mods folder, the way Vortex does for other games. Nothing about the existing install
flow changes: the mod simply appears in the list, and the user still chooses when to press
**Install**.

Setup is two menu items: gear → **Nexus downloads** → **Nexus API key…**, then **Handle "Mod Manager
Download" links**.

## How it works

| Piece | File | Role |
| --- | --- | --- |
| Link parsing | `ModsOfMistriaInstallerLib/Nexus/NxmLink.cs` | Parses `nxm://fieldsofmistria/mods/{id}/files/{id}?key=…&expires=…`, including the free-account download token and its expiry. Collections and other games are refused with a readable message. |
| Nexus API | `Nexus/NexusApiClient.cs` | v1 REST: `users/validate`, file metadata, and `download_link` (with the token when the account is not premium). HTTP failures are translated into something the user can act on. |
| Protocol handler | `Nexus/NxmProtocolHandler.cs` | Registers per-user: `HKCU\Software\Classes\nxm` on Windows; a `~/.local/share/applications/aim-nxm-handler.desktop` entry plus a direct `mimeapps.list` edit on Linux (Steam Deck often has no `xdg-mime`). Never needs administrator rights. |
| Single instance | `Nexus/NxmLinkListener.cs` | The browser starts a new process per click; it hands the link to the open window over a named pipe (a Unix socket on Linux) and exits, so no second window ever appears. |
| Download + unpack | `Nexus/NxmDownloadService.cs`, `Nexus/ModArchiveInstaller.cs` | Downloads with progress and cancellation, trying each CDN mirror, then extracts. |
| Credential storage | `Nexus/NexusSettings.cs` | API key stored per-user in `%LOCALAPPDATA%\AIM\nexus.json`, DPAPI-encrypted on Windows and `0600` elsewhere. Deliberately outside the mods folder, which people zip up and share. |
| UI | `ModsOfMistriaGUI/ViewModels/NexusDownloadsViewModel.cs`, `Views/NexusApiKeyWindow.axaml`, `Models/NexusDownloadModel.cs` | Gear-menu entries, an API-key dialog that validates before saving, and a downloads strip with progress and cancel. |

## Decisions worth reviewing

- **Personal API key, not SSO.** The websocket SSO flow needs an application slug that only Nexus
  staff can issue. The key screen validates against the API before saving, so a bad key fails there
  rather than at the first download. If AIM is ever granted a slug, SSO can be layered on top of the
  same storage.
- **Extraction is anchored on the manifest, not on the archive layout.** This is what makes the
  "nested folders" problem in the README a non-issue for downloaded mods, and it lets an archive
  that contains several mods install all of them.
- **Nexus file-name suffixes are stripped** (`Mod Name-78-2-1-1751991240.zip` → `Mod Name`), so the
  next release of a mod replaces the previous folder instead of installing beside it. A number that
  is part of the mod's own name is kept.
- **Replacing an existing mod asks first**, and the previous copy is moved aside and only deleted
  once the new one is written. A bundle that fails halfway rolls back the mods it already wrote.
- **Archive entries that resolve outside the mods folder are refused** — a download is not something
  to trust with arbitrary writes.
- **Registration is restored, not enforced.** If the user opted in and the registration has gone
  missing (a portable copy that moved), AIM re-registers silently. If another manager deliberately
  holds `nxm://`, AIM reports it and asks before taking over.

## Tests

`ModsOfMistriaInstallerLibTests/Nexus/`: link parsing (valid, premium, expired, collections,
malformed), archive extraction (root-level, nested flattening, bundles, conflict/replace, version
suffixes, zip-slip, missing manifest) and settings storage (round-trip, DPAPI, clearing, corrupt
file). No network calls in the suite.

## Not included

- Nexus collections.
- Premium "download without clicking the website button" flows beyond what the API already allows.
- New localisations: the 33 new strings are in `Resources.resx` (English) and fall back to English in
  the other languages until they are translated.
