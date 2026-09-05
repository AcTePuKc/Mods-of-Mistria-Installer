using Garethp.ModsOfMistriaInstallerLib.Crash;

namespace ModsOfMistriaInstallerLibTests.Crash;

// What the disable-and-check runs proved, kept between sessions.
[TestFixture]
public class CrashTrialStoreTest
{
    private string _modsFolder = "";

    [SetUp]
    public void SetUp()
    {
        _modsFolder = Path.Combine(Path.GetTempPath(), $"aim-trial-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_modsFolder);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_modsFolder)) Directory.Delete(_modsFolder, true);
    }

    [Test]
    public void ShouldAnswerUntestedForAModNobodyHasRun()
    {
        var store = new CrashTrialStore(_modsFolder);

        Assert.That(store.VerdictFor("crash-a", "some.mod", "1.0"),
            Is.EqualTo(CrashTrialVerdict.Untested));
    }

    [Test]
    public void ShouldSurviveBeingClosedAndReopened()
    {
        new CrashTrialStore(_modsFolder)
            .Record("crash-a", "some.mod", "1.0", CrashTrialVerdict.Cleared, "the crash came back");

        // The whole point of the store: the next session must not re-accuse a mod that has already
        // cost the user a four-minute run to rule out.
        var reopened = new CrashTrialStore(_modsFolder);

        Assert.Multiple(() =>
        {
            Assert.That(reopened.VerdictFor("crash-a", "some.mod", "1.0"),
                Is.EqualTo(CrashTrialVerdict.Cleared));
            Assert.That(reopened.Trial("crash-a", "some.mod", "1.0").Note,
                Is.EqualTo("the crash came back"));
        });
    }

    [Test]
    public void ShouldNotCarryAVerdictOverToADifferentCrash()
    {
        var store = new CrashTrialStore(_modsFolder);
        store.Record("crash-a", "some.mod", "1.0", CrashTrialVerdict.Cleared, "");

        Assert.That(store.VerdictFor("crash-b", "some.mod", "1.0"),
            Is.EqualTo(CrashTrialVerdict.Untested),
            "a different crash is a different question");
    }

    [Test]
    public void ShouldNotCarryAVerdictOverAnUpdate()
    {
        var store = new CrashTrialStore(_modsFolder);
        store.Record("crash-a", "some.mod", "1.0", CrashTrialVerdict.Cleared, "");

        Assert.That(store.VerdictFor("crash-a", "some.mod", "1.1"),
            Is.EqualTo(CrashTrialVerdict.Untested),
            "clearing a mod says something about the code that was on disk, and that code is gone");
    }

    [Test]
    public void ShouldIgnoreTheCaseOfAModId()
    {
        var store = new CrashTrialStore(_modsFolder);
        store.Record("crash-a", "Some.Mod", "1.0", CrashTrialVerdict.Guilty, "");

        Assert.That(store.VerdictFor("crash-a", "some.mod", "1.0"),
            Is.EqualTo(CrashTrialVerdict.Guilty));
    }

    [Test]
    public void ShouldNotCountAnInconclusiveRunAsAnAnswer()
    {
        var store = new CrashTrialStore(_modsFolder);
        store.Record("crash-a", "some.mod", "1.0", CrashTrialVerdict.Inconclusive, "the game did not start");

        Assert.Multiple(() =>
        {
            Assert.That(store.WasTested("crash-a", "some.mod", "1.0"), Is.False);

            // Still recorded, so the window can say the run happened and offer the mod again rather
            // than silently dropping it out of the queue.
            Assert.That(store.VerdictFor("crash-a", "some.mod", "1.0"),
                Is.EqualTo(CrashTrialVerdict.Inconclusive));
            Assert.That(store.Answered("crash-a"), Is.EqualTo(0));
        });
    }

    [Test]
    public void ShouldCountOnlyTheAnsweredTrialsForACrash()
    {
        var store = new CrashTrialStore(_modsFolder);
        store.Record("crash-a", "one.mod", "1.0", CrashTrialVerdict.Cleared, "");
        store.Record("crash-a", "two.mod", "1.0", CrashTrialVerdict.Guilty, "");
        store.Record("crash-a", "three.mod", "1.0", CrashTrialVerdict.Inconclusive, "");
        store.Record("crash-b", "four.mod", "1.0", CrashTrialVerdict.Cleared, "");

        Assert.That(store.Answered("crash-a"), Is.EqualTo(2));
    }

    [Test]
    public void ShouldForgetEveryVerdictForOneCrashAndLeaveTheOthers()
    {
        var store = new CrashTrialStore(_modsFolder);
        store.Record("crash-a", "one.mod", "1.0", CrashTrialVerdict.Cleared, "");
        store.Record("crash-b", "two.mod", "1.0", CrashTrialVerdict.Cleared, "");

        store.ForgetCrash("crash-a");

        Assert.Multiple(() =>
        {
            Assert.That(store.VerdictFor("crash-a", "one.mod", "1.0"),
                Is.EqualTo(CrashTrialVerdict.Untested));
            Assert.That(store.VerdictFor("crash-b", "two.mod", "1.0"),
                Is.EqualTo(CrashTrialVerdict.Cleared));
        });
    }

    [Test]
    public void ShouldStartFreshRatherThanThrowOnACorruptFile()
    {
        File.WriteAllText(Path.Combine(_modsFolder, CrashTrialStore.FileName), "{ not json");

        var store = new CrashTrialStore(_modsFolder);

        // Losing the verdicts costs some repeated runs. Refusing to open the crash window costs the
        // diagnosis, which is worse.
        Assert.That(store.VerdictFor("crash-a", "some.mod", "1.0"),
            Is.EqualTo(CrashTrialVerdict.Untested));

        Assert.DoesNotThrow(() =>
            store.Record("crash-a", "some.mod", "1.0", CrashTrialVerdict.Cleared, ""));
    }

    [Test]
    public void ShouldDropVerdictsOlderThanTheCutoff()
    {
        var store = new CrashTrialStore(_modsFolder);
        store.Record("crash-a", "some.mod", "1.0", CrashTrialVerdict.Cleared, "");

        store.PruneOlderThan(TimeSpan.FromDays(365));
        Assert.That(store.VerdictFor("crash-a", "some.mod", "1.0"),
            Is.EqualTo(CrashTrialVerdict.Cleared), "a verdict from a moment ago is not stale");

        store.PruneOlderThan(TimeSpan.Zero);
        Assert.That(store.VerdictFor("crash-a", "some.mod", "1.0"),
            Is.EqualTo(CrashTrialVerdict.Untested));
    }

    // The mod list's question, which is not the crash window's: this row, any crash at all.
    [Test]
    public void ShouldReportAModCaughtByAnyCrashAsGuilty()
    {
        var store = new CrashTrialStore(_modsFolder);
        store.Record("crash-b", "some.mod", "1.0", CrashTrialVerdict.Guilty, "the game ran without it");

        Assert.That(store.GuiltyVerdict("some.mod", "1.0")?.Note, Is.EqualTo("the game ran without it"));
        Assert.That(store.GuiltyVerdict("SOME.MOD", "1.0"), Is.Not.Null, "mod ids are not case sensitive");
    }

    [Test]
    public void ShouldNotCarryAGuiltyVerdictOntoANewVersionOfTheMod()
    {
        var store = new CrashTrialStore(_modsFolder);
        store.Record("crash-b", "some.mod", "1.0", CrashTrialVerdict.Guilty, "");

        // The update may well be the fix. Marking it as a crasher anyway teaches the user to
        // disbelieve the mark, which costs more than never showing it.
        Assert.That(store.GuiltyVerdict("some.mod", "1.1"), Is.Null);
    }

    [Test]
    public void ShouldNotReportAClearedModAsGuilty()
    {
        var store = new CrashTrialStore(_modsFolder);
        store.Record("crash-b", "some.mod", "1.0", CrashTrialVerdict.Cleared, "");

        Assert.That(store.GuiltyVerdict("some.mod", "1.0"), Is.Null);
    }
}
