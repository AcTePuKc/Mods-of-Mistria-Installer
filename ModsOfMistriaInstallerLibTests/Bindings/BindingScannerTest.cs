using Garethp.ModsOfMistriaInstallerLib.Bindings;
using Garethp.ModsOfMistriaInstallerLib.ModTypes;

namespace ModsOfMistriaInstallerLibTests.Bindings;

// Finding what the mods are actually bound to. The distinction the whole feature rests on is
// "what the user chose" versus "what the mod ships with": scanning only the latter reports clashes
// between defaults the user separated in-game months ago.
[TestFixture]
public class BindingScannerTest
{
    private string _root = "";
    private string _configRoot = "";

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"aim-scan-{Guid.NewGuid():N}");
        _configRoot = Path.Combine(_root, "config");
        Directory.CreateDirectory(_configRoot);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private FolderMod Mod(string name, string modId, string gml)
    {
        var folder = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(folder, "gml"));
        File.WriteAllText(Path.Combine(folder, "gml", "main.gml"), gml);
        File.WriteAllText(
            Path.Combine(folder, "manifest.json"),
            $$"""{"name": "{{name}}", "version": "1.0", "author": "{{modId}}", "minInstallerVersion": "0.12"}""");
        return FolderMod.FromManifest(folder);
    }

    private ModDataStore StoreWith(string modId, string json, string fileName = "")
    {
        var folder = Path.Combine(_configRoot, ModDataStore.ModDataFolderName, modId);
        Directory.CreateDirectory(folder);
        File.WriteAllText(
            Path.Combine(folder, fileName.Length > 0 ? fileName : $"{modId}.json"), json);
        return new ModDataStore(_configRoot);
    }

    [Test]
    public void ShouldPreferTheConfiguredValueOverTheModsDefault()
    {
        var mod = Mod("QuestTrack", "quest_track", "#macro QT_DEFAULT_KEY \"F3\"\n");
        var store = StoreWith("QuestTrack", """{"keybind": "SHIFT+Q"}""");

        var entries = BindingScanner.Scan([mod], store);

        Assert.Multiple(() =>
        {
            Assert.That(entries, Has.Exactly(1).Items);
            Assert.That(entries[0].Value, Is.EqualTo("SHIFT+Q"));
            Assert.That(entries[0].Source, Is.EqualTo(BindingSource.Configured));
            Assert.That(entries[0].IsEditable, Is.True);
        });
    }

    // A mod that has never run has no settings file, and its compiled-in default is what the game
    // will use - so it belongs in the list, marked as a default rather than a decision.
    [Test]
    public void ShouldFallBackToTheModsDefaultWhenNothingIsConfigured()
    {
        var mod = Mod("Wiki", "deulo_wiki", "#macro WIKI_DEFAULT_KEY \"F6\"\n");

        var entries = BindingScanner.Scan([mod], new ModDataStore(_configRoot));

        Assert.Multiple(() =>
        {
            Assert.That(entries, Has.Exactly(1).Items);
            Assert.That(entries[0].Value, Is.EqualTo("F6"));
            Assert.That(entries[0].Source, Is.EqualTo(BindingSource.ModDefault));
            Assert.That(entries[0].IsEditable, Is.False, "there is no file to write");
        });
    }

    // #macro also declares mod ids and version strings, which are not bindings.
    [Test]
    public void ShouldIgnoreDeclarationsThatAreNotBindings()
    {
        var mod = Mod("Wiki", "deulo_wiki", """
            #macro WIKI_VERSION "1.0.0"
            #macro WIKI_MOD_ID "deulo_wiki"
            #macro WIKI_DEFAULT_KEY "F6"

            """);

        var entries = BindingScanner.Scan([mod], new ModDataStore(_configRoot));

        Assert.That(entries.Select(entry => entry.Field), Is.EqualTo(new[] { "WIKI_DEFAULT_KEY" }));
    }

    // "B" is a valid key name, so a value is only read as a binding when its field name says so.
    [Test]
    public void ShouldNotMistakeAnOrdinarySettingForABinding()
    {
        var mod = Mod("Markers", "harvest_markers", "// nothing\n");
        var store = StoreWith("Markers", """{"marker_colour": "B", "hotkey": "F4"}""");

        var entries = BindingScanner.Scan([mod], store);

        Assert.That(entries.Select(entry => entry.Field), Is.EqualTo(new[] { "hotkey" }));
    }

    [Test]
    public void ShouldFindBindingsInASecondSettingsFile()
    {
        var mod = Mod("QuickStack", "quick_stack", "// nothing\n");
        StoreWith("QuickStack", """{"enabled": true}""");
        var store = StoreWith("QuickStack", """{"keyboard_keybind": "F7"}""", "bindings.json");

        var entries = BindingScanner.Scan([mod], store);

        Assert.Multiple(() =>
        {
            Assert.That(entries, Has.Exactly(1).Items);
            Assert.That(entries[0].Value, Is.EqualTo("F7"));
            Assert.That(entries[0].File!.FileName, Is.EqualTo("bindings.json"));
        });
    }

    [Test]
    public void ShouldPairUpTheModsFightingOverOneKey()
    {
        var first = Mod("QuestTrack", "quest_track", "// nothing\n");
        var second = Mod("CraftingTrack", "crafting_track", "// nothing\n");
        StoreWith("QuestTrack", """{"keybind": "F1"}""");
        var store = StoreWith("CraftingTrack", """{"keybind": "F1"}""");

        var overlaps = BindingScanner.FindOverlaps(BindingScanner.Scan([first, second], store));

        Assert.That(overlaps, Has.Count.EqualTo(2), "each side names the other");
        Assert.That(overlaps.Values.First(), Has.Exactly(1).Items);
    }

    // A controller button and a key are separate namespaces, and one mod using a key twice is its
    // own business.
    [Test]
    public void ShouldNotReportOverlapsThatAreNotOverlaps()
    {
        var first = Mod("QuestTrack", "quest_track", "// nothing\n");
        var second = Mod("CraftingTrack", "crafting_track", "// nothing\n");
        StoreWith("QuestTrack", """{"keybind": "A", "alt_keybind": "A"}""");
        var store = StoreWith("CraftingTrack", """{"gamepad_hotkey": "GAMEPAD_A"}""");

        var overlaps = BindingScanner.FindOverlaps(BindingScanner.Scan([first, second], store));

        Assert.That(overlaps, Is.Empty);
    }

    // The case that matters in a real install: the settings folder is named after the id the mod
    // gives MMAPI, which matches neither AIM's manifest id nor the folder the user unpacked it
    // into. Getting this wrong lists the mod twice - once under the raw id with the real binding,
    // once under its proper name with the default - and then reports it as clashing with itself.
    [Test]
    public void ShouldMatchAModToItsSettingsByTheIdItGivesMmapi()
    {
        var mod = Mod("QuestTracker-1.2.0", "deulo", """
            #macro QT_DEFAULT_KEY "F3"
            mmapi_mod_declare("quest_track", QT_VERSION);

            """);
        var store = StoreWith("quest_track", """{"keybind": "SHIFT+Q"}""");

        var entries = BindingScanner.Scan([mod], store);

        Assert.Multiple(() =>
        {
            Assert.That(entries, Has.Exactly(1).Items, "the default must not be listed as well");
            Assert.That(entries[0].Value, Is.EqualTo("SHIFT+Q"));
            Assert.That(entries[0].ModName, Is.EqualTo("QuestTracker-1.2.0"), "shown by its real name");
            Assert.That(entries[0].ModId, Is.EqualTo(mod.GetId()), "and owned by the mod in the list");
            Assert.That(BindingScanner.FindOverlaps(entries), Is.Empty, "one mod cannot clash with itself");
        });
    }

    [Test]
    public void ShouldKeepAnUnrecognisedValueButNotParseIt()
    {
        var mod = Mod("Markers", "harvest_markers", "// nothing\n");
        var store = StoreWith("Markers", """{"hotkey": "ALT+J"}""");

        var entries = BindingScanner.Scan([mod], store);

        Assert.Multiple(() =>
        {
            Assert.That(entries[0].Value, Is.EqualTo("ALT+J"));
            Assert.That(entries[0].Binding, Is.Null, "the game would reject this");
        });
    }
}
