# Hook: store.basket_cost

`store.basket_cost` is a **filter** hook. Register a callback with `mmapi_filter`. See [Hooks](../HOOKS.md) for registration and dispatch rules.

It fires after `StoreMenu.update_prices()` calculates the native total for every item in the basket, before that one value drives the receipt total, shelf affordability colours, Buy-button state, and the eventual `ARI.modify_gold(-self.basket_total)` deduction.

## Contract

| Part | Value |
| ---- | ----- |
| **Value** | The native whole-basket cost. |
| **Context** | `{ menu, basket, store, price_markup }` |
| **Return** | A numeric, non-negative replacement total; `undefined`, an error, or non-numeric value keeps the native total. |

The replacement is clamped to zero. A zero total is safe, but does **not** make a purchase free: vanilla's Buy button remains disabled unless `basket_total > 0`. The hook deliberately does not alter individual shelf prices, basket contents, stock, or payment settlement.

## Example

```gml
function my_mod_weekend_discount(total, ctx) {
    if total > 0 && weekday() == Weekday.Saturday {
        return floor(total * 0.9);
    }
    return undefined;
}

mmapi_filter("store.basket_cost", my_mod_weekend_discount);
```

## Implementation

- Seam [`store_basket_cost`](../seams/store_basket_cost.md) applies the filter immediately after the native sum.
- [store.item_added](store.item_added.md) is earlier: it observes a shelf tap that adds one item before `update_prices()` recalculates the cost.
- [player.gold_delta](player.gold_delta.md) observes the final gold mutation but cannot make the receipt and affordability UI agree with a different price.
