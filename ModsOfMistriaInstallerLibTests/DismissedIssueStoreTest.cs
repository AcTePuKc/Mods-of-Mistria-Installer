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
        new DismissedIssueStore(_modsFolder).SetDismissed("FileConflict|a,b", true, "a overrides b");

        var reopened = new DismissedIssueStore(_modsFolder);

        Assert.Multiple(() =>
        {
            Assert.That(reopened.IsDismissed("FileConflict|a,b"), Is.True);
            Assert.That(reopened.IsDismissed("FileConflict|a,c"), Is.False, "a different pair is a different issue");
        });
    }

    // The complaint this behaviour was changed for: settle an issue, update one of the mods in it,
    // and the whole thing came back as though nothing had been decided. Every issue is re-detected
    // from disk each run, so the version was never what decided whether the problem was still there
    // - all it did was re-ask a question that had already been answered.
    [Test]
    public void ShouldKeepADismissalWhenAModInTheIssueIsUpdated()
    {
        new DismissedIssueStore(_modsFolder).SetDismissed("FileConflict|a@1.0,b@2.0", true, "a overrides b");

        var afterTheUpdate = new DismissedIssueStore(_modsFolder);

        Assert.Multiple(() =>
        {
            Assert.That(afterTheUpdate.IsDismissed("FileConflict|a@1.0,b@3.0"), Is.True);
            Assert.That(afterTheUpdate.IsDismissed("FileConflict|a,b"), Is.True,
                "the same issue, however the caller spells the versions");
        });
    }

    // Judgements recorded before issue identity dropped versions have to keep working, or the first
    // report opened after updating AIM asks every settled question again - which is the very thing
    // this was meant to stop.
    [Test]
    public void ShouldCarryOverJudgementsRecordedUnderOldVersionBearingKeys()
    {
        // Written exactly as AIM used to write them, taken from a real mods folder.
        File.WriteAllText(Path.Combine(_modsFolder, DismissedIssueStore.FileName),
            """
            {
              "issues": {
                "FileConflict|twinlamps.twins_bathroom_charcoal@2.0,twinlamps.twins_carpentry_black@1.7": {
                  "dismissed": true,
                  "dismissedAt": "2026-08-30T11:45:09.0000000-04:00",
                  "note": "both are mine, I know"
                },
                "CompatibilityWarning|legacy-gml|mykay.forage_sparkles@Beta 1.0": {
                  "dismissed": true,
                  "dismissedAt": "2026-08-30T11:45:09.0000000-04:00",
                  "verdict": "not-an-issue"
                }
              }
            }
            """);

        var store = new DismissedIssueStore(_modsFolder);

        Assert.Multiple(() =>
        {
            Assert.That(
                store.IsDismissed("FileConflict|twinlamps.twins_bathroom_charcoal,twinlamps.twins_carpentry_black"),
                Is.True);

            // Including a version an author wrote with a space in it.
            Assert.That(store.IsDismissed("CompatibilityWarning|legacy-gml|mykay.forage_sparkles"), Is.True);
            Assert.That(store.Verdict("CompatibilityWarning|legacy-gml|mykay.forage_sparkles")?.Kind,
                Is.EqualTo(DismissedIssueStore.VerdictNotAnIssue));
        });
    }

    // The same issue judged twice - once before an update, once after - collapses onto one entry.
    // The later judgement is the one the user meant.
    [Test]
    public void ShouldKeepTheNewerOfTwoOldJudgementsAboutOneIssue()
    {
        File.WriteAllText(Path.Combine(_modsFolder, DismissedIssueStore.FileName),
            """
            {
              "issues": {
                "FileConflict|a@1.0,b@1.0": {
                  "dismissed": true,
                  "dismissedAt": "2026-01-01T00:00:00.0000000+00:00",
                  "verdict": "not-an-issue"
                },
                "FileConflict|a@2.0,b@1.0": {
                  "dismissed": true,
                  "dismissedAt": "2026-06-01T00:00:00.0000000+00:00",
                  "verdict": "incompatible"
                }
              }
            }
            """);

        var store = new DismissedIssueStore(_modsFolder);

        Assert.Multiple(() =>
        {
            Assert.That(store.Count, Is.EqualTo(1));
            Assert.That(store.Verdict("FileConflict|a,b")?.Kind,
                Is.EqualTo(DismissedIssueStore.VerdictIncompatible));
        });
    }

    // The newer judgement replaces the older one whole. Merging field by field would let an older
    // verdict outlive a newer entry that has none, so the report would show a conclusion the user
    // had since withdrawn.
    [Test]
    public void ShouldNotLetAnOlderVerdictSurviveANewerJudgement()
    {
        File.WriteAllText(Path.Combine(_modsFolder, DismissedIssueStore.FileName),
            """
            {
              "issues": {
                "FileConflict|a@1.0,b@1.0": {
                  "dismissed": true,
                  "dismissedAt": "2026-01-01T00:00:00.0000000+00:00",
                  "verdict": "incompatible",
                  "note": "an old conclusion"
                },
                "FileConflict|a@2.0,b@1.0": {
                  "dismissed": true,
                  "dismissedAt": "2026-06-01T00:00:00.0000000+00:00"
                }
              }
            }
            """);

        var store = new DismissedIssueStore(_modsFolder);

        Assert.Multiple(() =>
        {
            Assert.That(store.IsDismissed("FileConflict|a,b"), Is.True);
            Assert.That(store.Verdict("FileConflict|a,b"), Is.Null,
                "the newer judgement recorded no verdict, so there is none");
        });
    }

    // And a newer decision to reopen an issue must not be overridden by an older tick.
    [Test]
    public void ShouldHonourANewerDecisionToReopenAnIssue()
    {
        File.WriteAllText(Path.Combine(_modsFolder, DismissedIssueStore.FileName),
            """
            {
              "issues": {
                "FileConflict|a@1.0,b@1.0": {
                  "dismissed": true,
                  "dismissedAt": "2026-01-01T00:00:00.0000000+00:00"
                },
                "FileConflict|a@2.0,b@1.0": {
                  "dismissed": false,
                  "dismissedAt": "2026-06-01T00:00:00.0000000+00:00",
                  "verdict": "incompatible"
                }
              }
            }
            """);

        var store = new DismissedIssueStore(_modsFolder);

        Assert.Multiple(() =>
        {
            Assert.That(store.IsDismissed("FileConflict|a,b"), Is.False);
            Assert.That(store.Verdict("FileConflict|a,b")?.Kind,
                Is.EqualTo(DismissedIssueStore.VerdictIncompatible),
                "a verdict can stand without the issue being hidden");
        });
    }

    // A mod with no version in its manifest produced a key ending in a bare "@". Those have to
    // migrate too, or the judgement is stranded under a key nothing will ever ask for again.
    [Test]
    public void ShouldCarryOverAKeyWrittenForAModWithNoVersion()
    {
        File.WriteAllText(Path.Combine(_modsFolder, DismissedIssueStore.FileName),
            """
            {
              "issues": {
                "FileConflict|a@,b@1.0": {
                  "dismissed": true,
                  "dismissedAt": "2026-01-01T00:00:00.0000000+00:00"
                }
              }
            }
            """);

        Assert.That(new DismissedIssueStore(_modsFolder).IsDismissed("FileConflict|a,b"), Is.True);
    }

    // An @ inside a warning message must not be mistaken for a version - and because the same
    // rewriting runs on the way in and on every lookup, a key can never be stored under one
    // spelling and looked for under another whatever it does.
    [Test]
    public void ShouldNotTreatTextInAMessageAsAVersion()
    {
        const string key = "CompatibilityWarning|validation|a.b|thanks @ everyone for the reports";

        new DismissedIssueStore(_modsFolder).SetDismissed(key, true);

        Assert.That(new DismissedIssueStore(_modsFolder).IsDismissed(key), Is.True);
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
