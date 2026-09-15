# Engine Fix: recipe_component_schema_validation

`recipe_component_schema_validation` is an **engine fix**, an anchored edit with no hook behind it. Nothing dispatches. It validates a fiddle item's `recipe` component before the vanilla parser chooses a component type. See [Seams](../SEAMS.md).

Vanilla chooses the first present selector in this order: `item`, `tag`, `hours`, `gold`, `skill`, then `essence`. Consequently, a malformed component such as `{ item = "wood", tag = "ore" }` was silently parsed as an item and its tag was discarded. AIM now stops setup with an error that identifies both the output `ItemId` and recipe-component index.

## Schema

Each component must declare exactly one selector:

- `item`, optionally with `count`
- `tag`, optionally with `count`
- `hours`, optionally with `minutes`
- `gold`
- `skill`, with required `level`
- `essence`

`minutes` is valid only with `hours`, `level` only with `skill`, and `count` only with `item` or `tag`. Other existing valid recipe data stays on the original vanilla parsing path; this fix only rejects malformed selector combinations that previously had ambiguous, order-dependent meaning.

| Field | Value |
| ----- | ----- |
| **Game file** | `gml/scripts/GameplaySystems/Items/Items.gml` |
| **Anchor** | The per-component loop immediately before vanilla's `item` selector branch. |
| **Marker** | `mmapi_recipe_component_schema_validation` |
| **Hook** | None |

## Related

- [crafting.component_count](../hooks/crafting.component_count.md) - Filters the effective cost only after a valid component exists.
- [max_crafts_zero_component](max_crafts_zero_component.md) - The separate craft-ceiling safety fix.
