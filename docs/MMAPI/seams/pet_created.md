# Seam: pet_created

Emits after `spawn_pet()` creates a pet instance.

`pet_created` is a **text seam**. It feeds [pet.created](../hooks/pet.created.md). Mod authors consume the hook; they do not write seams.

| | |
| --- | --- |
| **File** | `gml/scripts/Pet.gml` |
| **Locator** | `spawn_pet(override_pet_initialization)` |
| **Op** | `emit` |
| **Feeds** | [`pet.created`](../hooks/pet.created.md) |
| **ctx** | The captured `obj_pet` instance |
| **Marker** | `mmapi_pet_created` |

The seam captures the return value of `instance_create_layer()` because the pristine function discards it, then emits after creation completes.
