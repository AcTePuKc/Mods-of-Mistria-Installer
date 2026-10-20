# Hook: ui.tooltip_opened

React once after an item tooltip is fully constructed.

`ui.tooltip_opened` is an **event** hook. Register a callback with `mmapi_on`.

## Contract

Fires at the end of `create_tooltip()`, after the tooltip has been spawned and its content nodes have been built. It is an edge event, not a per-frame callback.

| | |
| --- | --- |
| **Fires** | Once after an item tooltip is built and spawned. |
| **ctx** | `{ tooltip, item, source_node }` |
| **Return value** | Ignored. Handlers may update the constructed tooltip's text nodes. |

### The ctx struct

- `tooltip` - the live tooltip popup, with its title, description, and any applicable stat nodes.
- `item` - the `LiveItem` whose tooltip was created.
- `source_node` - the UI node the tooltip is attached to, or `undefined` when no source node was supplied.

## Usage

```gml
function example_ui_tooltip_opened(_ctx) {
    if (_ctx.tooltip.stat_number != undefined) {
        // Inspect or adjust the rendered stat label.
    }
}

mmapi_on("ui.tooltip_opened", example_ui_tooltip_opened);
```

## Engine Wiring

The `ui_tooltip_opened` seam dispatches immediately after `create_tooltip()` calls `popup.spawn()` and before it returns the popup.
