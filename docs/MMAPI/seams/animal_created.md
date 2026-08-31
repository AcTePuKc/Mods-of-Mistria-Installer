# Seam: animal_created

Emits after a barn or coop animal instance has been created and linked to its data.

`animal_created` is a **template seam** (`op = "emit"`). It feeds [animal.created](../hooks/animal.created.md). Mod authors consume the hook; they do not write seams.

| | |
| --- | --- |
| **File** | `gml/scripts/GameplaySystems/Ranching/AnimalUtils.gml` |
| **Locator** | `spawn_animal()`, after the `animal.instance` assignment |
| **Op** | `emit` |
| **Feeds** | [`animal.created`](../hooks/animal.created.md) |
| **ctx** | `animal.instance` |
| **Marker** | `mmapi_animal_created` |

The placement ensures `ctx.me.instance` is already coherent. The event fires once per spawned instance, not once per animal in the save.
