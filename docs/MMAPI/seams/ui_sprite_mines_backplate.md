# Seam: ui_sprite_mines_backplate

Routes the mines menu backplate through the `ui.sprite` filter.

`ui_sprite_mines_backplate` is a **text seam** (`anchor` + `replace`). It feeds [ui.sprite](../hooks/ui.sprite.md). Mod authors never write seams; they register handlers for the hooks they dispatch. See [Seams](../SEAMS.md).

## Placement

| | |
| --- | --- |
| **File** | `gml/scripts/UI/Anchor/Menus/MinesMenu.gml` |
| **Locator** | the dungeon-room branch inside the room-start callback |
| **Op** | text (filter dispatch) |
| **Feeds** | [`ui.sprite`](../hooks/ui.sprite.md) |
| **Value filtered** | `spr_ui_dungeon_backplate` |
| **Context** | `{ source: "mines_menu_backplate" }` |
| **Marker** | `mmapi_mines_backplate_sprite` |

## Behavior

On dungeon room start, the seam filters the default sprite and assigns the
result before enabling the backplate. The dispatch is isolated from the
engine's room-start path. Returning `undefined` preserves the default.

With no handler, the backplate receives `spr_ui_dungeon_backplate` exactly as
in the pristine game.

## See Also

- [ui.sprite](../hooks/ui.sprite.md) - The hook this seam dispatches.
- [ui_sprite_spell_card_backplate](ui_sprite_spell_card_backplate.md) - The other backplate filter site.
