# Post-PR2 integration workspace

This worktree is the clean integration base after Silver PR2 was merged into `main`.

- Worktree: `C:\WebStuff\AIM-pr2-integration`
- Branch: `aim/post-pr2-integration`
- Base commit: `df32906` (`Merge pull request #2 from SilvertongueRED/main`)
- Upstream: `origin/main`

## Integration order

Keep each topic in a separate commit and verify it before starting the next one:

1. Fix the validated `CrashSourceIndex` path-containment issue.
2. Rework the main layout so the mod list has usable space at 150% DPI and different window sizes.
3. Reapply the NXM handler and MMAPI hook work against the PR2 code.
4. Reapply FontInstaller/Conflict detection work after the hook contract is stable.

Do not merge old experimental branches wholesale. Review and port their changes against this
post-PR2 base, because PR2 changed several related UI and diagnostic paths.

The original `C:\WebStuff\AIM` checkout remains the historical/test workspace and is intentionally
not used as the clean integration base.
