# Seam: ui_sprite_spell_card_backplate

Routes each spell card's backplate through the `ui.sprite` filter.

`ui_sprite_spell_card_backplate` is a **text seam** (`anchor` + `replace`). It feeds [ui.sprite](../hooks/ui.sprite.md). Mod authors never write seams; they register handlers for the hooks they dispatch. See [Seams](../SEAMS.md).

## Placement

| | |
| --- | --- |
| **File** | `gml/scripts/UI/Anchor/Menus/SpellcastingMenu.gml` |
| **Locator** | the card build where the journal magic card backplate sprite is assigned |
| **Op** | text (filter dispatch) |
| **Feeds** | [`ui.sprite`](../hooks/ui.sprite.md) |
| **Value filtered** | `spr_ui_journal_magic_card_backplate` |
| **Context** | `{ source: "spellcasting_card_backplate", spell: spell }` |
| **Marker** | `mmapi_spell_card_backplate` |

## Behavior

The seam filters the default card sprite immediately before it is assigned.
The `spell` context value lets a handler choose a replacement per spell.
Returning `undefined` preserves the default card backplate.

With no handler, each spell card receives `spr_ui_journal_magic_card_backplate`
exactly as in the pristine game.

## See Also

- [ui.sprite](../hooks/ui.sprite.md) - The hook this seam dispatches.
- [ui_sprite_mines_backplate](ui_sprite_mines_backplate.md) - The other backplate filter site.
