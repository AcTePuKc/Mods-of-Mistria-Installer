# Engine Fixes: Tag Recipe Components

Tag recipe components let a recipe require a quantity of **any item carrying a tag**. They are data support, not a hook: nothing dispatches. The eight coordinated engine fixes keep the parser, UI, tooltip, availability, craft ceiling, and payment on one contract.

## Data Contract

```toml
{ tag = "ore", count = 3,
  display_item = "copper_ore",
  display_name = "mods/my_mod/recipe_tags/ore_name",
  display_description = "mods/my_mod/recipe_tags/ore_description" }
```

| Field | Meaning |
| ----- | ------- |
| `tag` | The actual category requirement. Any matching item is valid. |
| `count` | Matching items needed per craft; defaults to `1`. |
| `display_item` | A valid item id used only as the grid and recipe-scroll icon. It is not the required item. |
| `display_name` | Mod-supplied localization key for the tag's truthful display name. |
| `display_description` | Mod-supplied localization key explaining what matches. |

All three `display_*` fields are mandatory for a Tag component. This deliberately prevents a generic category requirement from masquerading as the representative item. The crafting hover tooltip hides the representative item's value and stats, then uses the supplied localized title and description.

## Fulfilment Contract

For a Tag component, AIM uses the same effective `get_modified_component_count(component, context, item_id) * quantity` value everywhere: affordability, displayed required amount, maximum crafts, and payment. Matching inventory is consumed first; then only storage nodes with `use_in_crafting = true` are used. A final assertion preserves the native rule that a craft never completes with unpaid ingredients.

This also makes the normal storage icon truthful: it appears precisely when the tag requirement is affordable with opted-in chest contents but not with the backpack alone.

## Catalog Entries

- `recipe_tag_component_presentation` and `recipe_tag_component_factory` parse and retain presentation data.
- `crafting_tag_chest_availability` and `crafting_tag_symmetric_payment` make counting and payment symmetric.
- `crafting_tag_display_item`, `crafting_tag_display_tooltip`, `crafting_tag_display_quantity`, and `recipe_tag_scroll_preview` make every visible recipe surface use the declared presentation.

See [Recipes](../RECIPES.md#tag-crafting-components), [crafting.component_count](../hooks/crafting.component_count.md), and [recipe_component_schema_validation](recipe_component_schema_validation.md).
