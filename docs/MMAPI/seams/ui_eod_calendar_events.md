# Seam: ui_eod_calendar_events

Filters the gathered next-day calendar event List before end-of-day notifications are built.

`ui_eod_calendar_events` is a text seam feeding [ui.eod_calendar_events](../hooks/ui.eod_calendar_events.md).

| | |
| --- | --- |
| **File** | `gml/scripts/UI/Anchor/Menus/EodMenu.gml` |
| **Locator** | Between `set_calendar_time(CALENDAR.time)` and `build_notifications(target_date)` in `play_calendar_sequence()` |
| **Feeds** | [ui.eod_calendar_events](../hooks/ui.eod_calendar_events.md) |
| **Marker** | `mmapi_ui_eod_calendar_events_filter` |

The edit dispatches the completed `self.events` List once. It accepts the result only when it still supports `count()`, so an accidental scalar or array return cannot break the notification loop.
