using Garethp.ModsOfMistriaInstallerLib.ModTypes;

namespace Garethp.ModsOfMistriaInstallerLib;

/// <summary>
/// What a mod does to the game, as far as load order is concerned.
///
/// The order of these values is the order the layers load in, and every one of them is chosen from
/// how AIM's own installers behave rather than from taste. See <see cref="ModRoleClassifier"/>.
/// </summary>
public enum ModRole
{
    /// <summary>
    /// Something another installed mod declares a requirement on.
    ///
    /// First, because its dependents need it - which the requirement pass enforces anyway - and
    /// because a framework usually ships code, and code is first-wins.
    /// </summary>
    Framework,

    /// <summary>
    /// Ships GML: it changes what the game does, not what it contains.
    ///
    /// Early, and this is the one layer where early is strictly safer rather than a preference.
    /// Two GML mods that export the same function name, or share an install namespace, are resolved
    /// first-wins - and the loser is not merely overruled, it is dropped from the install entirely,
    /// taking its sprites and data with it. Being early costs a GML mod nothing: its code installs
    /// under its own namespace, and its runtime hook order comes from the hooks' own priorities
    /// rather than from load order.
    /// </summary>
    Behaviour,

    /// <summary>
    /// Adds things the game did not have: new sprites, items, furniture, cosmetics, maps, points.
    ///
    /// The neutral middle. These contributions are merged or appended rather than overwritten, so
    /// their order rarely decides anything - which is exactly why they belong between the mods that
    /// need to be early and the mods that need to be late.
    /// </summary>
    Content,

    /// <summary>
    /// Writes into the game's existing data tables without adding new content: prices, rates, the
    /// contents of a shop.
    ///
    /// Late, because AIM merges these tables key by key and a plain value is last-wins. A mod whose
    /// whole purpose is to change a number the base game or another mod already set has to be read
    /// after the thing it is changing, or it changes nothing.
    /// </summary>
    DataOverride,

    /// <summary>
    /// Replaces files wholesale: sprites under <c>images/replace/</c>, fonts.
    ///
    /// Last, and for the bluntest reason: these are unconditional writes. A replacement removes the
    /// previous one's frames from the atlas and puts its own in. Whoever is read last wins outright,
    /// so a recolour placed above the mod it is recolouring simply does not appear.
    /// </summary>
    Replacement
}

/// <summary>
/// Works out what each mod is for, from what it actually installs.
///
/// Nothing here reads a mod page, a category, or a name. Every test is a question about the mod's
/// own folders, and each one corresponds to a specific installer whose behaviour decides whether
/// load order matters:
///
///   • <c>gml/</c> is picked up by the GML collector, which resolves name collisions first-wins.
///   • <c>images/replace/</c> is the image installer's hard-replacement path, which is last-wins.
///   • <c>momi/</c>, <c>tiled/</c>, <c>points/</c>, <c>animations/</c> and <c>shapes/</c> are where
///     new content lives; the installers merge or append these.
///   • <c>fiddle/</c>, <c>data_files/</c> and <c>localization/</c> are merged table by table, where
///     a scalar is last-wins.
///
/// A mod that does several of these is classified by the constraint that matters most, not by the
/// largest folder: code first, because losing a name collision costs a GML mod everything, and
/// otherwise by the latest layer its contributions need, because a mod that both adds content and
/// replaces a sprite has to be late enough for the replacement to land.
///
/// These are directory checks rather than file walks, so classifying two hundred mods is cheap -
/// but it still touches the disk, so it belongs on a background thread with the rest of planning.
/// </summary>
public static class ModRoleClassifier
{
    /// <summary>
    /// Classifies every mod, using the whole list to spot the frameworks.
    ///
    /// "Framework" is the one role that cannot be read off a single mod: it means other mods here
    /// depend on it, which is a fact about the list.
    /// </summary>
    public static Dictionary<string, ModRole> Classify(IReadOnlyList<IMod> mods)
    {
        var depended = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var mod in mods)
            foreach (var requirement in Requirements(mod))
                depended.Add(requirement.GetId());

        var roles = new Dictionary<string, ModRole>(StringComparer.OrdinalIgnoreCase);

        foreach (var mod in mods)
        {
            var id = mod.GetId();
            if (roles.ContainsKey(id)) continue;

            roles[id] = RoleOf(mod, depended.Contains(id));
        }

        return roles;
    }

    /// <param name="isDependedOn">True when another mod in the list requires this one.</param>
    public static ModRole RoleOf(IMod mod, bool isDependedOn)
    {
        // Asked first, and answered before anything is read from disk. A mod others build on has to
        // load before them whatever else it contains, so there is nothing a folder could say that
        // would change the answer.
        if (isDependedOn) return ModRole.Framework;

        // Code next, for the reason in ModRole.Behaviour: a GML mod that loses a first-wins name
        // collision is excluded outright, so its position is a safety question rather than a
        // preference, and it outranks whatever else the mod happens to ship.
        if (Has(mod, () => mod.FolderExists("gml"))) return ModRole.Behaviour;

        // Everything below is "how late does this mod need to be", answered by taking the latest
        // layer any of its contributions calls for.
        if (Has(mod, () => mod.HasFilesInFolder("images/replace", ".png")) ||
            Has(mod, () => mod.HasFilesInFolder("fonts", ".ttf")))
            return ModRole.Replacement;

        var addsContent =
            Has(mod, () => mod.FolderExists("momi")) ||
            Has(mod, () => mod.FolderExists("tiled")) ||
            Has(mod, () => mod.FolderExists("points")) ||
            Has(mod, () => mod.FolderExists("animations")) ||
            Has(mod, () => mod.FolderExists("shapes"));

        if (addsContent) return ModRole.Content;

        if (Has(mod, () => mod.FolderExists("fiddle")) ||
            Has(mod, () => mod.FolderExists("data_files")) ||
            Has(mod, () => mod.FolderExists("localization")))
            return ModRole.DataOverride;

        // Nothing recognised. The middle is where a mod AIM cannot classify does least harm: it is
        // the layer whose contributions are merged rather than fought over, so a wrong guess here
        // changes nothing rather than silently overruling somebody.
        return ModRole.Content;
    }

    /// <summary>
    /// A one-line reason, for the note that tells the user why a mod moved. Same wording as the
    /// enum's documentation, because the user is owed the actual reason and not a category name.
    /// </summary>
    public static string Explain(ModRole role) => role switch
    {
        ModRole.Framework => "other mods here require it, so it has to load before them",
        ModRole.Behaviour => "it ships code, and two code mods that collide are resolved in favour " +
                             "of whichever loads first - the loser is dropped from the install entirely",
        ModRole.DataOverride => "it changes values in the game's existing data tables, and a changed " +
                                "value only sticks if it is read after the one it replaces",
        ModRole.Replacement => "it replaces files outright, and the last mod to replace a file wins",
        _ => "it adds new content, which is merged rather than overwritten, so its position is not critical"
    };

    private static IEnumerable<ModRequirement> Requirements(IMod mod)
    {
        try
        {
            return mod.GetRequirements();
        }
        catch (Exception exception)
        {
            // A malformed manifest costs this mod its dependency edges, not the whole plan.
            Logger.Log($"Could not read {mod.GetName()}'s requirements: {exception.Message}");
            return [];
        }
    }

    /// <summary>
    /// Runs one folder probe, treating a failure as "no".
    ///
    /// A mod can be removed, renamed or locked while AIM is looking at it, and an archive-backed mod
    /// can refuse a path outright. None of that is worth failing a plan over: the mod simply falls
    /// through to the neutral middle, which is where an unclassified mod belongs anyway.
    /// </summary>
    private static bool Has(IMod mod, Func<bool> probe)
    {
        try
        {
            return probe();
        }
        catch (Exception exception)
        {
            Logger.Log($"Could not inspect {mod.GetName()}: {exception.Message}");
            return false;
        }
    }
}
