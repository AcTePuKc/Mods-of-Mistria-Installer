# Seam: furniture_preview_floor_sprite

Filters the held furniture ghost's floor sprite.

`furniture_preview_floor_sprite` is a text seam feeding [furniture.preview_sprite](../hooks/furniture.preview_sprite.md).

| | |
| --- | --- |
| **File** | `gml/scripts/GameplaySystems/Data/Grid/Furniture.gml` |
| **Locator** | The preview's raw `floor_sprite` assignment |
| **ctx** | `{ object_id, prototype, cardinal_index, x, y, source: "floor_sprite" }` |
| **Marker** | `mmapi_furniture_preview_floor_sprite` |

The preview reads the raw prototype field, so the seam deliberately does not introduce a winter-floor override that vanilla does not have.
