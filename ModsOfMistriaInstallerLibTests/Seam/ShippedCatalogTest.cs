using System.Text;
using System.Text.RegularExpressions;
using Garethp.ModsOfMistriaInstallerLib.Seam;
using ModsOfMistriaInstallerLibTests.TestUtils;

namespace ModsOfMistriaInstallerLibTests.Seam;

// The real seam catalog, proven against a pristine stand-in synthesised from
// its own anchors. This is what keeps a hand-edited catalog honest without a
// game checkout: anchors that stop matching, marker collisions, ordering
// violations and lint failures all surface here.
[TestFixture]
public class ShippedCatalogTest
{
    private static readonly string PayloadDir = Path.Combine(AppContext.BaseDirectory, "Payload");

    private static readonly string[] MmapiPrefixes = ["mmapi_", "__mmapi_"];

    private SeamCatalog _catalog = null!;
    private Dictionary<string, string> _pristine = null!;

    [OneTimeSetUp]
    public void LoadShippedCatalog()
    {
        var (name, bytes) = PayloadResolver.SeamCatalog();
        _catalog = SeamCatalogLoader.Load(bytes, name);
        _pristine = PristineSynthesis.FromCatalog(_catalog);
    }

    [Test]
    public void ShouldStageAgainstItsOwnAnchors()
    {
        var pristine = new MemoryPristineSource(
            _pristine.ToDictionary(f => f.Key, f => Encoding.UTF8.GetBytes(f.Value)));

        var staged = SeamStager.Simulate(_catalog, pristine);

        Assert.That(staged.Keys.Order(StringComparer.Ordinal), Is.EqualTo(_catalog.Files));
        var applied = staged.Values
            .SelectMany(f => f.EntryIds)
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.That(applied, Is.EqualTo(_catalog.Entries
            .Select(e => e.Id)
            .Order(StringComparer.Ordinal)
            .ToList()));
    }

    [Test]
    public void ShouldDeclareAndCountEveryHook()
    {
        // The shipped catalog must self-declare its integrity counts; the loader
        // enforces declared == parsed (and orphan hooks, in both directions), so
        // loading at all proves consistency. The echo here documents the contract.
        Assert.That(_catalog.DeclaredCounts, Is.Not.Null);
        Assert.That(_catalog.DeclaredCounts!.Hooks, Is.EqualTo(_catalog.HookDeclarations.Count));
        Assert.That(_catalog.DeclaredCounts.Seams, Is.EqualTo(_catalog.Seams.Count));
        Assert.That(_catalog.DeclaredCounts.EngineFixes, Is.EqualTo(_catalog.EngineFixes.Count));
        Assert.That(_catalog.DeclaredCounts.CallRewrites, Is.EqualTo(_catalog.CallRewrites.Count));

        var runtime = _catalog.HookDeclarations
            .Where(d => d.Provider == HookProvider.Runtime)
            .Select(d => d.Name)
            .ToList();
        Assert.That(runtime, Does.Contain("game.room_changed"));
        Assert.That(runtime, Does.Contain("game.day_changed"));

        // The rename kept the old name resolving: game.day_changed carries the
        // catalog's first alias.
        var dayChanged = _catalog.HookDeclarations.Single(d => d.Name == "game.day_changed");
        Assert.That(dayChanged.Aliases, Does.Contain("game.day_started"));
    }

    [Test]
    public void ShouldReplaceTheObsoleteSideRoomChanceLocatorWithTheRangeContract()
    {
        // This is a breaking 0.16.x migration. The old symbols named a
        // chance decision in a previous engine implementation; the current
        // function chooses a floor from start_floor + range instead.
        Assert.That(_catalog.DeclaredCounts!.Hooks, Is.EqualTo(136));
        Assert.That(_catalog.DeclaredCounts.Seams, Is.EqualTo(150));
        Assert.That(_catalog.Hook("dungeon.side_room_chance"), Is.Null);
        Assert.That(_catalog.Seams.Any(s => s.Id == "dungeon_side_room_chance"), Is.False);

        var hook = _catalog.Hook("dungeon.side_room_range");
        Assert.That(hook, Is.Not.Null);
        Assert.That(hook!.Kind, Is.EqualTo(HookKind.Filter));
        Assert.That(hook.Doc, Does.Contain("floor range"));
        Assert.That(hook.Doc, Does.Contain("start_floor"));

        var seam = _catalog.Seams.Single(s => s.Id == "dungeon_side_room_range");
        Assert.That(seam.File, Is.EqualTo("assets/gml/scripts/GameplaySystems/Dungeon/DungeonRunner.gml"));
        Assert.That(seam.Hooks, Is.EqualTo(new[] { "dungeon.side_room_range" }));
        Assert.That(seam.Op, Is.EqualTo(DispatchOp.Filter));
        Assert.That(seam.TargetFn, Is.EqualTo("try_create_side_room"));
        Assert.That(seam.TargetAt, Is.EqualTo("head"));
        Assert.That(seam.Marker, Is.EqualTo("mmapi_dungeon_run_side_room_range_filters"));
        Assert.That(seam.Replace, Does.Contain(
            "mmapi_apply_filters(\"dungeon.side_room_range\", range, { impl: impl, is_ritual: impl == DungeonImpl.Ritual, start_floor: start_floor })"));
        Assert.That(seam.Replace, Does.Not.Contain("chance_val"));
        Assert.That(seam.Replace, Does.Not.Contain("max_flr"));
    }

    [Test]
    public void ShouldDeclareTheFishSelectionEventAtTheFishingHubBoundary()
    {
        var hook = _catalog.Hook("fishing.fish_selected");
        Assert.That(hook, Is.Not.Null);
        Assert.That(hook!.Kind, Is.EqualTo(HookKind.Event));
        Assert.That(hook.Doc, Does.Contain("selected fish"));

        var seam = _catalog.Seams.Single(s => s.Id == "fishing_fish_selected");
        Assert.That(seam.File, Is.EqualTo("assets/gml/scripts/Player/FishingHub.gml"));
        Assert.That(seam.Hooks, Is.EqualTo(new[] { "fishing.fish_selected" }));
        Assert.That(seam.Marker, Is.EqualTo("mmapi_fishing_run_fish_selected_callbacks"));
        Assert.That(seam.Op, Is.EqualTo(DispatchOp.Emit));
    }

    [Test]
    public void ShouldDeclareTheMuseumDonationAttemptEventBeforeRegistration()
    {
        var hook = _catalog.Hook("museum.donation_attempted");
        Assert.That(hook, Is.Not.Null);
        Assert.That(hook!.Kind, Is.EqualTo(HookKind.Event));
        Assert.That(hook.Doc, Does.Contain("attempted donation"));

        var seam = _catalog.Seams.Single(s => s.Id == "museum_donation_attempted");
        Assert.That(seam.File, Is.EqualTo("assets/gml/scripts/Museum.gml"));
        Assert.That(seam.Hooks, Is.EqualTo(new[] { "museum.donation_attempted" }));
        Assert.That(seam.Marker, Is.EqualTo("mmapi_museum_run_donation_attempted_callbacks"));
        Assert.That(seam.Op, Is.EqualTo(DispatchOp.Emit));
    }

    [Test]
    public void ShouldEmitMuseumDonationAfterRegistrationWhilePreservingTheAttemptEvent()
    {
        var hook = _catalog.Hook("museum.donate_item");
        Assert.That(hook, Is.Not.Null);
        Assert.That(hook!.Kind, Is.EqualTo(HookKind.Event));
        Assert.That(hook.Doc, Does.Contain("after the item is registered"));
        Assert.That(hook.Doc, Does.Contain("museum.donation_attempted"));

        var seam = _catalog.Seams.Single(s => s.Id == "museum_donate_item");
        Assert.That(seam.File, Is.EqualTo("assets/gml/scripts/Museum.gml"));
        Assert.That(seam.Hooks, Is.EqualTo(new[] { "museum.donate_item" }));
        Assert.That(seam.Op, Is.EqualTo(DispatchOp.Emit));
        Assert.That(seam.TargetFn, Is.EqualTo("donate_item_to_museum"));
        Assert.That(seam.TargetAt, Is.EqualTo("after"));
        Assert.That(seam.TargetAnchor, Is.EqualTo("register_item_to_museum(item_id);"));
        Assert.That(seam.Marker, Is.EqualTo("mmapi_museum_donate_item"));
    }

    [Test]
    public void ShouldDeclarePostStatePerkAcquiredAlongsideTheExistingPreStateEvent()
    {
        var preState = _catalog.Hook("player.acquire_perk");
        Assert.That(preState, Is.Not.Null);

        var hook = _catalog.Hook("player.perk_acquired");
        Assert.That(hook, Is.Not.Null);
        Assert.That(hook!.Kind, Is.EqualTo(HookKind.Event));
        Assert.That(hook.Doc, Does.Contain("after the perk is flagged owned and active"));
        Assert.That(hook.Doc, Does.Contain("player.acquire_perk"));

        var seam = _catalog.Seams.Single(s => s.Id == "player_perk_acquired");
        Assert.That(seam.File, Is.EqualTo("assets/gml/scripts/GameplaySystems/Player/Ari.gml"));
        Assert.That(seam.Hooks, Is.EqualTo(new[] { "player.perk_acquired" }));
        Assert.That(seam.Op, Is.EqualTo(DispatchOp.Emit));
        Assert.That(seam.TargetFn, Is.EqualTo("acquire_perk"));
        Assert.That(seam.TargetAt, Is.EqualTo("after"));
        Assert.That(seam.TargetAnchor, Is.EqualTo(
            "refresh_achievements([Requirement.HasAtLeastOneTierFivePerkPerCategory]);"));
        Assert.That(seam.Marker, Is.EqualTo("mmapi_player_perk_acquired"));
    }

    [Test]
    public void ShouldAcceptPetCosmeticStoreEntriesWithoutChangingVanillaStock()
    {
        Assert.That(_catalog.DeclaredCounts!.EngineFixes, Is.EqualTo(8));

        var fix = _catalog.EngineFixes.Single(f => f.Id == "store_pet_cosmetic_entry");
        Assert.That(fix.File, Is.EqualTo("assets/gml/scripts/Stores.gml"));
        Assert.That(fix.Anchor, Does.Contain("ItemId.AnimalCosmetic"));
        Assert.That(fix.Anchor, Does.Contain("Failed to parse an item"));
        Assert.That(fix.Replace, Does.Contain("entry[$ \"pet_cosmetic\"] != undefined"));
        Assert.That(fix.Replace, Does.Contain("PET_PROTOTYPE.cosmetic_sets.contains_key"));
        Assert.That(fix.Replace, Does.Contain("ItemId.PetCosmetic"));
        Assert.That(fix.Marker, Is.EqualTo("mmapi_store_pet_cosmetic_entry"));
    }

    [Test]
    public void ShouldFilterFactoryProductsWithoutBypassingTheDropPipeline()
    {
        var hook = _catalog.Hook("factory.product_drops");
        Assert.That(hook, Is.Not.Null);
        Assert.That(hook!.Kind, Is.EqualTo(HookKind.Filter));
        Assert.That(hook.Doc, Does.Contain("apiary or terrarium"));
        Assert.That(hook.Doc, Does.Contain("items.dropped"));

        var seam = _catalog.Seams.Single(s => s.Id == "factory_product_drops");
        Assert.That(seam.File, Is.EqualTo("assets/gml/scripts/GameplaySystems/Data/Grid/Furniture.gml"));
        Assert.That(seam.Hooks, Is.EqualTo(new[] { "factory.product_drops" }));
        Assert.That(seam.Op, Is.Null);
        Assert.That(seam.Replace, Does.Contain("mmapi_apply_filters(\"factory.product_drops\""));
        Assert.That(seam.Replace, Does.Contain("if (!is_array(__mmapi_factory_products))"));
        Assert.That(seam.Replace, Does.Contain("drop_item("));
        Assert.That(seam.Marker, Is.EqualTo("mmapi_factory_run_product_drops_filters"));
    }

    [Test]
    public void ShouldFilterEodCalendarEventsAndRenderCustomEntries()
    {
        var hook = _catalog.Hook("ui.eod_calendar_events");
        Assert.That(hook, Is.Not.Null);
        Assert.That(hook!.Kind, Is.EqualTo(HookKind.Filter));
        Assert.That(hook.Doc, Does.Contain("menu.events"));
        Assert.That(hook.Doc, Does.Contain("custom entry"));

        var filter = _catalog.Seams.Single(s => s.Id == "ui_eod_calendar_events");
        Assert.That(filter.File, Is.EqualTo("assets/gml/scripts/UI/Anchor/Menus/EodMenu.gml"));
        Assert.That(filter.Hooks, Is.EqualTo(new[] { "ui.eod_calendar_events" }));
        Assert.That(filter.Replace, Does.Contain("mmapi_apply_filters(\"ui.eod_calendar_events\""));
        Assert.That(filter.Replace, Does.Contain("is_numeric(__mmapi_eod_events.count())"));
        Assert.That(filter.Marker, Is.EqualTo("mmapi_ui_eod_calendar_events_filter"));

        var fallback = _catalog.Seams.Single(s => s.Id == "ui_eod_notification_custom_entry");
        Assert.That(fallback.File, Is.EqualTo("assets/gml/scripts/UI/Anchor/Menus/EodMenu.gml"));
        Assert.That(fallback.Hooks, Is.EqualTo(new[] { "ui.eod_calendar_events" }));
        Assert.That(fallback.Replace, Does.Contain("default: // mmapi_ui_eod_notification_custom_entry"));
        Assert.That(fallback.Replace, Does.Contain("icon = event[$ \"icon\"]"));
        Assert.That(fallback.Marker, Is.EqualTo("mmapi_ui_eod_notification_custom_entry"));
    }

    [Test]
    public void ShouldDeclareBothPetRewardSitesForOnePetRewardEvent()
    {
        var hook = _catalog.Hook("pet.reward_generated");
        Assert.That(hook, Is.Not.Null);
        Assert.That(hook!.Kind, Is.EqualTo(HookKind.Event));
        Assert.That(hook.Doc, Does.Contain("each concrete item"));

        var seams = _catalog.Seams
            .Where(s => s.Hooks.Contains("pet.reward_generated"))
            .ToList();
        Assert.That(seams.Select(s => s.Id), Is.EquivalentTo(new[]
        {
            "pet_reward_generated_forageable",
            "pet_reward_generated_item",
        }));
        Assert.That(seams, Has.All.Matches<SeamEntry>(s =>
            s.File == "assets/gml/scripts/Pet.gml" && s.Op == DispatchOp.Emit));
    }

    [Test]
    public void ShouldDeclareTheCropHarvestDestroyFilterBeforeTheDestroyBranch()
    {
        var hook = _catalog.Hook("crop.harvest_destroy");
        Assert.That(hook, Is.Not.Null);
        Assert.That(hook!.Kind, Is.EqualTo(HookKind.Filter));
        Assert.That(hook.Doc, Does.Contain("item drops, XP"));

        var seam = _catalog.Seams.Single(s => s.Id == "crop_harvest_destroy");
        Assert.That(seam.File, Is.EqualTo("assets/gml/scripts/GameplaySystems/Data/Grid/Crops.gml"));
        Assert.That(seam.Hooks, Is.EqualTo(new[] { "crop.harvest_destroy" }));
        Assert.That(seam.Marker, Is.EqualTo("mmapi_crop_run_harvest_destroy_filters"));
        Assert.That(seam.Op, Is.EqualTo(DispatchOp.Filter));
    }

    [Test]
    public void ShouldKeepTheDocCountSentencesInStepWithTheCatalog()
    {
        var repoRoot = FindRepoRoot();
        if (repoRoot is null)
            Assert.Ignore("docs/MMAPI not found - running outside the repo checkout");

        var counts = _catalog.DeclaredCounts!;
        var sentence = new Regex(
            @"\*\*(\d+) hooks\*\*, fed by \*\*(\d+) seams\*\*, \*\*(\d+) engine fixes\*\*, and \*\*(\d+) call rewrites?\*\*");
        foreach (var page in (string[]) ["CATALOG.md", "SEAMS.md", "HOOKS.md"])
        {
            var text = File.ReadAllText(Path.Combine(repoRoot!, "docs", "MMAPI", page));
            var match = sentence.Match(text);
            Assert.That(match.Success, Is.True, $"{page} carries no catalog count sentence");
            Assert.That(int.Parse(match.Groups[1].Value), Is.EqualTo(counts.Hooks), $"{page} hook count");
            Assert.That(int.Parse(match.Groups[2].Value), Is.EqualTo(counts.Seams), $"{page} seam count");
            Assert.That(int.Parse(match.Groups[3].Value), Is.EqualTo(counts.EngineFixes), $"{page} engine fix count");
            Assert.That(int.Parse(match.Groups[4].Value), Is.EqualTo(counts.CallRewrites), $"{page} call rewrite count");
        }
    }

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "docs", "MMAPI", "CATALOG.md")))
                return dir.FullName;
            dir = dir.Parent;
        }

        return null;
    }

    [Test]
    public void ShouldCarryADocOnEveryHook()
    {
        var undocumented = _catalog.HookDeclarations
            .Where(d => d.Doc.Length == 0)
            .Select(d => d.Name)
            .ToList();

        Assert.That(undocumented, Is.Empty);
    }

    [Test]
    public void ShouldRenderEveryKindIntoTheGeneratedCatalog()
    {
        var rendered = HookCatalogRenderer.Render(_catalog);

        foreach (var declaration in _catalog.HookDeclarations)
            Assert.That(rendered,
                Does.Contain($"\"{declaration.Name}\", \"{declaration.Kind.CatalogName()}\","));
    }

    [Test]
    public void ShouldDeclareContentionOnEveryOverrideHook()
    {
        // the loader enforces this; the assertion documents the shipped split
        var overrides = _catalog.HookDeclarations
            .Where(d => d.Kind == HookKind.Override)
            .ToDictionary(d => d.Name, d => d.Contention);
        Assert.That(overrides["crafting.max_crafts"], Is.EqualTo(HookContention.Exclusive));
        Assert.That(overrides.Where(o => o.Key != "crafting.max_crafts"),
            Has.All.Matches<KeyValuePair<string, HookContention?>>(
                o => o.Value == HookContention.ClaimScoped));

        var rendered = HookCatalogRenderer.Render(_catalog);
        Assert.That(rendered, Does.Contain("\"crafting.max_crafts\", \"exclusive\","));
        Assert.That(rendered, Does.Contain("\"object.interact\", \"claim-scoped\","));
    }

    [Test]
    public void ShouldResolveEveryFrameworkCallInAReplaceBody()
    {
        // The catalog's own replace bodies are fixed at build time - they ship
        // inside the installer - so their check belongs here, where a typo
        // fails the moment it is written rather than in someone's game. The
        // compat dialect late-binds, so `mmapi_emitt(...)` in a replace body
        // compiles clean, installs clean, and silently never fires.
        var framework = Directory.GetFiles(Path.Combine(PayloadDir, "mmapi"), "*.gml")
            .Order(StringComparer.Ordinal)
            .SelectMany(path => GmlScanner.TopLevelDefinitions(File.ReadAllText(path)))
            .Where(span => span.Form == FunctionForm.Decl)
            .Select(span => span.Name)
            .ToHashSet();
        framework.UnionWith(GmlScanner.TopLevelDefinitions(HookCatalogRenderer.Render(_catalog))
            .Where(span => span.Form == FunctionForm.Decl)
            .Select(span => span.Name));
        Assert.That(framework, Does.Contain("mmapi_emit"));

        Dictionary<string, List<string>> unresolved = [];
        foreach (var entry in _catalog.Entries)
        {
            foreach (var (name, _) in GmlScanner.FindPrefixedCalls(entry.Replace, MmapiPrefixes))
            {
                if (framework.Contains(name)
                    || name.StartsWith(DispatchRenderer.OrigPrefix, StringComparison.Ordinal)) continue;
                if (!unresolved.TryGetValue(name, out var ids))
                {
                    ids = [];
                    unresolved[name] = ids;
                }

                ids.Add(entry.Id);
            }
        }

        Assert.That(unresolved, Is.Empty);

        // every call_rewrite's target too: it redirects real engine call
        // sites into a wrapper, so a wrapper that does not exist silently
        // breaks them
        var missing = _catalog.CallRewrites
            .Where(r => !framework.Contains(r.To))
            .Select(r => r.Id)
            .ToList();
        Assert.That(missing, Is.Empty);
    }
}
