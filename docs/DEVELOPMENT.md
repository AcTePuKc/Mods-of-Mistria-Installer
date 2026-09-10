# AIM development guide

## Build and test

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
workflows triggered by a pull request from a fork, so fork validation temporarily uses the compatible
keyless ImageSharp 3.1.11 package instead. Protected-branch builds always use the licensed 4.x package.

`build-windows-exe.ps1` publishes the single-file Windows executable the same way the release
workflow does, and checks for the key before it starts.

The release workflow builds the GUI and CLI for the supported desktop targets and uploads artifacts only to releases in this fork. Nexus publishing is manual and is not triggered by a normal GitHub release.

The repository does not include game archives or copyrighted game localization data.

## Branch and release policy

`main` contains only stable, releasable work. `develop` is the protected integration branch for the
next version. Start each change on a short-lived `aim/<topic>` branch and open its pull request against
`develop`. When a release is ready, open one release pull request from `develop` to `main`.

`Directory.Build.props` owns the public AIM version used by the GUI, CLI, and release packages.
Feature and fix pull requests do **not** bump it. The release pull request changes that one value,
updates the release-facing documentation, and creates the matching `v<version>` tag only after it is
merged. CI verifies that the visible release metadata agrees with the central version, and the release
workflow rejects a tag that does not match the built version.

The three-platform CI runs once for every pull request and again after merges to `develop` or `main`.
Windows CI also enforces complete localization resources, the no-em-dash rule, and CLI smoke tests.

GitHub prereleases are deliberate test candidates, not an artifact for every `develop` merge. Create a
tag such as `v0.3.0-rc.1` from `develop` and publish it as a GitHub prerelease when testers need a
downloadable build. The normal release workflow produces its platform binaries, while Nexus publishing
remains a separate manual decision. Stable tags are created from `main` and use the normal GitHub then
Nexus release sequence.

### Versioning before 1.0

Until AIM reaches `1.0.0`, version numbers communicate release scope rather than a promise of a frozen
API:

- `0.2.1`, `0.2.2`, and so on are compatible fixes, translation/documentation improvements, seam or
  hook catalog updates, and small game/MMAPI compatibility corrections.
- `0.3.0` is a substantial user-facing capability, a major game or MMAPI compatibility step, or a
  material workflow change.
- `1.0.0` is reserved for the point at which we are ready to promise a stable public workflow and
  compatibility contract.

## Localization audit

Run `pwsh -File tools/check-localization.ps1` to compare every translated
resource file with the English resource. Missing keys are reported as pending
fallbacks: the application can use the English resource for them, but they
still need a translation before that language is complete. Use
`-FailOnMissing` only when a language-complete build is required. The same
check reports em dashes; use `-FixEmDash` for the mechanical em-dash to
hyphen cleanup, or `-FailOnEmDash` to enforce the rule without modifying files.
