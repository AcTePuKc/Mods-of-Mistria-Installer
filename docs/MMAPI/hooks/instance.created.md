# Hook: instance.created

`instance.created` is an **event** hook. Register with `mmapi_on` and name the watched object in `opts.object`.

It fires once for every active instance of that object, per handler, from the framework's `Game` begin-step poll. The instance's complete create chain has already run, including vanilla interaction registration. `ctx` is the instance itself.

```gml
function my_mod_wheedle_created(wheedle) {
    wheedle.register_interaction(InputId.SecondaryInteract, "mods/my_mod/action", my_mod_action);
}
mmapi_on("instance.created", my_mod_wheedle_created, { object: obj_wheedle_stall });
```

The event is asynchronous with creation and can fire when an inactive instance next becomes active. A missing `opts.object`, `obj_node_renderer`, and `par_interactable` are refused to prevent an unbounded per-frame scan. Prefer `npc.created`, `pet.created`, or `animal.created` when one of those precise spawn hooks fits.
