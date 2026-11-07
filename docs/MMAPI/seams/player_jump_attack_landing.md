# Seam: player_jump_attack_landing

`player_jump_attack_landing` is a **text seam** (`anchor` + `replace`). It feeds [player.jump_attack_landing](../hooks/player.jump_attack_landing.md). Mod authors never write seams. You register handlers for the hooks they dispatch. See [Seams](../SEAMS.md).

It keeps the native out-of-grid early return, then filters the native non-collideable/non-water result for each tested bounding-box corner.

| Field | Value |
| ----- | ----- |
| **Game file** | `gml/scripts/Player/AriFsm.gml` |
| **Hook** | [player.jump_attack_landing](../hooks/player.jump_attack_landing.md) |
| **Marker** | `mmapi_player_run_jump_attack_landing_filters` |

The companion [player_jump_attack_water_exit](player_jump_attack_water_exit.md) is required whenever a handler permits water; it makes the completed attack transition to `Swim`.
