# Seam: museum_donate_item

Emits the moment an item is donated to the museum.

`museum_donate_item` is a **template seam** (`op = "emit"`). It feeds [museum.donate_item](../hooks/museum.donate_item.md). Mod authors never write seams. You register handlers for the hooks they dispatch. See [Seams](../SEAMS.md).

## Placement

| | |
| --- | --- |
| **File** | `gml/scripts/Museum.gml` |
| **Locator** | structural target: after `register_item_to_museum(item_id)` in `donate_item_to_museum` |
| **Op** | `emit` |
| **Feeds** | [`museum.donate_item`](../hooks/museum.donate_item.md) |
| **ctx built** | `{ item_id: item_id }` |
| **Marker** | `mmapi_museum_donate_item` |

## The Edit

The generated emit lands immediately after `register_item_to_museum(item_id)`. It calls `mmapi_emit("museum.donate_item", { item_id: item_id })` in the uniform try/catch shape. The placement gives handlers the updated museum progress while keeping the pending renown entry push and the set-progress scan that decides the `DonationResult` engine-owned and still ahead.

`donate_item_to_museum()` is the donation menu's single entry point, so the hook sees exactly the player's donations (plus those of the engine's test suite). The engine's two other progress writers, save load and the `ALL_UNLOCKS` new-game path, call `register_item_to_museum()` directly, below this seam's reach. That is what keeps load-time restoration from replaying as donations. With zero handlers the seam is behaviorally identical to pristine.

For a pre-registration observation point, use the separate [museum.donation_attempted](../hooks/museum.donation_attempted.md) hook. It remains before this seam and is not an alias for `museum.donate_item`.

## See Also

- [museum.donate_item](../hooks/museum.donate_item.md) - This is the hook this seam dispatches.
- [player_renown_delta](player_renown_delta.md) - This is the filter the donation's pending renown entry drains through at day rollover.
- [quest_complete](quest_complete.md) - This is the other progression emit that queues a pending renown entry.
