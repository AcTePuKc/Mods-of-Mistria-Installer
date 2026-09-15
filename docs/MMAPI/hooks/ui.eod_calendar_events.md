# Hook: ui.eod_calendar_events

Customize tomorrow's end-of-day calendar events and add custom rows.

`ui.eod_calendar_events` is a **filter** hook. Register it with `mmapi_filter`.

## Contract

It fires once in `EodMenu.play_calendar_sequence()`, after vanilla gathers the events for the next day and before `build_notifications()` turns them into rows below the calendar.

The value is `menu.events`: a `List` of event structs, using `count()`, `get()`, `push()`, and `insert()` rather than an array. `ctx` is `{ menu, target_date }`; `target_date` is the calendar timestamp for the day about to begin. Mutate the List in place, return a replacement List, or return `undefined` to keep it. A non-List return is ignored.

To add a row, push a struct with `{ type, key, icon, npcs }`. `type` must not match a vanilla `CalendarEvent` case; `CalendarEvent.LEN` is the usual choice. `key` is a localization key, or literal text wrapped with `ANCHOR.wrap_for_local()`. `icon` is its left-hand sprite. `npcs` is optional: when supplied, it is a Boolean array indexed by `NpcId` and draws the matching NPC icons beneath the row.

The custom-entry fallback only handles the switch `default`; every vanilla event keeps its normal engine case and fields.

## Usage

```gml
function farm_growth_eod_events(events, ctx) {
    events.push({
        type: CalendarEvent.LEN,
        key: ANCHOR.wrap_for_local("Harvestable crops tomorrow"),
        icon: spr_ui_calendar_icon_event_saturday_market,
        npcs: undefined,
    });
    return events;
}

mmapi_filter("ui.eod_calendar_events", farm_growth_eod_events);
```

## Engine Wiring

- [`ui_eod_calendar_events`](../seams/ui_eod_calendar_events.md) filters the gathered event List.
- [`ui_eod_notification_custom_entry`](../seams/ui_eod_notification_custom_entry.md) renders entries whose type has no vanilla case.
