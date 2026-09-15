# Seam: dungeon_side_room_range

Routes the side-room floor range through the filter chain before the runner picks a floor.

`dungeon_side_room_range` is a **template seam** (`op = "filter"`). It feeds [dungeon.side_room_range](../hooks/dungeon.side_room_range.md). Mod authors never write seams. You register handlers for the hooks they dispatch. See [Seams](../SEAMS.md).

## Placement

| | |
| --- | --- |
| **File** | `gml/scripts/GameplaySystems/Dungeon/DungeonRunner.gml` |
| **Locator** | structural target: `try_create_side_room`, at head |
| **Op** | `filter` |
| **Feeds** | [`dungeon.side_room_range`](../hooks/dungeon.side_room_range.md) |
| **Value filtered** | `range` - the number of floors ahead of `start_floor` the runner may choose from |
| **ctx built** | `{ impl: impl, is_ritual: impl == DungeonImpl.Ritual, start_floor: start_floor }` |
| **Marker** | `mmapi_dungeon_run_side_room_range_filters` |

## The Edit

The generated filter lands at the head of `try_create_side_room()`. It reassigns the function's `range` through `mmapi_apply_filters("dungeon.side_room_range", range, ctx)` before the engine calls `irandom_range(start_floor, start_floor + range)`. Changing the value changes the span of eligible floors, not the chance that a side room will be attempted.

The ctx literal precomputes `is_ritual` as `impl == DungeonImpl.Ritual`, so handlers can special-case ritual rooms without referencing the `DungeonImpl` enum. Both `impl` and `start_floor` ride along raw. The function runs once for every side-room impl the runner attempts. With zero handlers the seam is behaviorally identical to pristine.

## Migration

`dungeon_side_room_chance` and `dungeon.side_room_chance` are removed for the 0.16.x engine. This is intentionally not an alias: the old locator referred to `chance_val` and `max_flr`, neither of which exists in the current function, and it represented a different game decision.

## See Also

- [dungeon.side_room_range](../hooks/dungeon.side_room_range.md) - This is the hook this seam dispatches.
- [dungeon_ladder_spawn](dungeon_ladder_spawn.md) - This is the other `DungeonRunner.gml` seam.
- [dungeon_treasure_chest](dungeon_treasure_chest.md) - This seam fires when a treasure chest in one of these rooms starts its drop chain.
