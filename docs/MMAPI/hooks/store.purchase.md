# Hook: store.purchase

`store.purchase` is an **event** hook. Register a callback with `mmapi_on`.

It fires in the Buy callback after the basket has been drained and purchase bookkeeping has completed, immediately before the engine gives items and charges gold.

```gml
function my_mod_purchase(ctx) {
    // ctx.menu is the StoreMenu; ctx.total is the final gold charge.
    mmapi_log_info("my_mod", "Store charge: " + string(ctx.total));
}
mmapi_on("store.purchase", my_mod_purchase);
```

`ctx` is `{ menu, items, total }`. `items` is the engine's grouped list of pending item stacks. This is observation only: a handler cannot cancel the purchase and must not mutate `items`.
