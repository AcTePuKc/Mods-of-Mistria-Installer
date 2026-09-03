using System.Security.Cryptography;
using System.Text;
using Garethp.ModsOfMistriaInstallerLib.ModTypes;

namespace Garethp.ModsOfMistriaInstallerLib;

/// <summary>
/// One mod's part in an issue.
///
/// The report used to say all of this in prose, which meant a three-way shortcut clash rendered as
/// three wrapped lines of absolute paths and became unreadable. Keeping the pieces apart lets the
/// window show the name and hide the path behind a tooltip, and lets a button act on one specific
/// mod - reorder it, or rebind its shortcut - rather than on the issue as an undifferentiated blob.
/// </summary>
public sealed class IssueParticipant(string modId, string name, string version, string sourcePath)
{
    public string ModId { get; } = modId;
    public string Name { get; } = name;
    public string Version { get; } = version;
    public string SourcePath { get; } = sourcePath;

    /// <summary>What this mod contributes to the issue, e.g. the file it defines a shortcut in.</summary>
    public string Detail { get; init; } = "";

    public string Display => Version.Length > 0 ? $"{Name} v{Version}" : Name;
}

/// <summary>One thing the planner did, or one thing it wants the user to decide.</summary>
public sealed record LoadOrderNote(LoadOrderNoteKind Kind, string Message)
{
    /// <summary>Exact destination paths involved in this note, when available.</summary>
    public IReadOnlyList<string> Details { get; init; } = [];

    /// <summary>
    /// The mods this issue is about, in the order they currently load - so for a file conflict the
    /// last one is the one that wins today. Empty for notes that are not about a set of mods.
    /// </summary>
    public IReadOnlyList<IssueParticipant> Participants { get; init; } = [];

    /// <summary>The shortcut in dispute, for <see cref="LoadOrderNoteKind.HotkeyConflict"/>.</summary>
    public string? HotkeyKey { get; init; }

    /// <summary>
    /// A language-independent identity for the underlying issue, built from the mod IDs and
    /// versions that produced it. Whoever creates the note supplies this; see
    /// <see cref="StableKey"/> for what happens when they do not.
    /// </summary>
    public string? IssueKey { get; init; }

    /// <summary>
    /// The identity <see cref="DismissedIssueStore"/> files a dismissal under.
    ///
    /// Deliberately version-sensitive: an update to either mod produces a different key, so an
    /// issue the user waved through comes back for a fresh look rather than staying silenced by a
    /// judgement made about different code.
    ///
    /// The fallback hashes the message, which works but is tied to the display language - so
    /// generators set <see cref="IssueKey"/> wherever the underlying identity is available.
    /// </summary>
    public string StableKey =>
        IssueKey is { Length: > 0 } key
            ? $"{Kind}|{key}"
            : $"{Kind}|text:{Fingerprint(Message + "\u001f" + string.Join("\u001f", Details))}";

    private static string Fingerprint(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16].ToLowerInvariant();

    /// <summary>
    /// Builds the mods half of an issue key: IDs paired with versions, ordered so that the same set
    /// of mods always yields the same string.
    /// </summary>
    public static string DescribeMods(IEnumerable<IMod> mods) =>
        string.Join(",", mods
            .Select(mod => $"{mod.GetId()}@{mod.GetVersion()}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(entry => entry, StringComparer.OrdinalIgnoreCase));
}

public enum LoadOrderNoteKind
{
    /// <summary>A mod was moved so that it loads before something that requires it.</summary>
    DependencyMove,

    /// <summary>Two or more mods write the same file. The later one wins; the user may disagree.</summary>
    FileConflict,

    /// <summary>Mods require each other in a loop, so no order can satisfy them all.</summary>
    CircularRequirement,

    /// <summary>A required mod is not in the list at all.</summary>
    MissingRequirement,

    /// <summary>Two selected GML mods contend for an exclusive hook.</summary>
    HookConflict,

    /// <summary>Two selected GML mods appear to use the same keyboard shortcut.</summary>
    HotkeyConflict,

    /// <summary>A selected mod has a known or generic compatibility warning.</summary>
    CompatibilityWarning
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
    /// <param name="rankConflictsBySuggestedOrder">
    /// Which order decides who currently wins a shared file.
    ///
    /// True suits "Suggest order", where the user is about to accept the reordering and wants to
    /// know what it implies. False suits the conflict report, which does not apply the suggestion:
    /// naming a winner from an order the user has not agreed to would label the wrong mod, and the
    /// report's "make this one win" button would then have nothing to do.
    /// </param>
    public static LoadOrderPlan Plan(
        IReadOnlyList<IMod> mods,
        IReadOnlyList<IMod>? conflictScope = null,
        bool rankConflictsBySuggestedOrder = true)
    {
        var notes = new List<LoadOrderNote>();
        var order = SortByRequirements(mods, notes);

        notes.AddRange(DescribeFileConflicts(
            conflictScope ?? mods,
            rankConflictsBySuggestedOrder ? order : mods.ToList()));

        // IDs identify a mod package, not necessarily one row in the UI. A folder and a ZIP
        // copy may legitimately expose the same ID, so compare the actual instances here.
        var changed = !order.SequenceEqual(mods);
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
                        $"\"{mod.GetName()}\" requires \"{requirement.Name}\" by {requirement.Author}, which is not installed.")
                    {
                        IssueKey = $"{mod.GetId()}@{mod.GetVersion()}->{requiredId}"
                    });
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
                    .Select(name => $"\"{name}\"")))
            {
                IssueKey = LoadOrderNote.DescribeMods(cyclic.Select(id => byId[id]))
            });
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
        // Duplicate package copies share an ID. They cannot be named unambiguously in a
        // dependency-move note, so only use IDs that occur exactly once in each list.
        var beforeIndex = before
            .Select((mod, index) => (mod.GetId(), index))
            .GroupBy(pair => pair.Item1, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single().index, StringComparer.OrdinalIgnoreCase);

        var afterIndex = after
            .Select((mod, index) => (mod.GetId(), index))
            .GroupBy(pair => pair.Item1, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single().index, StringComparer.OrdinalIgnoreCase);

        var names = after
            .GroupBy(mod => mod.GetId(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single().GetName(), StringComparer.OrdinalIgnoreCase);

        foreach (var mod in after)
        {
            if (!beforeIndex.ContainsKey(mod.GetId()) || !afterIndex.ContainsKey(mod.GetId())) continue;

            foreach (var requirement in mod.GetRequirements())
            {
                var requiredId = requirement.GetId();
                if (!beforeIndex.ContainsKey(requiredId) || !afterIndex.ContainsKey(requiredId)) continue;

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
        // A folder and an archive copy can have the same manifest ID. Pick one copy per ID - the
        // last, because that is the one that would win - and take the position, name, version and
        // path from that same copy. Mixing them would let the report rank one copy and then point
        // its "make this one win" button at the other.
        var chosen = order
            .Select((mod, index) => (Mod: mod, Index: index))
            .GroupBy(pair => pair.Mod.GetId(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

        var position = chosen.ToDictionary(
            entry => entry.Key, entry => entry.Value.Index, StringComparer.OrdinalIgnoreCase);
        var names = chosen.ToDictionary(
            entry => entry.Key, entry => entry.Value.Mod.GetName(), StringComparer.OrdinalIgnoreCase);

        // Versions go into the issue key so that dismissing "these two both touch the same sprite"
        // does not also silence the next version of either mod.
        var versions = chosen.ToDictionary(
            entry => entry.Key, entry => entry.Value.Mod.GetVersion(), StringComparer.OrdinalIgnoreCase);
        var sources = chosen.ToDictionary(
            entry => entry.Key, entry => entry.Value.Mod.GetSourcePath(), StringComparer.OrdinalIgnoreCase);

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
            .Select(conflict => new
            {
                Conflict = conflict,
                ModIds = conflict.ModIds.Where(position.ContainsKey).ToList()
            })
            .Where(item => item.ModIds.Count > 1)
            // One note per set of mods, however many files they happen to share.
            .GroupBy(item => string.Join("|", item.ModIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase)),
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var ids = group.First().ModIds.OrderBy(id => position[id]).ToList();
                var winner = names[ids[^1]];
                var others = string.Join(", ", ids[..^1].Select(id => $"\"{names[id]}\""));
                var files = group.Count();

                return new LoadOrderNote(LoadOrderNoteKind.FileConflict,
                    $"\"{winner}\" overrides {others} ({files} shared file{(files == 1 ? "" : "s")}). " +
                    "Drag whichever should win to the bottom of that pair.")
                {
                    Details = group
                        .Select(item => item.Conflict.Path)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Order(StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    IssueKey = string.Join(",", ids
                        .Select(id => $"{id}@{versions[id]}")
                        .OrderBy(entry => entry, StringComparer.OrdinalIgnoreCase)),
                    // In load order, so the last entry is the one that wins as things stand. The
                    // report relies on that to label the current winner without recomputing.
                    Participants = ids
                        .Select(id => new IssueParticipant(id, names[id], versions[id], sources[id]))
                        .ToList()
                };
            })
            .ToList();
    }
}
