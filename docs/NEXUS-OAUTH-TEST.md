# Nexus OAuth status and testing

AIM implements Authorization Code + PKCE for a public desktop OAuth client. It
contains no embedded client secret and does not accept a personal Nexus API key.

## Current status

The production registration is intentionally pending. Nexus account sign-in,
Vortex/`nxm://` downloads, and update checks remain unavailable until Nexus Mods
provides AIM's public OAuth `client_id`. The GUI reports that registration is
awaiting Nexus approval instead of falling back to another credential type.

The Nexus API key used by the GitHub release-upload workflow remains a repository
secret and is never included in the application build.

## Implemented flow

When a public `client_id` is supplied, AIM:

1. Creates a PKCE authorization request and opens the browser.
2. Listens for the redirect on the registered localhost loopback callback.
3. Validates the callback's exact path and OAuth `state`; wrong-state callbacks
   receive an error and do not complete the sign-in attempt.
4. Exchanges a valid authorization code for access and refresh tokens.
5. Stores tokens locally and refreshes them when needed. The PKCE verifier,
   authorization code, and state remain in memory only and are not written to
   settings or diagnostic logs.

The GUI bounds one sign-in attempt to five minutes. Cancellation, timeout, or a
valid OAuth error ends that attempt and reports the failure to the user.
