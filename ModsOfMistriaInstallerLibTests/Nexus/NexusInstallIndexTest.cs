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
