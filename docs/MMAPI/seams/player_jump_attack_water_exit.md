# Seam: player_jump_attack_water_exit

`player_jump_attack_water_exit` is a **companion text seam** for [player.jump_attack_landing](../hooks/player.jump_attack_landing.md). It dispatches no hook itself.

Vanilla can never enter the DownSmash state over water, so it always exits to `PlayerState.Default`. A landing handler may deliberately permit water. In that case this seam checks the landing node and changes only that exit to `PlayerState.Swim`, matching the normal Jump state's water transition.

| Field | Value |
| ----- | ----- |
| **Game file** | `gml/scripts/Player/AriFsm.gml` |
| **Provides** | [player.jump_attack_landing](../hooks/player.jump_attack_landing.md) |
| **Marker** | `mmapi_player_jump_attack_water_exit` |
