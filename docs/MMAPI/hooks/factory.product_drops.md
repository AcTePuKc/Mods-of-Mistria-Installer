# Hook: factory.product_drops

Change the products an apiary or terrarium drops on collection.

`factory.product_drops` is a **filter** hook. Register a callback with `mmapi_filter`. See [Hooks](../HOOKS.md) for how registration and dispatch work.

## Contract

Fires when an apiary or terrarium completes a factory collection, after the requested item is removed from the player's hand and the tier reward and apiary honeycomb bonus are rolled, and before items drop. The filtered value is an array of item ids or `LiveItem` values accepted by `drop_item()`. ctx is `{ node, production_request, production_tier }`.

Return a replacement array, mutate the array in place, or return `undefined` to keep the rolled products. A non-array result is ignored. An empty array suppresses only item drops: the hand pop, bounce animation, reward sound, and factory request reset still run. Each final array element uses the normal `drop_item()` call, so downstream [items.dropped](items.dropped.md) handlers still see every item.

| | |
| --- | --- |
| **Fires** | Once per completed apiary or terrarium collection, after rolling products and before dropping them. |
| **Value** | An array of item ids or `LiveItem` values. |
| **ctx** | `{ node, production_request, production_tier }` |
| **Kind contract** | Return a replacement array, or `undefined` to keep the current value. |

## Usage

```gml
function add_bonus_factory_product(_value, _ctx) {
    // _value is the final product array before drops begin.
    // _ctx.node is the factory grid node.
    if (_value == undefined) return undefined;
    // array_push(_value, ItemId.Honeycomb);
    return undefined;
}

mmapi_filter("factory.product_drops", add_bonus_factory_product);
```

## Engine Wiring

- Seam [`factory_product_drops`](../seams/factory_product_drops.md) dispatches from `gml/scripts/GameplaySystems/Data/Grid/Furniture.gml` after the roll and before the per-product `drop_item()` calls.

## See Also

- [animal.product_drops](animal.product_drops.md) - The equivalent filter for barn and coop production.
- [items.dropped](items.dropped.md) - Fires once for each final product that drops.
