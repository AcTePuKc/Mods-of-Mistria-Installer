using Garethp.ModsOfMistriaInstallerLib.Bindings;
using Newtonsoft.Json.Linq;

namespace ModsOfMistriaInstallerLibTests.Bindings;

// Keeping the user's keybinds when a mod resets its own settings - and, just as importantly, not
// resurrecting one whose feature the update removed.
[TestFixture]
public class BindingVaultTest
{
    private string _modsFolder = "";
    private string _configFolder = "";

    [SetUp]
    public void SetUp()
    {
        _modsFolder = Path.Combine(Path.GetTempPath(), $"aim-vault-mods-{Guid.NewGuid():N}");
        _configFolder = Path.Combine(Path.GetTempPath(), $"aim-vault-cfg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_modsFolder);
        Directory.CreateDirectory(_configFolder);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var folder in new[] { _modsFolder, _configFolder })
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
    }

    private ModConfigFile Config(string modId, string json, string fileName = "")
    {
        var folder = Path.Combine(_configFolder, "mod_data", modId);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, fileName.Length > 0 ? fileName : $"{modId}.json");
        File.WriteAllText(path, json);
        return new ModConfigFile(modId, path, JObject.Parse(json));
    }

    private static ModBindingEntry Entry(ModConfigFile file, string field, string value) =>
        new(file.ModId, file.ModId, field, value, BindingSource.Configured)
        {
            File = file,
            Binding = MmapiBindingVocabulary.TryParse(value)
        };

    [Test]
    public void ShouldNoticeAModPuttingABindingBackToItsDefault()
    {
        var file = Config("quest_track", """{"keybind": "SHIFT+Q"}""");
        var vault = new BindingVault(_modsFolder);
        vault.Remember([Entry(file, "keybind", "SHIFT+Q")]);

        // The mod's next version resets the setting to its default.
        var afterUpdate = Entry(file, "keybind", "F3");
        var drift = new BindingVault(_modsFolder).FindDrift([afterUpdate]);

        Assert.Multiple(() =>
        {
            Assert.That(drift, Has.Exactly(1).Items);
            Assert.That(drift[0].Remembered, Is.EqualTo("SHIFT+Q"));
            Assert.That(drift[0].Now, Is.EqualTo("F3"));
        });
    }

    // The rule that makes this safe: a setting the update removed is forgotten, never re-applied.
    [Test]
    public void ShouldForgetABindingWhoseSettingIsGone()
    {
        var file = Config("quest_track", """{"keybind": "SHIFT+Q", "alt_keybind": "F4"}""");
        var vault = new BindingVault(_modsFolder);
        vault.Remember([Entry(file, "keybind", "SHIFT+Q"), Entry(file, "alt_keybind", "F4")]);
        Assert.That(vault.Count, Is.EqualTo(2));

        // The new version dropped the alternate binding entirely.
        var reopened = new BindingVault(_modsFolder);
        var drift = reopened.FindDrift([Entry(file, "keybind", "SHIFT+Q")]);

        Assert.Multiple(() =>
        {
            Assert.That(drift, Is.Empty, "nothing drifted");
            Assert.That(reopened.Count, Is.EqualTo(1), "the removed setting was forgotten");
            Assert.That(new BindingVault(_modsFolder).Count, Is.EqualTo(1), "and that was saved");
        });
    }

    // The scan only covers enabled mods, so anything else in the vault belongs to a mod that is
    // merely switched off. Forgetting those would wipe the user's keybinds every time they
    // unticked a mod and reloaded.
    [Test]
    public void ShouldKeepMemoriesOfModsTheScanDidNotCover()
    {
        var kept = Config("quest_track", """{"keybind": "SHIFT+Q"}""");
        var absent = Config("crafting_track", """{"keybind": "F8"}""");

        var vault = new BindingVault(_modsFolder);
        vault.Remember([Entry(kept, "keybind", "SHIFT+Q"), Entry(absent, "keybind", "F8")]);

        // Only one mod is enabled this time round.
        var reopened = new BindingVault(_modsFolder);
        reopened.FindDrift([Entry(kept, "keybind", "SHIFT+Q")]);

        Assert.That(reopened.Count, Is.EqualTo(2), "the disabled mod's binding is still remembered");
    }

    [Test]
    public void ShouldPutTheUsersChoiceBackIntoTheModsSettingsFile()
    {
        var file = Config("quest_track", """{"keybind": "SHIFT+Q", "show_icons": true}""");
        var vault = new BindingVault(_modsFolder);
        vault.Remember([Entry(file, "keybind", "SHIFT+Q")]);

        var drift = new BindingDrift(Entry(file, "keybind", "F3"), "SHIFT+Q");
        Assert.That(vault.Restore(drift), Is.True);

        var written = JObject.Parse(File.ReadAllText(file.Path));
        Assert.Multiple(() =>
        {
            Assert.That(written.Value<string>("keybind"), Is.EqualTo("SHIFT+Q"));
            // Everything the mod keeps beside its keybind has to survive the write.
            Assert.That(written.Value<bool>("show_icons"), Is.True);
        });
    }

    // A mod's compiled-in default is the author's choice, not the user's. Recording it would let
    // AIM overwrite a deliberate change in a new version.
    [Test]
    public void ShouldNotRememberCompiledInDefaults()
    {
        var vault = new BindingVault(_modsFolder);
        vault.Remember([
            new ModBindingEntry("wiki", "Wiki", "WIKI_DEFAULT_KEY", "F6", BindingSource.ModDefault)
        ]);

        Assert.That(vault.Count, Is.EqualTo(0));
    }

    [Test]
    public void ShouldTellTwoSettingsFilesOfOneModApart()
    {
        var main = Config("quick_stack", """{"keybind": "F7"}""");
        var extra = Config("quick_stack", """{"keybind": "F8"}""", "bindings.json");

        var vault = new BindingVault(_modsFolder);
        vault.Remember([Entry(main, "keybind", "F7"), Entry(extra, "keybind", "F8")]);

        Assert.That(vault.Count, Is.EqualTo(2));
    }

    [Test]
    public void ShouldStartEmptyRatherThanThrowOnACorruptFile()
    {
        File.WriteAllText(Path.Combine(_modsFolder, BindingVault.FileName), "{ not json");

        Assert.That(new BindingVault(_modsFolder).Count, Is.EqualTo(0));
    }
}
