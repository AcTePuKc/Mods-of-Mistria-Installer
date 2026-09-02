# Nexus OAuth status and testing

AIM implements Authorization Code + PKCE for a public desktop OAuth client. It
contains no embedded client secret and does not accept a personal Nexus API key.

## Current status

Nexus has supplied AIM's public OAuth registration. Nexus account sign-in,
Vortex/`nxm://` downloads, and update checks are available after the user signs in.
The GUI uses that public client registration instead of falling back to another
credential type.

The Nexus API key used by the GitHub release-upload workflow remains a repository
secret and is never included in the application build.

## Implemented flow

With the registered public `client_id`, AIM:

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

Update checks can identify a newer Nexus file from a page association. A direct
download from a page association may still be refused for a free account because
the website supplies a short-lived download token only through its Vortex
button. In that case AIM opens the latest file page; this is an expected Nexus
account limitation, not a failed OAuth sign-in.
