# AIM Roadmap

This roadmap describes current work for AIM — Alternative Installer for Mistria.
Completed historical work is summarized below; detailed implementation history is
recorded in the changelog and git history.

## Current release: 0.1.9

### Completed

- [x] Replace the personal Nexus API-key path with public OAuth 2.0 Authorization Code + PKCE.
- [x] Use a fixed loopback callback, validate OAuth `state`, and support token refresh/disconnect.
- [x] Enable OAuth account features with Nexus's registered public `client_id`.
- [x] Preserve the opt-in `nxm://` handler takeover separately from Nexus account connection.
- [x] Replace the description tooltip with a pointer-driven description popup.
- [x] Make the detailed Issues window non-modal.
- [x] Import and reconcile the relevant MMAPI updates for the current 1.0.x game build.
- [x] Ship 118 MMAPI hooks and 127 seams with source headers, documentation, and license notices.
- [x] Validate every seam against the current game's pristine `assets.bak.zip`.
- [x] Run disposable real-tree install/uninstall regression tests with the GML compile checker.
- [x] Verify old-GML, MMAPI/GML, content-mod, and NXM behavior manually.
- [x] Include `README.md`, `LICENCE.txt`, and `MMAPI-LICENSE.txt` in release artifacts.

### Remaining before release

- [x] Receive Nexus' public OAuth `client_id` and enable the registered client.
- [x] Run the OAuth connected-account test after Nexus registration.
- [ ] Review the final release archive and Nexus description once more before publishing.
- [ ] Push the completed 0.1.9 branch and create the release package.

The NXM handler remains opt-in and independent from account sign-in. Free Nexus
accounts may still need to start direct update downloads from the website's
Vortex button when Nexus requires its short-lived website token.

## Compatibility follow-up

- [ ] Investigate the cosmetic interoperability issues recorded during the 1.0.4
      test pass, beginning with a comparison of one working and one failing
      generated player-asset package.
- [ ] Keep legacy-GML detection narrow and advisory. Add a new signature only
      after a reproducible compatibility problem is confirmed.
- [ ] Consider revising the legacy-GML warning text to say "compatibility cannot
      be verified" rather than implying that every detected mod is broken.

## MMAPI policy and candidate queue

The shipped catalog should remain stable during 0.1.9. New hooks or seams should
be added only when there is a demonstrated mod use case, a stable event boundary,
focused tests, and documentation.

Candidate work is tracked in [`docs/MMAPI/UPSTREAM_CANDIDATES.md`](docs/MMAPI/UPSTREAM_CANDIDATES.md).
The next candidates worth investigating are quest completion and the separate
player death/pass-out events. They should start in an experimental branch and
should not enter the release catalog without a real consumer.

## Future backlog

- [ ] Cache detailed conflict results until selected mods or their source files change.
- [ ] Improve validation translations and translate exception details.
- [ ] Add optional per-mod localization selection when a real mod requires it.
- [ ] Add validators for Simple Conversations.
- [ ] Add installers for `player_tools.json`, `farms.json`, `hyper_points.json`, and `t2_input.json`.
- [ ] Add a sound installer and a cutscene generator.
- [ ] Add automatic updating.
- [ ] Add a JSON browser and optional install-time JSON scrambling.
- [ ] Consider skipping mods that fail `CanInstall` instead of merely disabling installation.

## Completed project milestones

AIM has already completed the major installer, conflict-reporting, localization,
archive-transaction, duplicate-source, drag/drop, cosmetic-validation, and
MMAPI compatibility work previously listed under the old 0.1.x and 0.15.x
sections. Those sections were intentionally consolidated here so the roadmap
does not present completed work as active tasks or mix obsolete release numbers
with the current 0.1.9 line.
