using Garethp.ModsOfMistriaInstallerLib.Nexus;

namespace ModsOfMistriaInstallerLibTests.Nexus;

// Where AIM records which Nexus mod each installed folder came from, and which mods the user has
// frozen. It lives in the mods folder, so the tests work on a temporary one.
[TestFixture]
public class NexusInstallIndexTest
{
    private string _modsFolder = "";

    private static NexusInstallRecord Record(int modId = 175, int fileId = 900, string? version = "1.2") =>
        new("fieldsofmistria", modId, fileId, "mod.zip", version, DateTimeOffset.UtcNow);

    [SetUp]
    public void SetUp()
    {
        _modsFolder = Path.Combine(Path.GetTempPath(), $"aim-index-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_modsFolder);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_modsFolder)) Directory.Delete(_modsFolder, true);
    }

    [Test]
    public void ShouldRememberWhereAModCameFrom()
    {
        var folder = Path.Combine(_modsFolder, "Effe's Dig and Dive Site Marker");
        new NexusInstallIndex(_modsFolder).Record(folder, Record());

        var recalled = new NexusInstallIndex(_modsFolder).Get(folder);

        Assert.That(recalled, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(recalled!.ModId, Is.EqualTo(175));
            Assert.That(recalled.FileId, Is.EqualTo(900));
            Assert.That(recalled.Version, Is.EqualTo("1.2"));
            Assert.That(recalled.PageUrl, Is.EqualTo("https://www.nexusmods.com/fieldsofmistria/mods/175"));
            // The timestamp has to survive the round trip through JSON, not just the ids.
            Assert.That(recalled.InstalledAt, Is.EqualTo(DateTimeOffset.UtcNow).Within(TimeSpan.FromMinutes(5)));
        });
    }

    [Test]
    public void ShouldTreatAFolderAndItsArchiveAsTheSameMod()
    {
        // The same mod can arrive as a folder or as a zip, and swapping between the two must not
        // lose the record of where it came from.
        var index = new NexusInstallIndex(_modsFolder);
        index.Record(Path.Combine(_modsFolder, "Kemonomimi Player"), Record(113, 500));

        var viaArchive = index.Get(Path.Combine(_modsFolder, "Kemonomimi Player.zip"));

        Assert.That(viaArchive?.ModId, Is.EqualTo(113));
    }

    [Test]
    public void ShouldKeepAFreezeWhenTheModIsReinstalled()
    {
        var folder = Path.Combine(_modsFolder, "Frozen Mod");
        var index = new NexusInstallIndex(_modsFolder);

        index.Record(folder, Record(fileId: 1));
        index.SetFrozen(folder, true);
        index.Record(folder, Record(fileId: 2));

        Assert.Multiple(() =>
        {
            Assert.That(index.IsFrozen(folder), Is.True, "a re-download must not quietly unfreeze a mod");
            Assert.That(index.Get(folder)!.FileId, Is.EqualTo(2));
        });
    }

    [Test]
    public void ShouldFreezeAModItNeverInstalled()
    {
        var folder = Path.Combine(_modsFolder, "Hand Made Mod");
        var index = new NexusInstallIndex(_modsFolder);

        index.SetFrozen(folder, true);

        Assert.Multiple(() =>
        {
            Assert.That(index.IsFrozen(folder), Is.True);
            Assert.That(new NexusInstallIndex(_modsFolder).IsFrozen(folder), Is.True);
        });
    }

    // A freeze the user set and a freeze AIM set mean opposite things about updates: the first says
    // "stop offering", the second says "this is patched, tell me when there is a version that might
    // make the patch unnecessary". The reason is how the two are told apart, so it has to survive
    // everything a freeze survives.
    [Test]
    public void ShouldRememberWhyAimFrozeAMod()
    {
        var folder = Path.Combine(_modsFolder, "Patched Mod");
        var index = new NexusInstallIndex(_modsFolder);

        index.SetFrozen(folder, true, "AIM's own fix: remove the reference to images/missing.png");

        Assert.Multiple(() =>
        {
            Assert.That(index.FreezeReason(folder), Does.Contain("images/missing.png"));
            Assert.That(new NexusInstallIndex(_modsFolder).FreezeReason(folder),
                Does.Contain("images/missing.png"), "it has to survive a restart");
        });
    }

    [Test]
    public void ShouldKeepTheFreezeReasonWhenTheModIsReinstalled()
    {
        var folder = Path.Combine(_modsFolder, "Patched Mod");
        var index = new NexusInstallIndex(_modsFolder);

        index.Record(folder, Record(fileId: 1));
        index.SetFrozen(folder, true, "AIM's own fix");
        index.Record(folder, Record(fileId: 2));

        Assert.That(index.FreezeReason(folder), Is.EqualTo("AIM's own fix"),
            "losing the reason would leave AIM holding a mod back with no idea what for");
    }

    [Test]
    public void ShouldReportNoReasonForAFreezeTheUserSet()
    {
        var folder = Path.Combine(_modsFolder, "User Frozen Mod");
        var index = new NexusInstallIndex(_modsFolder);

        index.SetFrozen(folder, true);

        Assert.Multiple(() =>
        {
            Assert.That(index.IsFrozen(folder), Is.True);
            Assert.That(index.FreezeReason(folder), Is.Null, "the user's decision, not AIM's");
        });
    }

    [Test]
    public void ShouldDropTheReasonWhenTheModIsUnfrozen()
    {
        var folder = Path.Combine(_modsFolder, "Patched Mod");
        var index = new NexusInstallIndex(_modsFolder);

        index.SetFrozen(folder, true, "AIM's own fix");
        index.SetFrozen(folder, false);

        Assert.Multiple(() =>
        {
            Assert.That(index.IsFrozen(folder), Is.False);
            Assert.That(index.FreezeReason(folder), Is.Null,
                "the reason describes a freeze, and there is no longer one to describe");
        });
    }

    [Test]
    public void ShouldTreatAHandFreezeAsTakingTheDecisionBackFromAim()
    {
        var folder = Path.Combine(_modsFolder, "Patched Mod");
        var index = new NexusInstallIndex(_modsFolder);

        index.SetFrozen(folder, true, "AIM's own fix");
        index.SetFrozen(folder, true);

        Assert.That(index.FreezeReason(folder), Is.Null,
            "a user who freezes it by hand has said to stop offering the update");
    }

    [Test]
    public void ShouldForgetAMod()
    {
        var folder = Path.Combine(_modsFolder, "Temporary");
        var index = new NexusInstallIndex(_modsFolder);
        index.Record(folder, Record());

        index.Forget(folder);

        Assert.That(index.Get(folder), Is.Null);
    }

    [TestCase("https://www.nexusmods.com/fieldsofmistria/mods/175", 175, TestName = "a plain page link")]
    [TestCase("https://nexusmods.com/fieldsofmistria/mods/78?tab=files", 78, TestName = "a files tab link")]
    [TestCase("http://www.nexusmods.com/fieldsofmistria/mods/9/", 9, TestName = "http with a trailing slash")]
    public void ShouldReadAModIdFromANexusUrl(string url, int expected)
    {
        Assert.That(NexusInstallIndex.TryReadNexusUrl(url, out var game, out var modId), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(game, Is.EqualTo("fieldsofmistria"));
            Assert.That(modId, Is.EqualTo(expected));
        });
    }

    [TestCase("https://github.com/someone/mod/releases", TestName = "a GitHub link")]
    [TestCase("https://www.nexusmods.com/fieldsofmistria", TestName = "a game page with no mod")]
    [TestCase("", TestName = "empty")]
    [TestCase(null, TestName = "null")]
    public void ShouldRejectUrlsThatAreNotNexusModPages(string? url)
    {
        Assert.That(NexusInstallIndex.TryReadNexusUrl(url, out _, out _), Is.False);
    }

    [Test]
    public void ShouldSurviveACorruptIndexFile()
    {
        File.WriteAllText(Path.Combine(_modsFolder, NexusInstallIndex.FileName), "{ not json");

        Assert.That(new NexusInstallIndex(_modsFolder).Get(Path.Combine(_modsFolder, "Anything")), Is.Null);
    }
}
