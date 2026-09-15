# Hook: dungeon.side_room_range

Adjust how far ahead dungeon side rooms may be placed.

`dungeon.side_room_range` is a **filter** hook. Register a callback with `mmapi_filter`. See [Hooks](../HOOKS.md) for how registration and dispatch work.

## Contract

Filters the floor range passed to `try_create_side_room()` at the head of that function, before the runner picks a side-room floor. ctx is `{ impl, is_ritual, start_floor }`. `is_ritual` is `true` when `impl == DungeonImpl.Ritual`, the pre-computed convenience flag, so consumers need not reference the `DungeonImpl` enum. Return an adjusted range to change the furthest floor the runner may choose for a treasure or ritual side room. Return the value unchanged (or `undefined`) to defer. Fires for every side-room impl the runner attempts.

> [!WARNING]
> This replaces the removed `dungeon.side_room_chance` hook for the 0.16.x engine. It is a breaking migration, not an alias: the old hook filtered a spawn-chance value, while this hook filters the floor-range value used by `irandom_range(start_floor, start_floor + range)`.

| | |
| --- | --- |
| **Fires** | At the head of `try_create_side_room()`, before the runner picks the side-room floor. |
| **Value** | The maximum number of floors ahead of `start_floor` the side room may be placed. |
| **ctx** | `{ impl, is_ritual, start_floor }` |
| **Kind contract** | The callback receives the current value and returns a replacement, or `undefined` to keep the current value. |

### The ctx struct

- `impl` - the side-room `DungeonImpl` the runner is attempting.
- `is_ritual` - `true` when `impl == DungeonImpl.Ritual`, pre-computed so your handler never has to reference the `DungeonImpl` enum.
- `start_floor` - the floor from which the runner measures the side-room range.

## Usage

```gml
// dungeon.side_room_range is a FILTER: you receive (value, ctx) and return a
// replacement, or undefined to keep the game's value.
function lucky_delver_dungeon_side_room_range(_value, _ctx) {
    // _value is the number of floors ahead of start_floor the runner may use.
    // _ctx is { impl, is_ritual, start_floor }.
    if (_value == undefined) return undefined; // test undefined BEFORE anything else
    // Let treasure rooms appear within a shorter span; leave ritual rooms alone.
    if (!_ctx.is_ritual) return min(3, _value);
    return undefined; // undefined = keep the game's value
}

mmapi_filter("dungeon.side_room_range", lucky_delver_dungeon_side_room_range);
```

## Engine Wiring

- Seam [`dungeon_side_room_range`](../seams/dungeon_side_room_range.md) dispatches from `gml/scripts/GameplaySystems/Dungeon/DungeonRunner.gml`, at the head of `try_create_side_room()`.

## See Also

- [dungeon.treasure_chest](dungeon.treasure_chest.md) - A treasure chest starts its drop chain.
- [items.treasure_distribution](items.treasure_distribution.md) - Filter the dungeon treasure roll itself.
- [dungeon.floor_enter](dungeon.floor_enter.md) - This hook fires as each floor is entered.
