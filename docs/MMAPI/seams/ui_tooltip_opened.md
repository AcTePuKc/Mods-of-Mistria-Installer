# Seam: ui_tooltip_opened

`ui_tooltip_opened` is a **template seam** (`op = "emit"`). It feeds [ui.tooltip_opened](../hooks/ui.tooltip_opened.md). Mod authors register handlers for the hook; they do not package seams themselves.

| | |
| --- | --- |
| **File** | `gml/scripts/UI/Anchor/Menus/TooltipMenu.gml` |
| **Locator** | Between `popup.spawn()` and `return popup;` in `create_tooltip()` |
| **Dispatch** | `mmapi_emit("ui.tooltip_opened", { tooltip: popup, item: item, source_node: source_node })` |
| **Frequency** | Once per tooltip construction; never per idle frame. |

The tooltip's content nodes are available to handlers at this point, making this a suitable place for a mod to adjust rendered labels without scanning UI state or polling every frame.

See [ui.tooltip_opened](../hooks/ui.tooltip_opened.md) for the hook contract.
