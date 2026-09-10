# Seam: font_styles_loaded

Filters the text-style mapping returned by the engine's font loader.

`font_styles_loaded` is a **template seam** (`op = "wrap"`). It feeds [`font.styles_loaded`](../hooks/font.styles_loaded.md).

## Placement

| | |
| --- | --- |
| **File** | `gml/scripts/GameplaySystems/FiddleParsers.gml` |
| **Function** | `load_text_styles()` |
| **Op** | `wrap` |
| **Value** | The complete returned `TEXT_STYLES` mapping |
| **ctx** | `undefined` |
| **Marker** | `mmapi_font_run_styles_loaded_filters` |

The wrapper preserves the vanilla mapping when no handlers are registered. Filters are chained in registration order, so each handler must preserve entries owned by other mods.
