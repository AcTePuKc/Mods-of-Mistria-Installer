# Seam: dialogue_prompt_lock

Applies the additive `dialogue.prompt_lock` decision while each dialogue prompt box prepares its sprites and input state.

`dialogue_prompt_lock` is a **text seam**, a verbatim `anchor`/`replace` edit. It feeds [dialogue.prompt_lock](../hooks/dialogue.prompt_lock.md). Mod authors never write seams. You register handlers for the hook they dispatch. See [Seams](../SEAMS.md).

## Placement

| | |
| --- | --- |
| **File** | `gml/scripts/UI/Anchor/Menus/TextboxMenu.gml` |
| **Locator** | Text anchor covering the existing prompt `reset_sprites()` pink/lock decision |
| **Feeds** | [`dialogue.prompt_lock`](../hooks/dialogue.prompt_lock.md) |
| **Value filtered** | Boolean effective lock state, seeded from the vanilla spouse/fiancé rule |
| **ctx built** | `{ driver, line, conversation_name, npc_id, prompt_index, prompt_key, vanilla_locked, is_pink }` |
| **Marker** | `mmapi_dialogue_prompt_lock` |

## The Edit

The seam preserves the existing pink-prompt logic and records whether that logic locked the option. It then passes that Boolean through `mmapi_apply_monotonic_filters`, whose `false -> true` contract allows every filter handler to add a lock but prevents any handler from clearing one. A failed handler is isolated by the dispatcher and the current state continues to the next handler.

When the final state is locked, the engine applies the existing grey sprite, `stay_locked` marker, tab target/lock, and soft-lock path. Mods do not manipulate `TextboxMenu`, its blackboard, or its tab. With no handlers, the original vanilla decision and presentation remain unchanged.

The seam runs during prompt preparation, before `join_prompt_slide_in_to_chain()` consumes `stay_locked` and converts it to the prompt's soft lock. It does not change prompt keys or labels, add or hide options, or intercept direct semantic selection.

## See Also

- [dialogue.prompt_lock](../hooks/dialogue.prompt_lock.md) - The public contract.
- [dialogue_prompt_metadata](dialogue_prompt_metadata.md) - Supplies stable original option identity.
