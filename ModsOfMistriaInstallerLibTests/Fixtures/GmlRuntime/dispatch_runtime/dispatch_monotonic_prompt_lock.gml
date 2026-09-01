// dialogue.prompt_lock uses the monotonic filter dispatcher. A handler may
// request a lock by returning true, but false/undefined can never clear one.

function p_lock_zero(value, ctx) {
    if (ctx.prompt_index == 0) { return true; }
    return undefined;
}

function p_lock_one(value, ctx) {
    if (ctx.prompt_index == 1) { return true; }
    return undefined;
}

function p_unlock(value, ctx) { return false; }
function p_boom(value, ctx) { throw "prompt lock exploded"; }

function p_capture(value, ctx) {
    global.p_seen = ctx;
    return undefined;
}

var p_driver = { kind: "driver" };
var p_line = { kind: "line" };
var p_ctx = {
    driver: p_driver,
    line: p_line,
    conversation_name: "Conversation_Test",
    npc_id: "Npc_Test",
    prompt_index: 1,
    prompt_key: "option_test",
    vanilla_locked: false,
    is_pink: true,
};

// Vanilla state is the input to the aggregator, and no handlers preserve it.
deq("vanilla lock remains locked with no handlers",
    mmapi_apply_monotonic_filters("p.none", true, p_ctx), true);
deq("vanilla unlocked state remains unlocked with no handlers",
    mmapi_apply_monotonic_filters("p.none", false, p_ctx), false);

// One mod can add a lock to an otherwise unlocked option.
mmapi_filter("p.one", p_lock_one, { mod_name: "lock_one_mod" });
deq("one mod can add a prompt lock",
    mmapi_apply_monotonic_filters("p.one", false, p_ctx), true);

// Independent mods can claim different prompt indexes on the same hook.
mmapi_filter("p.multi", p_lock_zero, { mod_name: "lock_zero_mod" });
mmapi_filter("p.multi", p_lock_one, { mod_name: "lock_one_mod" });
p_ctx.prompt_index = 0;
deq("first mod locks its prompt",
    mmapi_apply_monotonic_filters("p.multi", false, p_ctx), true);
p_ctx.prompt_index = 1;
deq("second mod locks its prompt",
    mmapi_apply_monotonic_filters("p.multi", false, p_ctx), true);
p_ctx.prompt_index = 2;
deq("unclaimed prompt remains unlocked",
    mmapi_apply_monotonic_filters("p.multi", false, p_ctx), false);

// Two mods targeting the same prompt compose without duplicate effects.
p_ctx.prompt_index = 0;
mmapi_filter("p.same", p_lock_zero, { mod_name: "same_a" });
mmapi_filter("p.same", p_lock_zero, { mod_name: "same_b" });
deq("same prompt stays locked when two mods request it",
    mmapi_apply_monotonic_filters("p.same", false, p_ctx), true);

// A later handler cannot unlock either another handler's lock or vanilla's.
mmapi_filter("p.no_unlock", p_lock_zero, { mod_name: "lock_mod" });
mmapi_filter("p.no_unlock", p_unlock, { mod_name: "unlock_mod" });
deq("later false cannot clear another mod lock",
    mmapi_apply_monotonic_filters("p.no_unlock", false, p_ctx), true);
deq("later false cannot clear a vanilla lock",
    mmapi_apply_monotonic_filters("p.no_unlock", true, p_ctx), true);

// A throwing handler is isolated and the following handler still gets to add
// its lock. The failure is also charged to its registering mod.
p_ctx.prompt_index = 1;
mmapi_filter("p.failure", p_boom, { mod_name: "bad_prompt_mod" });
mmapi_filter("p.failure", p_lock_one, { mod_name: "good_prompt_mod" });
deq("failed handler does not block the next lock request",
    mmapi_apply_monotonic_filters("p.failure", false, p_ctx), true);
var p_stats = mmapi_hook_stats();
deq("failed prompt handler is attributed to its mod",
    p_stats.errors[$ "bad_prompt_mod"], 1);

// The stable context is the original option identity plus conversation data,
// not resolved display text or a private TextboxMenu lookup.
mmapi_filter("p.context", p_capture, { mod_name: "context_mod" });
mmapi_apply_monotonic_filters("p.context", false, p_ctx);
deq("context carries the driver", global.p_seen.driver.kind, "driver");
deq("context carries the line", global.p_seen.line.kind, "line");
deq("context carries the conversation", global.p_seen.conversation_name, "Conversation_Test");
deq("context carries the npc", global.p_seen.npc_id, "Npc_Test");
deq("context carries the original prompt index", global.p_seen.prompt_index, 1);
deq("context carries the raw prompt key", global.p_seen.prompt_key, "option_test");
deq("context carries vanilla lock state", global.p_seen.vanilla_locked, false);
deq("context carries pink state", global.p_seen.is_pink, true);
