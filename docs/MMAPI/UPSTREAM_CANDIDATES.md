# Upstream MMAPI parity and deferred candidates

This file records the comparison against the upstream MMAPI catalog. The
shipped 0.1.9 branch includes the stable entries from `upstream/main` at
`d437e08` (`v0.15.10`), plus the entity-creation hooks already accepted from
the upstream #173 work.

## Stable parity imported for 0.1.9

The following entries were missing from the branch's earlier catalog and are
now included, tested through the normal seam verification, and documented:

- `animal.production_gate`
- `animal.product_ready`
- `animal.product_drops`
- `animal.breeding_result`
- `animal.adoption_variant_unlocked`
- `date.cooldown`
- `date.cutscene`
- `ui.preset_popup_layout`
- `ui.backplate_sprite` (the canonical upstream name for the existing
  backplate seams)
- `ui_crafting_refreshed` (an additional seam feeding the existing
  `ui.menu_refreshed` hook)

The two breeding-result seams remain separate because the GeminiSeason path
has distinct engine context, while both dispatch the same hook.

## Deferred or experimental work

| Hook | Matching seam | Status | Reason |
|---|---|---|---|
| `ui.relationship_row_built` | `ui_relationship_row_built` | Experimental branch only | Present on upstream branch #180, but not yet in `upstream/main`; defer until it has a stable release baseline and a demonstrated consumer. |

Branch-only or localization/future-work changes are intentionally not copied
into the 0.1.9 release branch. Re-run this comparison when upstream publishes
the next stable MMAPI baseline.

## Import policy

1. Compare the current branch with `upstream/main` and relevant active
   upstream branches.
2. Import stable `upstream/main` entries whose anchors are verified against the
   current pristine assets.
3. Import a branch-only entry only when its contract is stable, its use case is
   demonstrated, and it does not expand the release scope unnecessarily.
4. Keep the catalog, generated documentation, counts, compatibility checklist,
   tests, and release notes synchronized in the same change.
