# Engine Fix: store_pet_cosmetic_entry

Lets a store entry declare a validated pet-cosmetic set and produce a `PetCosmetic` item.

`store_pet_cosmetic_entry` is an **engine fix**, an anchored edit with no hook behind it. Nothing dispatches. It extends the store-item data parser to match the already-supported pet-cosmetic reward shape. See [Seams](../SEAMS.md).

## Placement

| | |
| --- | --- |
| **File** | `gml/scripts/Stores.gml` |
| **Locator** | text anchor: the end of `parse_store_item(entry)`, after the animal-cosmetic branch and before its required-item assertion |
| **Feeds** | (no hook) |
| **Marker** | `mmapi_store_pet_cosmetic_entry` |

## The Edit

Vanilla store entries can describe normal items, animal cosmetics, furniture, animals, and a few other item kinds, but the parser lacks a `pet_cosmetic` branch. The reward parser already accepts the field for the chicken-statue path, so a mod could define a valid pet cosmetic but could not sell it through a normal store.

The fix accepts `pet_cosmetic = "<set-name>"` on a store entry. It validates the named set against `PET_PROTOTYPE.cosmetic_sets`, creates `new LiveItem(ItemId.PetCosmetic)`, and assigns `item.pet_cosmetic_set_name`. An unknown set fails during Setup with the same clear assertion style used by sibling cosmetic entries.

No vanilla store entry carries `pet_cosmetic`; without modded data the new branch is never reached, so staged vanilla behavior remains identical to pristine.

## See Also

- [pet_appearance_popup_scrollable](pet_appearance_popup_scrollable.md) - Keeps an extended pet-appearance picker usable when pet-skin mods add many variants.
- [CATALOG](../CATALOG.md#engine-fixes-and-the-call-rewrite) - The complete hook-less catalog surface.
