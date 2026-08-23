using Garethp.ModsOfMistriaInstallerLib.ModTypes;

namespace Garethp.ModsOfMistriaInstallerLib;

/// <summary>One thing the planner did, or one thing it wants the user to decide.</summary>
public sealed record LoadOrderNote(LoadOrderNoteKind Kind, string Message);

public enum LoadOrderNoteKind
{
    /// <summary>A mod was moved so that it loads before something that requires it.</summary>
    DependencyMove,

    /// <summary>Two or more mods write the same file. The later one wins; the user may disagree.</summary>
    FileConflict,

    /// <summary>Mods require each other in a loop, so no order can satisfy them all.</summary>
    CircularRequirement,

    /// <summary>A required mod is not in the list at all.</summary>
    MissingRequirement
}

public sealed record LoadOrderPlan(List<IMod> Order, List<LoadOrderNote> Notes)
{
    public bool ChangesAnything { get; init; }
}

/// <summary>
/// Suggests a load order.
///
/// Two rules, and only two, because they are the two that have a correct answer:
/// a mod loads after everything it declares a requirement on, and everything else keeps the order
/// the user already chose. The planner does not reorder mods that merely touch the same files -
/// when two cosmetic mods both replace a sprite, which one should win is a preference, not a fact.
/// Those pairs come back as notes instead, so the user can drag the winner below the loser.
/// </summary>
public static class LoadOrderPlanner
{
    /// <param name="mods">The mods in their current order.</param>
    /// <param name="conflictScope">
    /// The mods to check for file conflicts - normally the enabled ones, since a disabled mod
    /// cannot collide with anything. Defaults to <paramref name="mods"/>.
    /// </param>
    public static LoadOrderPlan Plan(IReadOnlyList<IMod> mods, IReadOnlyList<IMod>? conflictScope = null)
    {
        var notes = new List<LoadOrderNote>();
        var order = SortByRequirements(mods, notes);

        notes.AddRange(DescribeFileConflicts(conflictScope ?? mods, order));

        var changed = !order.Select(mod => mod.GetId()).SequenceEqual(mods.Select(mod => mod.GetId()));
        return new LoadOrderPlan(order, notes) { ChangesAnything = changed };
    }

    // ── Dependency ordering ──────────────────────────────────────────────────────

    /// <summary>
    /// Fixes the order with the smallest edits that satisfy the requirements: walk the list, and
    /// whenever a mod sits above something it requires, lift that requirement to just above it.
    /// Every other pair keeps the relative position the user gave it.
    ///
    /// A topological sort would also produce a valid order, but it re-seats mods that have nothing
    /// to do with the dependency - a mod the user deliberately dragged below another can come out
    /// above it - and an order the user cannot recognise is not a suggestion they will trust.
    /// </summary>
    private static List<IMod> SortByRequirements(IReadOnlyList<IMod> mods, List<LoadOrderNote> notes)
    {
        var byId = new Dictionary<string, IMod>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in mods) byId.TryAdd(mod.GetId(), mod);

        // requirements[x] = the mods x must load after, restricted to mods that are actually here
        var requirements = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var mod in mods)
        {
            var id = mod.GetId();
            var required = new List<string>();

            foreach (var requirement in mod.GetRequirements())
            {
                var requiredId = requirement.GetId();

                if (!byId.ContainsKey(requiredId))
                {
                    notes.Add(new LoadOrderNote(LoadOrderNoteKind.MissingRequirement,
                        $"\"{mod.GetName()}\" requires \"{requirement.Name}\" by {requirement.Author}, which is not installed."));
                    continue;
                }

                if (requiredId.Equals(id, StringComparison.OrdinalIgnoreCase)) continue;
                if (!required.Contains(requiredId, StringComparer.OrdinalIgnoreCase)) required.Add(requiredId);
            }

            requirements[id] = required;
        }

        var cyclic = FindCyclicMods(requirements);
        if (cyclic.Count > 0)
        {
            notes.Add(new LoadOrderNote(LoadOrderNoteKind.CircularRequirement,
                "These mods require each other in a loop, so their order was left alone: " +
                string.Join(", ", cyclic
                    .Select(id => byId[id].GetName())
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .Select(name => $"\"{name}\"")));
        }

        var order = mods.ToList();

        // Each pass can only move a mod upwards, and a mod that has reached the top cannot move
        // again, so the work is bounded. The cap is belt and braces against a cycle the detector
        // somehow missed.
        var movesLeft = order.Count * order.Count + 1;
        var settled = false;

        while (!settled && movesLeft-- > 0)
        {
            settled = true;

            for (var index = 0; index < order.Count && settled; index++)
            {
                var id = order[index].GetId();
                if (cyclic.Contains(id)) continue;

                foreach (var requiredId in requirements[id])
                {
                    if (cyclic.Contains(requiredId)) continue;

                    var requiredIndex = order.FindIndex(mod => mod.GetId().Equals(requiredId, StringComparison.OrdinalIgnoreCase));
                    if (requiredIndex <= index) continue;

                    var required = order[requiredIndex];
                    order.RemoveAt(requiredIndex);
                    order.Insert(index, required);

                    settled = false;
                    break;
                }
            }
        }

        AddDependencyMoveNotes(mods, order, notes);
        return order;
    }

    /// <summary>
    /// Every mod that sits on a requirement cycle. Those are left exactly where they are: no order
    /// can satisfy them, and shuffling them would only make the list harder to read.
    /// </summary>
    private static HashSet<string> FindCyclicMods(Dictionary<string, List<string>> requirements)
    {
        var cyclic = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);  // 0 unvisited, 1 on stack, 2 done
        var stack = new List<string>();

        foreach (var id in requirements.Keys) Visit(id);

        return cyclic;

        void Visit(string id)
        {
            if (state.TryGetValue(id, out var seen) && seen != 0)
            {
                if (seen != 1) return;

                // Everything from where this id sits on the stack down to the top is in the loop.
                var from = stack.LastIndexOf(id);
                for (var i = from; i < stack.Count; i++) cyclic.Add(stack[i]);
                return;
            }

            state[id] = 1;
            stack.Add(id);

            foreach (var requiredId in requirements[id]) Visit(requiredId);

            stack.RemoveAt(stack.Count - 1);
            state[id] = 2;
        }
    }

    private static void AddDependencyMoveNotes(IReadOnlyList<IMod> before, List<IMod> after, List<LoadOrderNote> notes)
    {
        var beforeIndex = before
            .Select((mod, index) => (id: mod.GetId(), index))
            .ToDictionary(pair => pair.id, pair => pair.index, StringComparer.OrdinalIgnoreCase);

        var afterIndex = after
            .Select((mod, index) => (id: mod.GetId(), index))
            .ToDictionary(pair => pair.id, pair => pair.index, StringComparer.OrdinalIgnoreCase);

        var names = after.ToDictionary(mod => mod.GetId(), mod => mod.GetName(), StringComparer.OrdinalIgnoreCase);

        foreach (var mod in after)
        {
            foreach (var requirement in mod.GetRequirements())
            {
                var requiredId = requirement.GetId();
                if (!afterIndex.ContainsKey(requiredId)) continue;

                // Only worth reporting when the order was wrong before and is right now. A mod
                // caught in a requirement cycle stays where it was, and claiming it moved would be
                // a lie the user could check.
                if (beforeIndex[requiredId] < beforeIndex[mod.GetId()]) continue;
                if (afterIndex[requiredId] > afterIndex[mod.GetId()]) continue;

                notes.Add(new LoadOrderNote(LoadOrderNoteKind.DependencyMove,
                    $"\"{names[requiredId]}\" now loads before \"{mod.GetName()}\", which requires it."));
            }
        }
    }

    // ── File conflicts ───────────────────────────────────────────────────────────

    /// <summary>
    /// Reports pairs of mods that write the same destination file, naming the one that wins under
    /// the suggested order. Mergeable metadata and shared localisation are left out: those are
    /// combined rather than overwritten, so their order does not decide a winner.
    /// </summary>
    private static List<LoadOrderNote> DescribeFileConflicts(IReadOnlyList<IMod> scope, List<IMod> order)
    {
        var position = order
            .Select((mod, index) => (id: mod.GetId(), index))
            .ToDictionary(pair => pair.id, pair => pair.index, StringComparer.OrdinalIgnoreCase);

        var names = order.ToDictionary(mod => mod.GetId(), mod => mod.GetName(), StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<ModFileConflict> conflicts;
        try
        {
            conflicts = ModFileConflictDetector.Find(scope);
        }
        catch (Exception exception)
        {
            Logger.Log($"Load order conflict check skipped: {exception.Message}");
            return [];
        }

        return conflicts
            .Where(conflict => conflict.Kind is ModFileConflictKind.HardReplacement or ModFileConflictKind.SharedDestination)
            .Select(conflict => conflict.ModIds.Where(position.ContainsKey).ToList())
            .Where(ids => ids.Count > 1)
            // One note per set of mods, however many files they happen to share.
            .GroupBy(ids => string.Join("|", ids.OrderBy(id => id, StringComparer.OrdinalIgnoreCase)),
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var ids = group.First().OrderBy(id => position[id]).ToList();
                var winner = names[ids[^1]];
                var others = string.Join(", ", ids[..^1].Select(id => $"\"{names[id]}\""));
                var files = group.Count();

                return new LoadOrderNote(LoadOrderNoteKind.FileConflict,
                    $"\"{winner}\" overrides {others} ({files} shared file{(files == 1 ? "" : "s")}). " +
                    "Drag whichever should win to the bottom of that pair.");
            })
            .ToList();
    }
}
