# Seam: monster_draw

Emits at the end of every monster's world-space draw.

`monster_draw` is a **template seam** (`op = "emit"`). It feeds [monster.draw](../hooks/monster.draw.md). Mod authors never write seams. You register handlers for the hooks they dispatch. See [Seams](../SEAMS.md).

## Placement

| | |
| --- | --- |
| **File** | `gml/objects/Combat/par_monster.gml` |
| **Locator** | pristine `animation_end` method declaration, immediately after which the generated `draw_end` event is inserted |
| **Op** | `emit` |
| **Feeds** | [`monster.draw`](../hooks/monster.draw.md) |
| **ctx built** | `self` - the monster instance |
| **Marker** | `mmapi_monster_run_draw_callbacks` |

## The Edit

The generated emit creates a `draw_end` event on `par_monster`, immediately after the normal world-draw pass has rendered the monster and its status renderables. It calls `mmapi_emit("monster.draw", self)` in the uniform try/catch shape. At that point the monster is already on screen, so a handler draws on top of it in world space, with the monster instance in hand: `x`, `y`, `z`, `health`, and everything else the instance carries. A health bar above the monster is the canonical use.

This fires once per visible monster per frame in the world-draw pass, a hot path. Keep handlers cheap. With zero handlers the emit early-outs on an empty registry.

## See Also

- [monster.draw](../hooks/monster.draw.md) - This is the hook this seam dispatches.
- [monster_step_begin](monster_step_begin.md) - This seam is the same file's per-frame logic-side emit.
