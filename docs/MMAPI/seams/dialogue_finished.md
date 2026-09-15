# Seam: dialogue_finished

`dialogue_finished` is an **event seam** for [dialogue.finished](../hooks/dialogue.finished.md). It is a structural after-anchor in `ConversationDriver.finish_conversation()`, immediately after the driver becomes `Finished`.

The native method has already processed `T2R.conversation_end()` actions and asked the textbox to close before it reaches this point. It may have called `begin_close()` rather than instantly removing the textbox, so the event deliberately promises engine completion rather than animation completion.

| Field | Value |
| ----- | ----- |
| **Game file** | `gml/scripts/GameplaySystems/Dialogue/ConversationDriver.gml` |
| **Function** | `finish_conversation()` |
| **After-anchor** | `self.state = ConversationDriverState.Finished;` |
| **Hook** | [dialogue.finished](../hooks/dialogue.finished.md) |
| **Marker** | `mmapi_dialogue_finished` |
