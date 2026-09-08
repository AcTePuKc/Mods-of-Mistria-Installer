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
workflows triggered by a pull request from a fork, so that CI run stops at the license step and the
maintainer has to build the branch themselves.

`build-windows-exe.ps1` publishes the single-file Windows executable the same way the release
workflow does, and checks for the key before it starts.

The release workflow builds the GUI and CLI for the supported desktop targets and uploads artifacts only to releases in this fork. Nexus publishing is manual and is not triggered by a normal GitHub release.

The repository does not include game archives or copyrighted game localization data.

## Localization audit

Run `pwsh -File tools/check-localization.ps1` to compare every translated
resource file with the English resource. Missing keys are reported as pending
fallbacks: the application can use the English resource for them, but they
still need a translation before that language is complete. Use
`-FailOnMissing` only when a language-complete build is required.
