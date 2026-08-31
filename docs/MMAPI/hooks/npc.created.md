# Hook: npc.created

Customize each villager instance as it spawns, after initialization.

`npc.created` is an **event** hook. Register with `mmapi_on`. It fires after the NPC create-event chain and `initialize(npc)` have completed, so `ctx.me`, `ctx.npc_id`, and the NPC FSM are available. NPC instances are transient and can be recreated whenever rooms or schedules change.

| | |
| --- | --- |
| **Fires** | At the end of `spawn_npc()`, after initialization. |
| **ctx** | The `par_NPC` instance; its data is `ctx.me`. |
| **Return** | Ignored. Mutate the instance or register interactions. |

```gml
function my_mod_npc_created(_npc) {
    if (_npc.npc_id != NpcId.Adeline) return;
    with (_npc) {
        // Customize this spawned NPC or register a mod interaction.
    }
}

mmapi_on("npc.created", my_mod_npc_created);
```

Vanilla interactions are registered first, so a later mod interaction cannot shadow an active vanilla interaction on the same input. Direct cutscene/test creation paths that bypass `spawn_npc()` do not fire this hook.

## Engine Wiring

- Seam [`npc_created`](../seams/npc_created.md) emits after `spawn_npc()` calls `new_inst.initialize(npc)`.

## See Also

- [animal.created](animal.created.md)
- [pet.created](pet.created.md)
