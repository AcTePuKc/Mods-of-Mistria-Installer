# Seam: ui_eod_notification_custom_entry

Renders a custom end-of-day calendar event row when its type has no vanilla case.

`ui_eod_notification_custom_entry` is a text seam feeding [ui.eod_calendar_events](../hooks/ui.eod_calendar_events.md).

| | |
| --- | --- |
| **File** | `gml/scripts/UI/Anchor/Menus/EodMenu.gml` |
| **Locator** | The `default` branch of `build_notifications()`' event-type switch |
| **Feeds** | [ui.eod_calendar_events](../hooks/ui.eod_calendar_events.md) |
| **Marker** | `mmapi_ui_eod_notification_custom_entry` |

Vanilla event types still use their original cases. Only an unknown type reads `icon`, `key`, and optional `npcs` from the custom event struct, then continues through the engine's existing row layout and fade sequence.
