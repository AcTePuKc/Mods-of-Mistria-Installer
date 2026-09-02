# Seam: pet_reward_generated_forageable

Emits the concrete forageable item produced by a scheduled pet job.

`pet_reward_generated_forageable` is a **text seam** (`anchor` + `emit`). It feeds [pet.reward_generated](../hooks/pet.reward_generated.md). Mod authors never write seams; they register handlers for the hook. See [Seams](../SEAMS.md).

## Placement

| | |
| --- | --- |
| **File** | `gml/scripts/Pet.gml` |
| **Locator** | after `array_push(PET.items_to_pop, NODE_PROTOTYPES[forageable].harvest)` |
| **Op** | event dispatch |
| **Feeds** | [`pet.reward_generated`](../hooks/pet.reward_generated.md) |
| **Context** | `{ pet: PET, job: PET.job, item: NODE_PROTOTYPES[forageable].harvest }` |
| **Marker** | `mmapi_pet_run_forageable_reward_callbacks` |

## Behavior

The event fires only after a valid forageable reward has been appended. It is
observation-only; the reward queue remains engine-owned. With no handlers, the
append is unchanged.
