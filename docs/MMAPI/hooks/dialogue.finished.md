# Hook: dialogue.finished

`dialogue.finished` is an **event** hook. Register a callback with `mmapi_on`. See [Hooks](../HOOKS.md) for registration and dispatch rules.

It fires once from `ConversationDriver.finish_conversation()` after the engine has run every T2 conversation-end action, requested that any textbox close, and set the driver's state to `ConversationDriverState.Finished`.

## Context

```gml
{
    driver,             // the finished ConversationDriver
    conversation_name,  // the T2 path that completed
    npc_id,             // the driver's NPC owner; may be undefined for non-NPC dialogue
}
```

The event means **engine conversation completion**, not that a visible textbox close animation has already completed. Use [ui.menu_closed](ui.menu_closed.md) if a mod specifically needs a menu close event; it is intentionally generic and not proof that a particular dialogue caused it.

## Example

```gml
function my_mod_dialogue_finished(ctx) {
    if ctx.conversation_name == "my_mod/intro" {
        GLOBAL.my_mod_intro_complete = true;
    }
}

mmapi_on("dialogue.finished", my_mod_dialogue_finished);
```

`dialogue.finish` is an alias for compatibility with the upstream request. New mods should require and register `dialogue.finished`.

## Implementation

- Seam [`dialogue_finished`](../seams/dialogue_finished.md) emits after `self.state = ConversationDriverState.Finished` in `finish_conversation()`.
- [dialogue.line](dialogue.line.md) runs earlier, once for each delivered line.
- [dialogue.path](dialogue.path.md) runs before the driver begins.
