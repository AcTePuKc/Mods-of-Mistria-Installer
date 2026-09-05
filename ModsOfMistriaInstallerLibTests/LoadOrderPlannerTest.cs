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

    // Reported even though there is no winner to pick. The mod rows show these, so a report that
    // left them out left a lit warning triangle with nothing behind it.
    [Test]
    public void ShouldReportOverlapsItMergesRatherThanOverrides()
    {
        var first = new MockMod(new Dictionary<string, object>
        {
            ["animations/foo.meta.toml"] = "first",
            ["manifest.toml"] = "first"
        }) { Id = "a.first", Name = "First" };

        var second = new MockMod(new Dictionary<string, object>
        {
            ["animations/foo.meta.toml"] = "second",
            ["manifest.toml"] = "second"
        }) { Id = "a.second", Name = "Second" };

        var plan = LoadOrderPlanner.Plan([first, second]);
        var note = plan.Notes.SingleOrDefault(note => note.Kind == LoadOrderNoteKind.FileConflict);

        Assert.Multiple(() =>
        {
            Assert.That(note, Is.Not.Null);

            // Worded as combining rather than overriding: telling the user to drag one below the
            // other would be advice that does nothing.
            Assert.That(note!.Message, Does.Contain("combines"));
            Assert.That(note.Message, Does.Not.Contain("overrides"));
            Assert.That(note.Details, Is.EqualTo(new[] { "animations/foo.meta.toml" }));

            // Its own dismissal, so settling it does not also settle a genuine override between
            // the same pair.
            Assert.That(note.IssueKey, Does.EndWith("|merge"));
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

    // ── Layering by what a mod installs ──────────────────────────────────────────
    //
    // Opt-in, and only for "Suggest order". The layers come from how AIM's own installers resolve a
    // collision — code first-wins, merged values and replacements last-wins — so the tests are
    // written in terms of what each mod ships rather than what it is called.

    private static MockMod Ships(string id, params string[] files) =>
        new(files.ToList()) { Id = id, Name = id };

    [Test]
    public void ShouldLeaveTheOrderAloneWhenLayeringIsNotAskedFor()
    {
        var recolour = Ships("author.recolour", "images/replace/axe.png");
        var code = Ships("author.tweaks", "gml/main.gml");

        // The conflict report asks for a plan so it can name who currently wins a shared file.
        // Rearranging the list underneath that question would label the wrong mod.
        var plan = LoadOrderPlanner.Plan([recolour, code]);

        Assert.Multiple(() =>
        {
            Assert.That(IdsOf(plan), Is.EqualTo(new[] { "author.recolour", "author.tweaks" }));
            Assert.That(plan.ChangesAnything, Is.False);
        });
    }

    [Test]
    public void ShouldLoadCodeBeforeReplacementsWhenLayeringIsAskedFor()
    {
        var recolour = Ships("author.recolour", "images/replace/axe.png");
        var code = Ships("author.tweaks", "gml/main.gml");

        var plan = LoadOrderPlanner.Plan([recolour, code], groupByRole: true);

        Assert.Multiple(() =>
        {
            Assert.That(IdsOf(plan), Is.EqualTo(new[] { "author.tweaks", "author.recolour" }));
            Assert.That(plan.ChangesAnything, Is.True);
            Assert.That(plan.Notes.Where(note => note.Kind == LoadOrderNoteKind.RoleMove),
                Is.Not.Empty, "a mod that moved has to say why");
        });
    }

    [Test]
    public void ShouldKeepTheUsersOwnOrderInsideALayer()
    {
        var first = Ships("author.first", "images/replace/a.png");
        var second = Ships("author.second", "images/replace/b.png");
        var third = Ships("author.third", "images/replace/c.png");

        var plan = LoadOrderPlanner.Plan([third, first, second], groupByRole: true);

        // Which of two sprite replacers should win is a preference, not a fact. The planner has
        // nothing to say about it and must not pretend otherwise.
        Assert.Multiple(() =>
        {
            Assert.That(IdsOf(plan), Is.EqualTo(new[] { "author.third", "author.first", "author.second" }));
            Assert.That(plan.ChangesAnything, Is.False);
            Assert.That(plan.Notes, Is.Empty);
        });
    }

    [Test]
    public void ShouldLetADeclaredRequirementOverruleTheLayering()
    {
        // The framework replaces sprites, which on its own would put it in the last layer, and the
        // mod that requires it only ships code, which would put it in an early one.
        var framework = new MockMod(new List<string> { "images/replace/axe.png" })
        {
            Id = "author.framework", Name = "Framework"
        };

        var dependent = new MockMod(new List<string> { "gml/main.gml" })
        {
            Id = "author.hats",
            Name = "Hats",
            Requirements = [new ModRequirement("Framework", "Author")]
        };

        var plan = LoadOrderPlanner.Plan([dependent, framework], groupByRole: true);

        Assert.That(IdsOf(plan), Is.EqualTo(new[] { "author.framework", "author.hats" }),
            "a declared requirement is the only hard fact here and has to win");
    }

    [Test]
    public void ShouldPutDataOverridesAfterTheContentTheyChange()
    {
        var prices = Ships("author.prices", "fiddle/items/tools.toml");
        var furniture = Ships("author.furniture", "momi/furniture/items/chairs.toml");

        var plan = LoadOrderPlanner.Plan([prices, furniture], groupByRole: true);

        // A merged table settles a repeated key last-wins, so a mod whose job is to change a value
        // has to be read after whatever set it.
        Assert.That(IdsOf(plan), Is.EqualTo(new[] { "author.furniture", "author.prices" }));
    }
}
