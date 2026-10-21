# Hook: player.jump_attack_landing

`player.jump_attack_landing` is a **filter** hook. Register a callback with `mmapi_filter`. See [Hooks](../HOOKS.md) for registration and dispatch rules.

It fires inside the Jump Attack perk's landing test, once per tested corner of the player's bounding box on each frame after a jump's peak while attack is buffered. Vanilla permits a corner only when its node is neither collideable nor water. Every tested corner must pass before the attack lands.

## Context

```gml
{
    x,           // tested room x coordinate in pixels
    y,           // tested room y coordinate in pixels
    node,        // GRID node index at that coordinate
    collideable, // the node's native collision flag
    water        // whether the node's terrain is TerrainKind.Water
}
```

Return a Boolean replacement, or `undefined` to retain the current decision. The engine combines the corner decisions with `&&`; a false result stops the remaining corner tests for that frame.

The engine rejects a monster or NPC beneath the player, and an out-of-grid corner, before dispatch. Those cases do not reach the hook.

## Example

```gml
function my_mod_jump_attack_landing(_allowed, _ctx) {
    if (_ctx.water && !_ctx.collideable) {
        return true;
    }
    return undefined;
}

mmapi_filter("player.jump_attack_landing", my_mod_jump_attack_landing);
```

When a handler allows a water landing, the companion seam sends the completed DownSmash to `PlayerState.Swim`. It therefore does not leave the player walking on water after the attack.

## Implementation

- Seam [`player_jump_attack_landing`](../seams/player_jump_attack_landing.md) filters each valid node decision.
- Companion seam [`player_jump_attack_water_exit`](../seams/player_jump_attack_water_exit.md) retains native water-transition behaviour for newly possible water landings.
- [combat.tarball_grid](combat.tarball_grid.md) is separate: it controls what the completed swing can affect.
