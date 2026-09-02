# Hook: animal.created

Customize each barn or coop animal instance as it spawns.

`animal.created` is an **event** hook. Register with `mmapi_on`. It fires after the animal instance is created and linked back to its `Animal` data struct. Instances are transient, so expect repeated fires when rooms are loaded or a held animal is summoned.

| | |
| --- | --- |
| **Fires** | At the end of `spawn_animal()`. |
| **ctx** | The `obj_player_animal` instance; its data is `ctx.me`. |
| **Return** | Ignored. Mutate the instance or register interactions. |

```gml
function my_mod_animal_created(_animal) {
    with (_animal) {
        // Customize this spawned instance or register a mod interaction.
    }
}

mmapi_on("animal.created", my_mod_animal_created);
```

Vanilla interactions are registered first, so a later mod interaction cannot shadow an active vanilla interaction on the same input.

## Engine Wiring

- Seam [`animal_created`](../seams/animal_created.md) emits after `spawn_animal()` writes the instance to `animal.instance`.

## See Also

- [animal.pet](animal.pet.md)
- [pet.created](pet.created.md)
- [npc.created](npc.created.md)
