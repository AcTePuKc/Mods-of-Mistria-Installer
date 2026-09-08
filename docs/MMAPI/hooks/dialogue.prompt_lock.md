# Hook: dialogue.prompt_lock

`dialogue.prompt_lock` is a **filter** hook. Register a callback with `mmapi_filter`. See [Hooks](../HOOKS.md) for how registration and dispatch work.

It fires once while each dialogue prompt option is prepared for display, after the vanilla lock decision is known and before the option becomes interactive. The filtered value is the effective Boolean lock state, seeded from vanilla.

## Contract

| | |
| --- | --- |
| **Fires** | During each prompt box's preparation, before it becomes interactive. |
| **Value** | The current Boolean lock state. It starts as the vanilla state. |
| **ctx** | `{ driver, line, conversation_name, npc_id, prompt_index, prompt_key, vanilla_locked, is_pink }` |
| **Return** | Return Boolean `true` to add a lock. `undefined`, `false`, and other values leave the current state unchanged. |
| **Composition** | Monotonic: a handler cannot clear a vanilla lock or another handler's lock. Handler failures keep the current state and do not stop later handlers. |

`prompt_index` is the original zero-based T2 selection index. `prompt_key` is the raw localization key supplied by the conversation, not resolved display text. `driver`, `line`, `conversation_name`, and `npc_id` are provided when the live conversation context is available; NPC and incomplete/non-NPC conversations may have undefined values. `vanilla_locked` reports the initial engine decision, and `is_pink` reports the prompt's existing pink marker.

## Example

```gml
function my_mod_lock_prompt(value, ctx) {
    if (ctx.npc_id == NpcId.Celine && ctx.prompt_key == "my_mod_option") {
        return true;
    }
    return undefined;
}

mmapi_filter("dialogue.prompt_lock", my_mod_lock_prompt);
```

The engine owns the result: it applies the existing grey sprite, `stay_locked` marker, tab state, and prompt soft-lock. A handler must not call `TextboxMenu` methods or write its blackboard. This hook does not hide, add, rewrite, or semantically veto prompt selections.

## Edge Cases

- Registering no handler preserves the vanilla lock decision and presentation.
- Returning `false` cannot unlock an option already locked by vanilla or an earlier handler.
- Multiple mods may lock different or the same prompt; their requests combine as logical OR.
- The hook runs when the prompt is prepared, not every frame. A condition that changes while the prompt is already visible is applied when the prompt is rebuilt.
- This is a presentation/soft-lock contract. It does not promise to block direct programmatic conversation selection paths.

## See Also

- [dialogue_prompt_lock](../seams/dialogue_prompt_lock.md) - Applies the monotonic lock decision in `TextboxMenu`.
- [dialogue_prompt_metadata](../seams/dialogue_prompt_metadata.md) - Carries the original option index and key to the prompt box.
- [dialogue.line](dialogue.line.md) - Rewrites the dialogue body text, not the prompt-option state.
