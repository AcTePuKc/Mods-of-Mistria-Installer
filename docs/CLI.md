# AIM command-line interface

`AIM-cli` is the non-interactive command-line companion to the AIM GUI. Run
`AIM-cli --help` for the installed executable's complete usage summary.

## Installation commands

```powershell
AIM-cli --install
AIM-cli --uninstall
```

`--install` rebuilds the game archive from AIM's verified pristine backup. It
does not accept a mod path: it uses the current game and mods-folder discovery,
the same way as the standalone installer. `--uninstall` restores the verified
archive and removes the installed game manifest.

Optional install flags:

- `--strict-lints` enables strict GML linting.
- `--fail-on-skip` turns a GML skip into a failed install.
- `--compile-check on|off|require` selects automatic, disabled, or mandatory
  compile checking.

## Read-only inspection

```powershell
AIM-cli --list-mods
AIM-cli --status
AIM-cli --doctor
```

`--list-mods` reports discovered folders and archives, validation state, source
paths, installed state, and required hooks. `--status` reports the located game,
mods folder, archive presence, AIM state, and recorded installed mods.
`--doctor` checks the game location, mods folder, and whether the live archive
and verified backup are safe and consistent. It never repairs or replaces an
archive.

## Dry-run preflight

```powershell
AIM-cli --dry-run --install
```

This runs the per-mod manifest, seam, skip, and compile checks without opening
an archive transaction and without writing `assets.zip` or the game manifest.
It is a read-only preflight, not a complete simulation of cross-mod merge
interactions that only occur during the combined rebuild.

## Mod and seam checks

```powershell
AIM-cli --lint <mod-folder> [pristine-assets.zip] [--strict-lints] [--compile-check on|off|require]
AIM-cli --seam-check [pristine-assets.zip]
AIM-cli --seam-check-json [pristine-assets.zip]
```

`--lint` checks one mod without writing the game. The pristine archive argument
is optional when AIM can locate `assets.bak.zip`. `--seam-check` validates the
embedded seam catalog against a pristine archive; the JSON variant is intended
for scripts and CI.

## Machine-readable output

Inspection and dry-run commands use human-readable output by default. Use
either `--json` or `--toml` (not both), or the equivalent `--format json`,
`--format toml`, or `--format human`:

```powershell
AIM-cli --list-mods --json
AIM-cli --status --toml
AIM-cli --doctor --format json
AIM-cli --dry-run --install --json
```

Exit codes are stable for automation:

| Code | Meaning |
| ---: | --- |
| `0` | The command completed successfully. |
| `1` | A check found a problem, or a dry-run mod would be skipped. |
| `2` | Invalid arguments, missing input, or a check could not run. |

`--help` and `--version` are available on every platform build.
