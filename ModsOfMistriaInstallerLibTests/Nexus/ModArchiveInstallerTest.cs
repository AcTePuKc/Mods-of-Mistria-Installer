using System.IO.Compression;
using Garethp.ModsOfMistriaInstallerLib.Nexus;

namespace ModsOfMistriaInstallerLibTests.Nexus;

// Unpacking a mod downloaded from Nexus. Everything is anchored on the manifest inside the
// archive, which is what makes the "nested folders" mistake described in the README a non-issue.
[TestFixture]
public class ModArchiveInstallerTest
{
    private string _workspace = "";
    private string _modsFolder = "";

    private const string Manifest = "name = \"Test Mod\"\nauthor = \"Tester\"\nversion = \"1.0.0\"\n";

    [SetUp]
    public void SetUp()
    {
        _workspace = Path.Combine(Path.GetTempPath(), $"aim-archive-test-{Guid.NewGuid():N}");
        _modsFolder = Path.Combine(_workspace, "mods");
        Directory.CreateDirectory(_modsFolder);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_workspace)) Directory.Delete(_workspace, true);
    }

    private string CreateArchive(string name, params (string Path, string Content)[] entries)
    {
        var archivePath = Path.Combine(_workspace, name);

        using var stream = File.Create(archivePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        foreach (var (path, content) in entries)
        {
            using var writer = new StreamWriter(archive.CreateEntry(path).Open());
            writer.Write(content);
        }

        return archivePath;
    }

    [Test]
    public void ShouldInstallAModThatSitsAtTheArchiveRoot()
    {
        var archive = CreateArchive("Cosmetics recolor-78-2-1-1751991240.zip",
            ("manifest.toml", Manifest),
            ("momi/cosmetics/hat.toml", "hat"));

        var installed = ModArchiveInstaller.Install(
            archive, _modsFolder, "Cosmetics recolor-78-2-1-1751991240.zip");

        Assert.That(installed, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            // The mod id, version and timestamp Nexus appends to the file name are dropped, so the
            // next release of the same mod lands on this folder instead of beside it.
            Assert.That(installed[0].Name, Is.EqualTo("Cosmetics recolor"));
            Assert.That(installed[0].ReplacedExisting, Is.False);
            Assert.That(File.Exists(Path.Combine(installed[0].Path, "manifest.toml")), Is.True);
            Assert.That(File.Exists(Path.Combine(installed[0].Path, "momi", "cosmetics", "hat.toml")), Is.True);
        });
    }

    [Test]
    public void ShouldFlattenAnArchiveWhoseModIsNestedInsideAnotherFolder()
    {
        var archive = CreateArchive("nested.zip",
            ("Some Mod-78-2-1/Some Mod/manifest.toml", Manifest),
            ("Some Mod-78-2-1/Some Mod/images/hat.png", "png"),
            ("Some Mod-78-2-1/readme.txt", "ignore me"));

        var installed = ModArchiveInstaller.Install(archive, _modsFolder, "nested.zip");

        Assert.That(installed, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            // The manifest must end up directly inside the mod folder - a folder deeper and the
            // installer would not find the mod at all.
            Assert.That(File.Exists(Path.Combine(_modsFolder, "Some Mod", "manifest.toml")), Is.True);
            Assert.That(File.Exists(Path.Combine(_modsFolder, "Some Mod", "images", "hat.png")), Is.True);
            Assert.That(File.Exists(Path.Combine(_modsFolder, "Some Mod", "readme.txt")), Is.False);
        });
    }

    [Test]
    public void ShouldInstallEveryModInABundle()
    {
        var archive = CreateArchive("bundle.zip",
            ("First Mod/manifest.toml", Manifest),
            ("Second Mod/manifest.json", "{\"name\": \"Second\"}"));

        var installed = ModArchiveInstaller.Install(archive, _modsFolder, "bundle.zip");

        Assert.That(installed.Select(mod => mod.Name), Is.EquivalentTo(new[] { "First Mod", "Second Mod" }));
    }

    [Test]
    public void ShouldTreatANewVersionOfARootLevelModAsTheSameMod()
    {
        var first = CreateArchive("Cosmetics recolor-78-2-1-1751991240.zip", ("manifest.toml", Manifest));
        ModArchiveInstaller.Install(first, _modsFolder, "Cosmetics recolor-78-2-1-1751991240.zip");

        var second = CreateArchive("Cosmetics recolor-78-2-2-1760000000.zip", ("manifest.toml", Manifest));

        var conflict = Assert.Throws<ModArchiveConflictException>(
            () => ModArchiveInstaller.Install(second, _modsFolder, "Cosmetics recolor-78-2-2-1760000000.zip"));

        Assert.Multiple(() =>
        {
            Assert.That(conflict!.Folders, Is.EqualTo(new List<string> { "Cosmetics recolor" }));
            Assert.That(Directory.GetDirectories(_modsFolder), Has.Length.EqualTo(1),
                "an update must not install alongside the version it replaces");
        });
    }

    [Test]
    public void ShouldKeepANumberThatBelongsToTheModName()
    {
        var archive = CreateArchive("Portal 2 Decor.zip", ("manifest.toml", Manifest));

        var installed = ModArchiveInstaller.Install(archive, _modsFolder, "Portal 2 Decor.zip");

        Assert.That(installed[0].Name, Is.EqualTo("Portal 2 Decor"));
    }

    [Test]
    public void ShouldRefuseToOverwriteAnExistingModByDefault()
    {
        Directory.CreateDirectory(Path.Combine(_modsFolder, "Some Mod"));
        var archive = CreateArchive("update.zip", ("Some Mod/manifest.toml", Manifest));

        var conflict = Assert.Throws<ModArchiveConflictException>(
            () => ModArchiveInstaller.Install(archive, _modsFolder, "update.zip"));

        Assert.That(conflict!.Folders, Is.EqualTo(new List<string> { "Some Mod" }));
    }

    [Test]
    public void ShouldReplaceAnExistingModWhenAsked()
    {
        var existing = Path.Combine(_modsFolder, "Some Mod");
        Directory.CreateDirectory(existing);
        File.WriteAllText(Path.Combine(existing, "old-file.txt"), "old");

        var archive = CreateArchive("update.zip",
            ("Some Mod/manifest.toml", Manifest),
            ("Some Mod/new-file.txt", "new"));

        var installed = ModArchiveInstaller.Install(
            archive, _modsFolder, "update.zip", ArchiveConflictBehaviour.Replace);

        Assert.Multiple(() =>
        {
            Assert.That(installed[0].ReplacedExisting, Is.True);
            Assert.That(File.Exists(Path.Combine(existing, "new-file.txt")), Is.True);
            Assert.That(File.Exists(Path.Combine(existing, "old-file.txt")), Is.False,
                "the previous version should be gone, not merged with the new one");
            Assert.That(Directory.Exists(existing + ".aim-old"), Is.False, "the backup should be cleaned up");
        });
    }

    [Test]
    public void ShouldRejectAnArchiveWithoutAManifest()
    {
        var archive = CreateArchive("not-a-mod.zip", ("readme.txt", "hello"));

        var error = Assert.Throws<ModArchiveException>(
            () => ModArchiveInstaller.Install(archive, _modsFolder, "not-a-mod.zip"));

        Assert.That(error!.Message, Does.Contain("manifest.toml"));
    }

    [Test]
    public void ShouldRefuseEntriesThatEscapeTheModFolder()
    {
        var archive = CreateArchive("evil.zip",
            ("Some Mod/manifest.toml", Manifest),
            ("Some Mod/../../escaped.txt", "nope"));

        try
        {
            ModArchiveInstaller.Install(archive, _modsFolder, "evil.zip");
        }
        catch (ModArchiveException)
        {
            // Rejecting the archive outright is the expected outcome. The assertion below is the
            // property that actually matters either way: nothing was written outside the mods folder.
            Assert.That(Directory.Exists(Path.Combine(_modsFolder, "Some Mod")), Is.False,
                "a failed extraction should not leave a half-written mod folder behind");
        }

        Assert.That(File.Exists(Path.Combine(_workspace, "escaped.txt")), Is.False);
    }

    [Test]
    public void ShouldReportAMissingArchivePlainly()
    {
        Assert.Throws<ModArchiveException>(
            () => ModArchiveInstaller.Install(
                Path.Combine(_workspace, "missing.zip"), _modsFolder, "missing.zip"));
    }
}
