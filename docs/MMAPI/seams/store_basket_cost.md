# Seam: store_basket_cost

`store_basket_cost` is a text seam feeding [store.basket_cost](../hooks/store.basket_cost.md). It follows the native `self.basket.slots.sum_with(...)` calculation inside `StoreMenu.update_prices()`.

The filter replaces `self.basket_total` only when its result is numeric, and clamps a numeric result to zero. Every native consumer below the insertion therefore sees the same amount: receipt, affordability colours, Buy unlock, and final deduction. An undefined result, thrown handler, or non-numeric result leaves the native total intact.

| Field | Value |
| ----- | ----- |
| **Game file** | `gml/scripts/UI/Anchor/Menus/StoreMenu.gml` |
| **Function** | `update_prices()` |
| **Hook** | [store.basket_cost](../hooks/store.basket_cost.md) |
| **Marker** | `mmapi_store_run_basket_cost_filters` |
