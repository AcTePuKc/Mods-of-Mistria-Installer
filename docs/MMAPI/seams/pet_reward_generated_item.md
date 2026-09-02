# Seam: pet_reward_generated_item

Emits each fixed item produced by a scheduled pet job.

`pet_reward_generated_item` is a **text seam** (`anchor` + `emit`). It feeds [pet.reward_generated](../hooks/pet.reward_generated.md). Mod authors never write seams; they register handlers for the hook. See [Seams](../SEAMS.md).

## Placement

| | |
| --- | --- |
| **File** | `gml/scripts/Pet.gml` |
| **Locator** | after `array_push(PET.items_to_pop, job_data.reward)` |
| **Op** | event dispatch |
| **Feeds** | [`pet.reward_generated`](../hooks/pet.reward_generated.md) |
| **Context** | `{ pet: PET, job: PET.job, item: job_data.reward }` |
| **Marker** | `mmapi_pet_run_item_reward_callbacks` |

## Behavior

The event fires once per fixed reward item after it is appended. A multi-item
reward therefore produces one event per item. It is observation-only; the
reward queue remains engine-owned. With no handlers, the append is unchanged.
