# Hook: font.styles_loaded

Customize the engine's text-style mapping after vanilla font assets are resolved.

`font.styles_loaded` is a **filter** hook. Register a callback with `mmapi_filter`.

## Contract

Fires when `load_text_styles()` has converted the `ui/text_styles` fiddle data into runtime font assets. The value is the complete styles mapping; `ctx` is `undefined`.

Handlers should modify only the styles and languages they own, preserve unrelated entries, and return the mapping (or `undefined` to keep the current value).

```gml
function my_font_styles(_styles, _ctx) {
    // _styles.standard[$ "bul"] = my_font_asset;
    return _styles;
}

mmapi_filter("font.styles_loaded", my_font_styles);
```

## Engine Wiring

Seam [`font_styles_loaded`](../seams/font_styles_loaded.md) wraps `load_text_styles()` in `gml/scripts/GameplaySystems/FiddleParsers.gml`.
