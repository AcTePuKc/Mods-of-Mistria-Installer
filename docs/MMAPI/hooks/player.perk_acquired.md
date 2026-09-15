# Hook: player.perk_acquired

Know when a perk is fully acquired and active.

`player.perk_acquired` is an **event** hook. Register a callback with `mmapi_on`. See [Hooks](../HOOKS.md) for how registration and dispatch work.

## Contract

Fires at the end of `Ari.acquire_perk(perk)`, after the perk is flagged owned and active, its acquisition side effects run, the stats entry is pushed, and achievements refresh. ctx is `{ perk }`, the `Perk` enum id.

This hook is observation only. It fires on every acquisition path: the Dragonshrine purchase, the debug CLI grant, and the `ALL_UNLOCKS` new-game grant-all loop. It never fires on save load, which restores the perk arrays directly. Toggling an owned perk on or off never fires either, because those paths write `perks_active` directly. Use [player.acquire_perk](player.acquire_perk.md) when a handler needs the distinct pre-state moment.

| | |
| --- | --- |
| **Fires** | At the end of `Ari.acquire_perk(perk)`, after perk state and side effects are complete. |
| **ctx** | `{ perk }` |
| **Kind contract** | The callback observes the completed acquisition. Its return value is ignored. |

### The ctx struct

- `perk` - the acquired `Perk` enum id.

## Usage

```gml
// player.perk_acquired is an EVENT: the return value is ignored.
function refresh_perk_ui_player_perk_acquired(_ctx) {
    // _ctx is { perk }.
    // ARI.perks[_ctx.perk] and ARI.perks_active[_ctx.perk] are already true.
    // Refresh mod UI or react to the completed acquisition here.
}

// inside your latched register function (see Mod Anatomy):
mmapi_on("player.perk_acquired", refresh_perk_ui_player_perk_acquired);
```

## Engine Wiring

- Seam [`player_perk_acquired`](../seams/player_perk_acquired.md) dispatches from `gml/scripts/GameplaySystems/Player/Ari.gml`, after `acquire_perk()` finishes its state changes and side effects.

## See Also

- [player.acquire_perk](player.acquire_perk.md) - The distinct pre-state acquisition event.
- [player.essence_delta](player.essence_delta.md) - The Dragonshrine purchase cost applies before either perk event.
