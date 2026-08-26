using Garethp.ModsOfMistriaInstallerLib;
using Garethp.ModsOfMistriaInstallerLib.ModTypes;
using ModsOfMistriaInstallerLibTests.Fixtures;

namespace ModsOfMistriaInstallerLibTests;

// The load order suggestion. Two rules only: a mod loads after what it requires, and everything
// else keeps the order the user chose. Anything the planner cannot decide comes back as a note.
[TestFixture]
public class LoadOrderPlannerTest
{
    private static MockMod Mod(string id, string name, params (string Name, string Author)[] requires) =>
        new(new Dictionary<string, object> { ["manifest.toml"] = id })
        {
            Id = id,
            Name = name,
            Requirements = requires.Select(r => new ModRequirement(r.Name, r.Author)).ToList()
        };

    private static List<string> IdsOf(LoadOrderPlan plan) => plan.Order.Select(mod => mod.GetId()).ToList();

    [Test]
    public void ShouldMoveARequiredModAboveTheModThatRequiresIt()
    {
        // "framework" is required by "hats" but currently sits below it.
        var hats = Mod("author.hats", "Hats", ("Framework", "Author"));
        var framework = Mod("author.framework", "Framework");

        var plan = LoadOrderPlanner.Plan([hats, framework]);

        Assert.Multiple(() =>
        {
            Assert.That(IdsOf(plan), Is.EqualTo(new List<string> { "author.framework", "author.hats" }));
            Assert.That(plan.ChangesAnything, Is.True);
            Assert.That(plan.Notes.Where(note => note.Kind == LoadOrderNoteKind.DependencyMove),
                Has.Exactly(1).Items);
        });
    }

    [Test]
    public void ShouldLeaveAnOrderThatAlreadySatisfiesRequirements()
    {
        var framework = Mod("author.framework", "Framework");
        var hats = Mod("author.hats", "Hats", ("Framework", "Author"));

        var plan = LoadOrderPlanner.Plan([framework, hats]);

        Assert.Multiple(() =>
        {
            Assert.That(IdsOf(plan), Is.EqualTo(new List<string> { "author.framework", "author.hats" }));
            Assert.That(plan.ChangesAnything, Is.False);
            Assert.That(plan.Notes, Is.Empty);
        });
    }

    [Test]
    public void ShouldKeepUnrelatedModsWhereTheUserPutThem()
    {
        // Only the dependency pair should move; the rest of the list is the user's business.
        var zebra = Mod("a.zebra", "Zebra");
        var hats = Mod("author.hats", "Hats", ("Framework", "Author"));
        var apple = Mod("a.apple", "Apple");
        var framework = Mod("author.framework", "Framework");

        var plan = LoadOrderPlanner.Plan([zebra, hats, apple, framework]);

        Assert.That(IdsOf(plan), Is.EqualTo(new List<string>
        {
            "a.zebra", "author.framework", "author.hats", "a.apple"
        }));
    }

    [Test]
    public void ShouldResolveAChainOfRequirements()
    {
        var top = Mod("a.top", "Top", ("Middle", "A"));
        var middle = Mod("a.middle", "Middle", ("Bottom", "A"));
        var bottom = Mod("a.bottom", "Bottom");

        var plan = LoadOrderPlanner.Plan([top, middle, bottom]);

        Assert.That(IdsOf(plan), Is.EqualTo(new List<string> { "a.bottom", "a.middle", "a.top" }));
    }

    [Test]
    public void ShouldReportACircularRequirementInsteadOfGuessing()
    {
        var first = Mod("a.first", "First", ("Second", "A"));
        var second = Mod("a.second", "Second", ("First", "A"));

        var plan = LoadOrderPlanner.Plan([first, second]);

        Assert.Multiple(() =>
        {
            // Both mods survive the plan, in their original order.
            Assert.That(IdsOf(plan), Is.EqualTo(new List<string> { "a.first", "a.second" }));
            Assert.That(plan.Notes.Where(note => note.Kind == LoadOrderNoteKind.CircularRequirement),
                Has.Exactly(1).Items);
        });
    }

    [Test]
    public void ShouldReportARequirementThatIsNotInstalled()
    {
        var hats = Mod("author.hats", "Hats", ("Framework", "Author"));

        var plan = LoadOrderPlanner.Plan([hats]);

        var note = plan.Notes.SingleOrDefault(note => note.Kind == LoadOrderNoteKind.MissingRequirement);

        Assert.Multiple(() =>
        {
            Assert.That(note, Is.Not.Null);
            Assert.That(note!.Message, Does.Contain("Framework"));
            Assert.That(plan.ChangesAnything, Is.False);
        });
    }

    [Test]
    public void ShouldNameTheWinnerWhenTwoModsReplaceTheSameFile()
    {
        var first = new MockMod(new Dictionary<string, object>
        {
            ["images/replace/shared.png"] = new byte[] { 1 },
            ["manifest.toml"] = "first"
        }) { Id = "a.first", Name = "First" };

        var second = new MockMod(new Dictionary<string, object>
        {
            ["images/replace/shared.png"] = new byte[] { 2 },
            ["manifest.toml"] = "second"
        }) { Id = "a.second", Name = "Second" };

        var plan = LoadOrderPlanner.Plan([first, second]);

        var note = plan.Notes.SingleOrDefault(note => note.Kind == LoadOrderNoteKind.FileConflict);

        Assert.Multiple(() =>
        {
            Assert.That(note, Is.Not.Null);
            // The later mod wins, and the note has to say so rather than reordering them: which
            // sprite should survive is a preference, not something the planner can know.
            Assert.That(note!.Message, Does.Contain("\"Second\" overrides \"First\""));
            Assert.That(note.Details, Is.EqualTo(new[] { "images/replace/shared.png" }));
            Assert.That(plan.ChangesAnything, Is.False);
        });
    }

    [Test]
    public void ShouldIgnoreConflictsBetweenModsThatAreNotSelected()
    {
        var first = new MockMod(new Dictionary<string, object>
        {
            ["images/replace/shared.png"] = new byte[] { 1 },
            ["manifest.toml"] = "first"
        }) { Id = "a.first", Name = "First" };

        var second = new MockMod(new Dictionary<string, object>
        {
            ["images/replace/shared.png"] = new byte[] { 2 },
            ["manifest.toml"] = "second"
        }) { Id = "a.second", Name = "Second" };

        // Only the first mod is enabled, so nothing collides.
        var plan = LoadOrderPlanner.Plan([first, second], [first]);

        Assert.That(plan.Notes.Where(note => note.Kind == LoadOrderNoteKind.FileConflict), Is.Empty);
    }

    [Test]
    public void ShouldHandleAnEmptyList()
    {
        var plan = LoadOrderPlanner.Plan([]);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Order, Is.Empty);
            Assert.That(plan.Notes, Is.Empty);
            Assert.That(plan.ChangesAnything, Is.False);
        });
    }

    [Test]
    public void ShouldNotCrashWhenFolderAndArchiveCopiesShareAnId()
    {
        var folderCopy = new MockMod(new Dictionary<string, object> { ["manifest.toml"] = "folder" })
        {
            Id = "author.same", Name = "Same Mod", DirName = "folder-copy"
        };
        var archiveCopy = new MockMod(new Dictionary<string, object> { ["manifest.toml"] = "archive" })
        {
            Id = "author.same", Name = "Same Mod", DirName = "archive-copy"
        };

        Assert.DoesNotThrow(() => LoadOrderPlanner.Plan([folderCopy, archiveCopy], [folderCopy]));
    }
}
