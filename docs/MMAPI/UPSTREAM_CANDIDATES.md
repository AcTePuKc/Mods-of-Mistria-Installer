# Upstream MMAPI candidates

This is a work list for upstream MMAPI entries that are not currently part of
AIM's shipped catalog. Nothing in this list is supported by AIM yet.

The first focused comparison covers upstream commits `01e83d8` and `f204bd5`.
The exact comparison contains 13 hooks and 13 seams. A few UI entries replace
or rename older entries, so they must not be treated as simple additions
without checking the local catalog first.

## Status values

Each candidate starts as:

```text
Candidate — not supported
```

It may later move to one of these categories:

- `Already supported` — the AIM catalog and runtime already provide it;
- `Candidate — needs a real mod` — technically plausible, but no use case has
  been confirmed;
- `Experimental branch only` — useful for testing or development, but not part
  of the normal AIM release;
- `Ready for AIM` — implemented, tested and documented in this repository;
- `Rejected/deferred` — no demonstrated benefit or too much compatibility risk.

## Focused candidate batch

| Hook | Matching seam | Initial status | What must be checked |
|---|---|---|---|
| `museum.donate_item` | `museum_donate_item` | Candidate — not supported | Find a real museum-related mod and verify event arguments. |
| `player.acquire_perk` | `player_acquire_perk` | Candidate — not supported | Confirm the perk lifecycle and whether a mod needs a pre/post seam. |
| `player.died` | `player_died` | Candidate — not supported | Test death handling and event timing without changing save behavior. |
| `player.pass_out` | `player_pass_out` | Candidate — not supported | Test pass-out behavior separately from ordinary death. |
| `player.skill_leveled` | Existing `player_xp_delta` seam was extended to provide it | Candidate — not supported | Confirm the existing AIM seam can expose the same event without changing its current behavior. |
| `quest.complete` | `quest_complete` | Candidate — not supported | Identify a real quest mod and verify completion payloads. |
| `renown.level_gained` | `renown_gains` | Candidate — not supported | Check whether level and rank changes are distinguishable and stable. |
| `renown.rank_gained` | `renown_gains` | Candidate — not supported | Check ordering when one action changes both level and rank. |
| `ui.spawn_tutorial_guard` | `ui_spawn_tutorial_guard` | Candidate — not supported | Test tutorial suppression without blocking normal tutorial state. |
| `date.cutscene` | `date_cutscene` / `date_cutscene_chain_args` | Candidate — not supported | Confirm cutscene arguments and whether chain handling is required. |
| `date.cooldown` | `date_cooldown` | Candidate — not supported | Find a real date/cooldown use case and test repeated calls. |
| `ui.preset_popup_layout` | `ui_preset_popup_layout` | Candidate — not supported | Verify layout timing and interaction with existing UI hooks. |
| `ui.backplate_sprite` | `ui_backplate_sprite_mines` / `ui_backplate_sprite_spell_card` | Candidate — not supported | Confirm whether the generic hook can safely replace the older specific paths. |

## Process

1. Pick one candidate or one tightly related pair.
2. Find a real mod that needs it, or explicitly record that no use case is
   known.
3. Compare the upstream seam against AIM's current 1.0.4 catalog and anchors.
4. Implement it only in an experimental branch first.
5. Add focused tests for the seam, dispatch behavior and failure handling.
6. Document the verified contract in `docs/MMAPI/`.
7. Move it to the normal AIM catalog only if it provides a demonstrated benefit
   without breaking existing mods or the MOMI-compatible baseline.

The list intentionally does not include the larger extension branch for custom
NPCs, perks, spells and status effects. That work requires a separate design
and runtime review before it can be considered a catalog update.
