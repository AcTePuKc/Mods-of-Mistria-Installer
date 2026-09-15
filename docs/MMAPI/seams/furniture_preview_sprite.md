# Seam: furniture_preview_sprite

Filters the held furniture ghost's main sprite.

`furniture_preview_sprite` is a template filter seam feeding [furniture.preview_sprite](../hooks/furniture.preview_sprite.md).

| | |
| --- | --- |
| **File** | `gml/scripts/GameplaySystems/Data/Grid/Furniture.gml` |
| **Locator** | Between the main sprite's native seasonal override and its assignment to the preview renderer |
| **Value** | `spr` |
| **ctx** | `{ object_id, prototype, cardinal_index, x, y, source: "main_sprite" }` |
| **Marker** | `mmapi_furniture_preview_sprite` |

The preview has no furniture node yet, so normal placed-node sprite hooks cannot reach it.
