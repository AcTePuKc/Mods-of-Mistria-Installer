using Garethp.ModsOfMistriaInstallerLib;
using Garethp.ModsOfMistriaInstallerLib.ModTypes;
using ModsOfMistriaInstallerLibTests.Fixtures;

namespace ModsOfMistriaInstallerLibTests;

// What a mod is for, worked out from what it installs.
//
// The rule these tests exist to hold is that a role is read off the mod's own folders and the
// requirements other mods declare — never off its name, its category, or a curated list. Each role
// corresponds to a real difference in how AIM's installers resolve a collision, so the tests are
// written in terms of that difference rather than in terms of the label.
[TestFixture]
public class ModRoleClassifierTest
{
    private static MockMod Mod(string id, params string[] files) =>
        new(files.ToList()) { Id = id, Name = id };

    private static MockMod Requiring(string id, string name, string author) =>
        new(new List<string> { "manifest.toml" })
        {
            Id = id,
            Name = id,
            Requirements = [new ModRequirement(name, author)]
        };

    [Test]
    public void ShouldCallAModOthersDependOnAFramework()
    {
        var framework = Mod("author.framework", "images/replace/axe.png");
        var dependent = Requiring("author.hats", "Framework", "Author");

        var roles = ModRoleClassifier.Classify([framework, dependent]);

        // Even though it replaces sprites, which would otherwise put it last. A mod others build on
        // has to load before them, and no folder can outweigh that.
        Assert.That(roles["author.framework"], Is.EqualTo(ModRole.Framework));
    }

    [Test]
    public void ShouldCallAModThatShipsCodeBehaviour()
    {
        var mod = Mod("author.tweaks", "gml/main.gml");

        Assert.That(ModRoleClassifier.RoleOf(mod, isDependedOn: false), Is.EqualTo(ModRole.Behaviour));
    }

    [Test]
    public void ShouldPutCodeAheadOfWhateverElseTheModShips()
    {
        var mod = Mod("author.big", "gml/main.gml", "images/replace/axe.png", "momi/cosmetics/hat.toml");

        // A GML mod that loses a first-wins name collision is dropped from the install entirely,
        // sprites and all. That risk outranks the preference about which sprite wins.
        Assert.That(ModRoleClassifier.RoleOf(mod, isDependedOn: false), Is.EqualTo(ModRole.Behaviour));
    }

    [Test]
    public void ShouldCallASpriteReplacerAReplacement()
    {
        var mod = Mod("author.recolour", "images/replace/axe.png");

        Assert.That(ModRoleClassifier.RoleOf(mod, isDependedOn: false), Is.EqualTo(ModRole.Replacement));
    }

    [Test]
    public void ShouldTakeTheLatestLayerItsContentsCallFor()
    {
        var mod = Mod("author.both", "momi/cosmetics/hat.toml", "images/replace/axe.png");

        // It adds content and replaces a sprite. The replacement is the part that only works if the
        // mod is late, so late is where it goes.
        Assert.That(ModRoleClassifier.RoleOf(mod, isDependedOn: false), Is.EqualTo(ModRole.Replacement));
    }

    [Test]
    public void ShouldCallANewContentModContent()
    {
        var mod = Mod("author.furniture", "momi/furniture/items/chairs.toml");

        Assert.That(ModRoleClassifier.RoleOf(mod, isDependedOn: false), Is.EqualTo(ModRole.Content));
    }

    [Test]
    public void ShouldCallATableTweakADataOverride()
    {
        var mod = Mod("author.prices", "fiddle/items/tools.toml");

        Assert.That(ModRoleClassifier.RoleOf(mod, isDependedOn: false), Is.EqualTo(ModRole.DataOverride));
    }

    [Test]
    public void ShouldPreferContentOverDataOverrideWhenAModDoesBoth()
    {
        var mod = Mod("author.shop", "momi/furniture/items/chairs.toml", "fiddle/items/tools.toml");

        // Adding a thing and registering it in a table is one act, not two, and it belongs with the
        // other content mods rather than among the mods whose whole job is to overwrite values.
        Assert.That(ModRoleClassifier.RoleOf(mod, isDependedOn: false), Is.EqualTo(ModRole.Content));
    }

    [Test]
    public void ShouldPutAModItCannotClassifyInTheMiddle()
    {
        var mod = Mod("author.mystery", "manifest.toml");

        // The middle layer is the one whose contributions are merged rather than fought over, so a
        // wrong guess there overrules nobody.
        Assert.That(ModRoleClassifier.RoleOf(mod, isDependedOn: false), Is.EqualTo(ModRole.Content));
    }

    [Test]
    public void ShouldOrderTheLayersCodeFirstAndReplacementsLast()
    {
        // The enum's order is the load order, and the rest of the planner relies on that.
        Assert.That(new[]
        {
            ModRole.Framework, ModRole.Behaviour, ModRole.Content,
            ModRole.DataOverride, ModRole.Replacement
        }.Select(role => (int)role), Is.Ordered);
    }
}
