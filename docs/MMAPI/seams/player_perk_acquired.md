# Seam: player_perk_acquired

Emits after a perk is fully acquired and active.

`player_perk_acquired` is a **template seam** (`op = "emit"`). It feeds [player.perk_acquired](../hooks/player.perk_acquired.md). Mod authors never write seams. You register handlers for the hooks they dispatch. See [Seams](../SEAMS.md).

## Placement

| | |
| --- | --- |
| **File** | `gml/scripts/GameplaySystems/Player/Ari.gml` |
| **Locator** | structural target: after `refresh_achievements([Requirement.HasAtLeastOneTierFivePerkPerCategory]);` in `acquire_perk` |
| **Op** | `emit` |
| **Feeds** | [`player.perk_acquired`](../hooks/player.perk_acquired.md) |
| **ctx built** | `{ perk: perk }` |
| **Marker** | `mmapi_player_perk_acquired` |

## The Edit

The generated emit lands after `acquire_perk()` writes the owned and active flags, runs its per-perk side effects, records the stat, and refreshes achievements. It calls `mmapi_emit("player.perk_acquired", { perk: perk })` in the uniform try/catch shape.

Every engine acquisition routes through this method: the Dragonshrine purchase menu, the debug CLI, and the `ALL_UNLOCKS` grant-all loop. Save load writes the perk arrays wholesale and never calls it, and enable/disable toggles write `perks_active` directly. With zero handlers the seam is behaviorally identical to pristine.

The separate [player.acquire_perk](../hooks/player.acquire_perk.md) event remains at the head of the same method for AIM handlers that need the pre-state moment. This seam is not a replacement or rename.

## See Also

- [player.perk_acquired](../hooks/player.perk_acquired.md) - This is the completed-acquisition event this seam dispatches.
- [player_acquire_perk](player_acquire_perk.md) - The existing pre-state event in the same method.
- [player_essence_delta](player_essence_delta.md) - The Dragonshrine purchase cost applies before either perk event.
