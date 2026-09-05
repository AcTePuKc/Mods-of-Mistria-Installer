using Garethp.ModsOfMistriaInstallerLib.ModTypes;
using Garethp.ModsOfMistriaInstallerLib.Research;

namespace ModsOfMistriaInstallerLibTests.Research;

// The fixes AIM is willing to write into somebody else's mod without being told what to write.
//
// The rule these tests exist to hold is that the bar for proposing one is "the file says something
// demonstrably false", never "AIM is fairly confident". Every repair is removal-shaped: AIM takes a
// broken line out of play and never invents a value, because a plausible-looking wrong value is far
// harder for an author to spot in a bug report than a commented-out line with AIM's name on it.
[TestFixture]
public class ModRepairPlannerTest
{
    private string _root = "";

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"aim-repair-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private FolderMod Mod(params (string Path, string Contents)[] files)
    {
        var path = Path.Combine(_root, "Example Mod");
        Directory.CreateDirectory(path);

        File.WriteAllText(Path.Combine(path, "manifest.toml"), """
            name = "Example Mod"
            author = "Example Author"
            version = "1.0.0"
            manifestVersion = "1"
            minInstallerVersion = "0.1.0"
            """);

        foreach (var (relative, contents) in files)
        {
            var target = Path.Combine(path, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, contents);
        }

        return FolderMod.FromManifest(path);
    }

    [Test]
    public void ShouldProposeRemovingAReferenceToAFileTheModDoesNotHave()
    {
        var mod = Mod(("data/stores.toml", """
            [thing]
            icon = "images/missing.png"
            """));

        var repairs = ModRepairPlanner.For(mod);

        Assert.That(repairs, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(repairs[0].Path, Is.EqualTo("data/stores.toml"));
            Assert.That(repairs[0].Line, Is.EqualTo(2));
            Assert.That(repairs[0].Becomes.TrimStart(), Does.StartWith("#"));
            Assert.That(repairs[0].Becomes, Does.Contain("disabled by AIM"));

            // The original text survives inside the comment. A repair that deleted the line would
            // leave the author reading a bug report about a file they cannot reconstruct.
            Assert.That(repairs[0].Becomes, Does.Contain("images/missing.png"));
        });
    }

    [Test]
    public void ShouldLeaveAReferenceAloneWhenTheFileIsThere()
    {
        var mod = Mod(
            ("data/stores.toml", """
            [thing]
            icon = "images/present.png"
            """),
            ("images/present.png", "not really a png"));

        Assert.That(ModRepairPlanner.For(mod), Is.Empty);
    }

    [Test]
    public void ShouldAcceptAPathWrittenRelativeToTheDataFile()
    {
        var mod = Mod(
            ("data/stores.toml", """
            [thing]
            icon = "sprites/present.png"
            """),
            ("data/sprites/present.png", "not really a png"));

        // Mods organise their folders differently, and a rule that accused every one of them that
        // wrote paths this way would be worse than having no rule.
        Assert.That(ModRepairPlanner.For(mod), Is.Empty);
    }

    [Test]
    public void ShouldNotTreatAValueWithADotInItAsAFilePath()
    {
        var mod = Mod(("data/stores.toml", """
            [thing]
            name = "Mrs. Baker"
            id = "example.thing"
            """));

        Assert.That(ModRepairPlanner.For(mod), Is.Empty,
            "the extension is what makes a value a file reference, not the dot");
    }

    [Test]
    public void ShouldProposeRemovingTheSecondOfTwoIdenticalKeys()
    {
        var mod = Mod(("data/stores.toml", """
            [thing]
            price = "10"
            price = "20"
            """));

        var repairs = ModRepairPlanner.For(mod);

        Assert.That(repairs, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(repairs[0].Line, Is.EqualTo(3), "the second one, not the first");
            Assert.That(repairs[0].Why, Does.Contain("twice"));
            Assert.That(repairs[0].Becomes, Does.Contain("20"));
        });
    }

    [Test]
    public void ShouldAllowTheSameKeyOncePerEntryInAnArrayOfTables()
    {
        var mod = Mod(("data/stores.toml", """
            [[thing]]
            price = "10"

            [[thing]]
            price = "20"
            """));

        // An array of tables is meant to repeat its keys, once per entry. Flagging that would
        // report every well-formed list in every mod.
        Assert.That(ModRepairPlanner.For(mod), Is.Empty);
    }

    [Test]
    public void ShouldIgnoreALineItCannotParseWithCertainty()
    {
        var mod = Mod(("data/stores.toml", """
            [thing]
            icons = ["images/missing.png", "images/also-missing.png"]
            """));

        // An array value is not the narrow "key = string" form the planner understands. AIM says
        // nothing rather than guessing at how to rewrite it.
        Assert.That(ModRepairPlanner.For(mod), Is.Empty);
    }

    [Test]
    public void ShouldNotRepairALineThatIsAlreadyCommentedOut()
    {
        var mod = Mod(("data/stores.toml", """
            [thing]
            # icon = "images/missing.png"
            """));

        Assert.That(ModRepairPlanner.For(mod), Is.Empty);
    }
}
