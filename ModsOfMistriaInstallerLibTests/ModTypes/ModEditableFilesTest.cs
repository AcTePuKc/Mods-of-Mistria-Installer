using Garethp.ModsOfMistriaInstallerLib.ModTypes;
using ModsOfMistriaInstallerLibTests.Fixtures;

namespace ModsOfMistriaInstallerLibTests.ModTypes;

// Which of a mod's own files AIM will offer to open in a text editor. The answer has to be a real
// path on disk: an archive-backed mod has nothing to edit, and offering it anyway would let the
// user type into a copy that the next install throws away.
[TestFixture]
public class ModEditableFilesTest
{
    private string _root = "";

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"aim-editable-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private MockMod ModAt(string folder, params string[] files)
    {
        Directory.CreateDirectory(folder);
        foreach (var file in files) File.WriteAllText(Path.Combine(folder, file), "{}");
        return new MockMod(files.ToList()) { DirName = folder };
    }

    [Test]
    public void ShouldFindAJsonManifest()
    {
        var mod = ModAt(Path.Combine(_root, "hats"), "manifest.json");

        Assert.That(ModEditableFiles.FindManifest(mod), Is.EqualTo(Path.Combine(_root, "hats", "manifest.json")));
    }

    [Test]
    public void ShouldFindATomlManifest()
    {
        var mod = ModAt(Path.Combine(_root, "hats"), "manifest.toml");

        Assert.That(ModEditableFiles.FindManifest(mod), Is.EqualTo(Path.Combine(_root, "hats", "manifest.toml")));
    }

    [Test]
    public void ShouldPreferAKnownConfigName()
    {
        var mod = ModAt(Path.Combine(_root, "hats"), "manifest.json", "zzz_config.json", "config.json");

        Assert.That(ModEditableFiles.FindConfig(mod), Is.EqualTo(Path.Combine(_root, "hats", "config.json")));
    }

    // Plenty of mods invent their own name, but nearly all of them still say "config" in it.
    [Test]
    public void ShouldFallBackToAConfigShapedName()
    {
        var mod = ModAt(Path.Combine(_root, "hats"), "manifest.json", "hat_config.toml");

        Assert.That(ModEditableFiles.FindConfig(mod), Is.EqualTo(Path.Combine(_root, "hats", "hat_config.toml")));
    }

    // The manifest is offered by its own menu item; it must never be mistaken for the config.
    [Test]
    public void ShouldNotOfferTheManifestAsAConfig()
    {
        var mod = ModAt(Path.Combine(_root, "hats"), "manifest.json");

        Assert.That(ModEditableFiles.FindConfig(mod), Is.Null);
    }

    [Test]
    public void ShouldOfferNothingForAModThatIsNotOnDisk()
    {
        var mod = new MockMod(["manifest.json"]) { DirName = Path.Combine(_root, "not-extracted.zip") };

        Assert.Multiple(() =>
        {
            Assert.That(ModEditableFiles.RootFolder(mod), Is.Null);
            Assert.That(ModEditableFiles.FindManifest(mod), Is.Null);
            Assert.That(ModEditableFiles.FindConfig(mod), Is.Null);
        });
    }
}
