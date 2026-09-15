# Hook: furniture.preview_sprite

Keep a held furniture preview's main and floor sprites in sync.

`furniture.preview_sprite` is a **filter** hook. Register it with `mmapi_filter`.

## Contract

It fires in `create_test_placement_furniture_draw_info()` while the player holds a placeable furniture item. The value is one sprite drawn by the translucent placement ghost. `ctx` is `{ object_id, prototype, cardinal_index, x, y, source }`.

`source` is `"main_sprite"` after the engine has applied its seasonal main-sprite override, or `"floor_sprite"` for the preview's raw floor sprite. The preview has no `winter_floor_sprite` override. There is no placed node yet; use `prototype`, `object_id`, and `cardinal_index` to recognize the item.

This is a hot path: it fires every frame while an item is held, once for each available sprite site. Return `undefined` immediately when the sprite is not yours.

## Usage

```gml
function fresh_coat_preview_sprite(sprite, ctx) {
    if (ctx.object_id != ObjectId.MyFurniture) return undefined;
    if (ctx.source == "main_sprite") return spr_my_furniture_preview;
    return undefined;
}

mmapi_filter("furniture.preview_sprite", fresh_coat_preview_sprite);
```

## See Also

- [furniture.floor_sprite](furniture.floor_sprite.md) - Filters the floor sprite of an already built furniture renderer.
- [object.node_sprite](object.node_sprite.md) - Filters a placed world node's main sprite.
