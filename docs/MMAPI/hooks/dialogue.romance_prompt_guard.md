# Hook: dialogue.romance_prompt_guard

Legacy compatibility guard for pink romance prompts.

`dialogue.romance_prompt_guard` is a **guard** hook. Register it with `mmapi_guard`.

## Contract

This exists for mods written for MMAPI 0.16. It fires only when a pink romance prompt is prepared, after the vanilla spouse/fiance rule has run and only when that rule left the prompt selectable.

`ctx` is `{ npc_id, speaker, path }`. `npc_id` is the live textbox speaker's `NpcId`, or `undefined` for a cameo or absent speaker. `speaker` is the live speaker struct, and `path` is the T2 conversation name. Return `false` to apply the normal grey soft-lock. `undefined` or `true` leaves the prompt selectable.

New mods should use [dialogue.prompt_lock](dialogue.prompt_lock.md), which offers stable prompt metadata and monotonic composition with other mods.

## Usage

```gml
function my_mod_legacy_romance_guard(ctx) {
    if (ctx.npc_id == NpcId.Celine && global.my_mod_lock_celine) return false;
    return undefined;
}

mmapi_guard("dialogue.romance_prompt_guard", my_mod_legacy_romance_guard);
```

## Engine Wiring

The [dialogue_prompt_lock](../seams/dialogue_prompt_lock.md) seam runs this bridge beside the modern `dialogue.prompt_lock` filter.
