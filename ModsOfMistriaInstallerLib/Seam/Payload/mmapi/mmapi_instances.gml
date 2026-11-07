// Runtime provider for instance.created. A handler names its object in
// opts.object; each active instance is delivered once per registration after
// its complete create chain, during Game begin_step.
function mmapi_instances_poll() {
    var registry = global[$ "__mmapi_hooks"];
    if (registry == undefined) { return; }
    var handlers = registry[$ "instance.created"];
    if (handlers == undefined) { return; }

    var count = array_length(handlers);
    for (var i = 0; i < count; i++) {
        var record = handlers[i];
        if (record.kind != "event") { continue; }
        var watched = record[$ "object"];
        if (watched == undefined) { continue; }
        if (record[$ "refused"] == undefined) {
            record.refused = (watched == obj_node_renderer || watched == par_interactable);
            if (record.refused) {
                mmapi_warn_rate_limited(
                    "hook_hot_root:" + string(record.mod_name), record.mod_name,
                    "mmapi hook instance.created: " + string(record.mod_name)
                    + " watches obj_node_renderer or par_interactable, which is refused.");
            }
        }
        if (record.refused) { continue; }

        var marker = record.marker;
        var mod_name = record.mod_name;
        with (watched) {
            if (self[$ marker] != undefined) { continue; }
            self[$ marker] = true;
            try {
                record.fn(self);
            } catch (err) {
                __mmapi_hook_handler_failed("instance.created", mod_name, err);
            }
        }
    }
}

__mmapi_register_as(mmapi_instances_poll, "mmapi");
