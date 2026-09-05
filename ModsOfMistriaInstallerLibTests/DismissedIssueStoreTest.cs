using Garethp.ModsOfMistriaInstallerLib;

namespace ModsOfMistriaInstallerLibTests;

// Where AIM records the conflict-report findings the user has looked at and accepted. It lives in
// the mods folder, so the tests work on a temporary one.
[TestFixture]
public class DismissedIssueStoreTest
{
    private string _modsFolder = "";

    [SetUp]
    public void SetUp()
    {
        _modsFolder = Path.Combine(Path.GetTempPath(), $"aim-dismissed-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_modsFolder);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_modsFolder)) Directory.Delete(_modsFolder, true);
    }

    [Test]
    public void ShouldRememberADismissalAcrossRestarts()
    {
        new DismissedIssueStore(_modsFolder).SetDismissed("FileConflict|a@1.0,b@2.0", true, "a overrides b");

        var reopened = new DismissedIssueStore(_modsFolder);

        Assert.Multiple(() =>
        {
            Assert.That(reopened.IsDismissed("FileConflict|a@1.0,b@2.0"), Is.True);
            Assert.That(reopened.IsDismissed("FileConflict|a@1.0,b@3.0"), Is.False);
        });
    }

    [Test]
    public void ShouldForgetADismissalTheUserReverses()
    {
        var store = new DismissedIssueStore(_modsFolder);
        store.SetDismissed("HookConflict|x", true);
        store.SetDismissed("HookConflict|x", false);

        Assert.That(new DismissedIssueStore(_modsFolder).IsDismissed("HookConflict|x"), Is.False);
    }

    // A judgement about a mod the user no longer has would otherwise sit in the file for ever.
    // Age is the pruning signal because presence in the current report is not one: the report only
    // covers ticked mods, so pruning against it would discard judgements about disabled ones.
    [Test]
    public void ShouldPruneOnlyJudgementsOlderThanTheCutoff()
    {
        var path = Path.Combine(_modsFolder, DismissedIssueStore.FileName);
        File.WriteAllText(path, $$"""
            {
              "issues": {
                "FileConflict|recent": { "dismissedAt": "{{DateTimeOffset.UtcNow:o}}" },
                "FileConflict|ancient": { "dismissedAt": "{{DateTimeOffset.UtcNow.AddDays(-400):o}}" }
              }
            }
            """);

        new DismissedIssueStore(_modsFolder).PruneOlderThan(TimeSpan.FromDays(365));

        var reopened = new DismissedIssueStore(_modsFolder);
        Assert.Multiple(() =>
        {
            Assert.That(reopened.IsDismissed("FileConflict|recent"), Is.True);
            Assert.That(reopened.IsDismissed("FileConflict|ancient"), Is.False);
            Assert.That(reopened.Count, Is.EqualTo(1));
        });
    }

    // Losing the dismissals is annoying; refusing to open the conflict report is worse.
    [Test]
    public void ShouldStartEmptyRatherThanThrowOnACorruptFile()
    {
        File.WriteAllText(Path.Combine(_modsFolder, DismissedIssueStore.FileName), "{ not json");

        var store = new DismissedIssueStore(_modsFolder);

        Assert.Multiple(() =>
        {
            Assert.That(store.Count, Is.EqualTo(0));
            Assert.That(store.IsDismissed("anything"), Is.False);
        });
    }

    [Test]
    public void ShouldIgnoreAnEmptyKey()
    {
        var store = new DismissedIssueStore(_modsFolder);
        store.SetDismissed("", true);

        Assert.That(store.Count, Is.EqualTo(0));
    }
}
