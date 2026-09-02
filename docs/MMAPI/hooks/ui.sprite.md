# Hook: ui.sprite

Swap the backplate sprite used by the mines menu or a spell card.

`ui.sprite` is a **filter** hook. Register a callback with `mmapi_filter`. See [Hooks](../HOOKS.md) for registration and dispatch details.

## Contract

The hook fires at two backplate assignment sites. The filtered value is the
engine's default sprite. `ctx.source` identifies the site:

- `mines_menu_backplate`: `ctx` is `{ source }`;
- `spellcasting_card_backplate`: `ctx` is `{ source, spell }`.

Return a replacement sprite, or `undefined` to keep the current value. The
engine remains responsible for assigning and enabling the backplate.

## Usage

```gml
function themed_backplate(_value, _ctx) {
    if (_ctx.source == "spellcasting_card_backplate" && _ctx.spell == Spell.Fire)
        return spr_my_fire_card_backplate;
    return undefined;
}

mmapi_filter("ui.sprite", themed_backplate);
```

## Engine Wiring

- Seam [`ui_sprite_mines_backplate`](../seams/ui_sprite_mines_backplate.md) filters the mines menu backplate on dungeon room start.
- Seam [`ui_sprite_spell_card_backplate`](../seams/ui_sprite_spell_card_backplate.md) filters each spell card's backplate as it is built.

With no handlers, both sites receive their pristine sprite values.

## See Also

- [ui.button_sprites](ui.button_sprites.md) - Swap the sprite set used to build a UI button.
- [ui.item_icon](ui.item_icon.md) - Swap an item's displayed icon.
