# Nexus OAuth/API test build

The `aim/0.1.5-nexus-oauth-test` branch contains the initial distribution
boundary for a future Nexus application integration.

This branch deliberately does **not** contain:

- a Nexus API key;
- an OAuth client secret;
- a user access token;
- a working Nexus API request;
- a GitHub update check in the Nexus distribution.

The Nexus configuration is disabled by default until Nexus Mods registers AIM
and confirms the required OAuth/SSO or application API flow. The personal key
used by GitHub Actions for release uploads remains a repository secret and is
never part of the application build.

The future user-facing status and sign-in controls will be designed after the
registration response and tested in a separate build before publication.
