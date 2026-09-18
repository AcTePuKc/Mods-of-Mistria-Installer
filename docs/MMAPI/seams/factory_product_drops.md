# Seam: factory_product_drops

Filters the rolled products before an apiary or terrarium drops them.

`factory_product_drops` is a **text seam** (`anchor` + `replace`). It feeds [factory.product_drops](../hooks/factory.product_drops.md). Mod authors never write seams. You register handlers for the hooks they dispatch. See [Seams](../SEAMS.md).

## Placement

| | |
| --- | --- |
| **File** | `gml/scripts/GameplaySystems/Data/Grid/Furniture.gml` |
| **Locator** | text anchor: the factory collection reward roll, optional apiary honeycomb roll, and reward sound block |
| **Op** | text (`anchor` + `replace`) |
| **Feeds** | [`factory.product_drops`](../hooks/factory.product_drops.md) |
| **Value filtered** | An array of rolled item ids or `LiveItem` values. |
| **ctx built** | `{ node: self, production_request: self.production_request, production_tier: self.production_tier }` |
| **Marker** | `mmapi_factory_run_product_drops_filters` |

## The Edit

Pristine collection immediately drops one tier reward, then may drop an apiary honeycomb. The replacement preserves both rolls, gathers their results into an array, filters that array, and then sends every final entry through the original `drop_item()` path. An invalid return falls back to the rolled array.

The requested hand item has already been popped before the filter runs. The collection animation, sound, and request reset are below the replacement and stay engine-owned. With zero handlers, the same reward and honeycomb rolls occur in the same order; only the physical item drops are performed after both rolls are known.

## See Also

- [factory.product_drops](../hooks/factory.product_drops.md) - This is the hook this seam dispatches.
- [animal_product_drops](animal_product_drops.md) - The related barn and coop product filter.
