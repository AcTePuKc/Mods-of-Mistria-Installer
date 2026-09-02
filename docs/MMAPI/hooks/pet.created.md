# Hook: pet.created

Customize the farm pet instance as it spawns.

`pet.created` is an **event** hook. Register with `mmapi_on`. It fires after the pet create event, data attachment, FSM setup, and vanilla interaction registration. Pet instances are transient and may be recreated several times during normal play.

| | |
| --- | --- |
| **Fires** | At the end of `spawn_pet()`. |
| **ctx** | The `obj_pet` instance; its data is `ctx.me`. |
| **Return** | Ignored. Mutate the instance or register interactions. |

```gml
function my_mod_pet_created(_pet) {
    with (_pet) {
        // Customize this spawned pet instance.
    }
}

mmapi_on("pet.created", my_mod_pet_created);
```

Vanilla interactions are registered first, so a later mod interaction cannot shadow an active vanilla interaction on the same input.

## Engine Wiring

- Seam [`pet_created`](../seams/pet_created.md) captures the instance returned by `instance_create_layer()` and emits after `spawn_pet()` creates it.

## See Also

- [animal.created](animal.created.md)
- [npc.created](npc.created.md)
