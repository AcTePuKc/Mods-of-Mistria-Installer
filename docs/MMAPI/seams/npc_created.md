# Seam: npc_created

Emits after `spawn_npc()` has fully initialized a villager instance.

`npc_created` is a **template seam** (`op = "emit"`). It feeds [npc.created](../hooks/npc.created.md). Mod authors consume the hook; they do not write seams.

| | |
| --- | --- |
| **File** | `gml/scripts/GameplaySystems/NPCs/NpcDatabase.gml` |
| **Locator** | `spawn_npc()`, after `new_inst.initialize(npc);` |
| **Op** | `emit` |
| **Feeds** | [`npc.created`](../hooks/npc.created.md) |
| **ctx** | `new_inst` |
| **Marker** | `mmapi_npc_created` |

The placement ensures the NPC data and FSM are live before handlers run. It covers spawns routed through `spawn_npc()` and does not cover direct cutscene/test instance creation.
