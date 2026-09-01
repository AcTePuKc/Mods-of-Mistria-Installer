# Seam: dialogue_prompt_metadata

Threads the original prompt index and localization key into each `TextboxMenu` prompt box for [dialogue.prompt_lock](../hooks/dialogue.prompt_lock.md).

`dialogue_prompt_metadata` is a **text seam** and a **companion edit**: it dispatches nothing itself. It exists so the sibling [dialogue_prompt_lock](dialogue_prompt_lock.md) seam can expose stable option identity without requiring mods to inspect private menu state.

## Placement

| | |
| --- | --- |
| **File** | `gml/scripts/UI/Anchor/Menus/TextboxMenu.gml` |
| **Locator** | Text anchor in the `TextboxState.Ask` translation loop |
| **Feeds** | [`dialogue.prompt_lock`](../hooks/dialogue.prompt_lock.md), through [`dialogue_prompt_lock`](dialogue_prompt_lock.md) |
| **Value** | None; stores the original option index and raw localization key on the prompt box |
| **Marker** | `mmapi_dialogue_prompt_metadata` |

## The Edit

As the engine assigns each option key to its prompt box, the companion edit stores the loop's original `i` and `key` on that box. The later lock seam reads those values while the same prompt is prepared. The original index is preserved even when the prompt loop lays options out in reverse order, so it remains the index used by conversation selection.

The edit does not alter the option array, translated text, ordering, visibility, or selection behavior.

## See Also

- [dialogue.prompt_lock](../hooks/dialogue.prompt_lock.md) - The hook receiving the metadata.
- [dialogue_prompt_lock](dialogue_prompt_lock.md) - The dispatching sibling seam.
