using Garethp.ModsOfMistriaInstallerLib;
using Garethp.ModsOfMistriaInstallerLib.ModTypes;

namespace ModsOfMistriaInstallerLibTests;

// Bringing a hand-downloaded mod into the mods folder. The interesting case is the cross-volume
// one: Directory.Move cannot cross drives, so the folder is copied - and a copy is not atomic, so
// there is a window where a mod exists under its real name with only some of its files in it.
[TestFixture]
public class ModDropFoldersTest
{
    private string _workspace = "";
    private string _modsFolder = "";
    private string _dropFolder = "";

    private const string Manifest = "name = \"Test Mod\"\nauthor = \"Tester\"\nversion = \"1.0.0\"\n";

    [SetUp]
    public void SetUp()
    {
        _workspace = Path.Combine(Path.GetTempPath(), $"aim-drop-test-{Guid.NewGuid():N}");
        _modsFolder = Path.Combine(_workspace, "mods");
        _dropFolder = Path.Combine(_workspace, "downloads");
        Directory.CreateDirectory(_modsFolder);
        Directory.CreateDirectory(_dropFolder);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_workspace)) Directory.Delete(_workspace, true);
    }

    private string CreateModFolder(string name)
    {
        var folder = Path.Combine(_dropFolder, name);
        Directory.CreateDirectory(Path.Combine(folder, "images"));
        File.WriteAllText(Path.Combine(folder, "manifest.toml"), Manifest);
        File.WriteAllText(Path.Combine(folder, "images", "hat.png"), "png");
        return folder;
    }

    [Test]
    public void ShouldNotLeaveAPartialModBehindWhenACrossVolumeCopyFails()
    {
        var source = CreateModFolder("Some Mod");
        var destination = Path.Combine(_modsFolder, "Some Mod");

        // A copy that writes the manifest and then dies: exactly the shape that makes a partial
        // folder look like an installed mod.
        Assert.Throws<IOException>(() => ModDropFolders.MoveFolderForTesting(
            source, destination, _modsFolder, (from, to) =>
            {
                Directory.CreateDirectory(to);
                File.Copy(Path.Combine(from, "manifest.toml"), Path.Combine(to, "manifest.toml"));
                throw new IOException("The drive went away half way through.");
            }));

        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(destination), Is.False,
                "a failed copy must not leave a mod folder AIM would install from");
            Assert.That(Directory.GetDirectories(_modsFolder), Is.Empty,
                "and it must not leave the staging folder behind either");
            Assert.That(File.Exists(Path.Combine(source, "manifest.toml")), Is.True,
                "the download the user still has must not be consumed by a failed import");
            Assert.That(File.Exists(Path.Combine(source, "images", "hat.png")), Is.True);
        });
    }

    [Test]
    public void ShouldNotShowACrossVolumeCopyAsAModUntilEveryFileIsThere()
    {
        var source = CreateModFolder("Some Mod");
        var destination = Path.Combine(_modsFolder, "Some Mod");

        var scannableMidCopy = new List<string>();

        ModDropFolders.MoveFolderForTesting(source, destination, _modsFolder, (from, to) =>
        {
            Directory.CreateDirectory(to);
            File.Copy(Path.Combine(from, "manifest.toml"), Path.Combine(to, "manifest.toml"));

            // This is the moment a watcher sweep or a list reload would land in. Anything the scan
            // could pick up here is what the user would have been shown as an installed mod: a
            // folder with a manifest and nothing else in it.
            scannableMidCopy.AddRange(Directory.GetDirectories(_modsFolder)
                .Where(folder => !Path.GetFileName(folder).StartsWith('.'))
                .Where(folder => FolderMod.GetModLocation(folder) is not null));

            Directory.CreateDirectory(Path.Combine(to, "images"));
            File.Copy(Path.Combine(from, "images", "hat.png"), Path.Combine(to, "images", "hat.png"));
        });

        Assert.Multiple(() =>
        {
            Assert.That(scannableMidCopy, Is.Empty,
                "a half-copied mod must be invisible to the mod scan, not offered as an install");
            Assert.That(File.Exists(Path.Combine(destination, "manifest.toml")), Is.True);
            Assert.That(File.Exists(Path.Combine(destination, "images", "hat.png")), Is.True);
            Assert.That(Directory.Exists(source), Is.False, "the original is removed once the copy is whole");
            Assert.That(
                Directory.GetDirectories(_modsFolder)
                    .Select(Path.GetFileName)
                    .Where(name => name!.StartsWith(ModDropFolders.StagingPrefixForTesting)),
                Is.Empty);
        });
    }

    // Two copies of one mod in the mods folder is not something a user can have meant: they fight
    // over every file they share, and the conflict report ends up blaming the mod for arguing with
    // itself. The download is left in the watched folder rather than deleted, and reported.
    [Test]
    public void ShouldLeaveADownloadAloneWhenTheUserAlreadyHasThatMod()
    {
        var installed = Path.Combine(_modsFolder, "Some Mod 78 2 1 1751991240");
        Directory.CreateDirectory(installed);
        File.WriteAllText(Path.Combine(installed, "manifest.toml"), Manifest);
        File.WriteAllText(Path.Combine(installed, "mine.txt"), "do not lose me");

        // A later release of the same mod, downloaded by hand. Nexus names it after the file, so
        // nothing about the folder name says it is the same mod - only the manifest does.
        var download = CreateModFolder("Some Mod 78 2 2 1760000000");
        Touch(download, TimeSpan.FromMinutes(1));

        var sweep = ModDropFolders.Import([_dropFolder], _modsFolder);

        Assert.Multiple(() =>
        {
            Assert.That(sweep.Imported, Is.Empty);
            Assert.That(sweep.AlreadyInstalled, Has.Count.EqualTo(1));
            Assert.That(sweep.AlreadyInstalled[0].InstalledAs, Is.EqualTo(installed));
            Assert.That(Directory.Exists(download), Is.True,
                "the download is the user's, so it is left where they put it");
            Assert.That(File.ReadAllText(Path.Combine(installed, "mine.txt")), Is.EqualTo("do not lose me"));
            Assert.That(Directory.GetDirectories(_modsFolder), Has.Length.EqualTo(1));
        });
    }

    // A different mod that happens to share a folder name is not a duplicate, and must still come
    // in - under a name of its own, without overwriting what is there.
    [Test]
    public void ShouldNotOverwriteADifferentModThatSharesAFolderName()
    {
        var installed = Path.Combine(_modsFolder, "Some Mod");
        Directory.CreateDirectory(installed);
        File.WriteAllText(Path.Combine(installed, "manifest.toml"), Manifest);
        File.WriteAllText(Path.Combine(installed, "mine.txt"), "do not lose me");

        var download = CreateModFolder("Some Mod");
        File.WriteAllText(Path.Combine(download, "manifest.toml"),
            "name = \"Other Mod\"\nauthor = \"Someone Else\"\nversion = \"1.0.0\"\n");
        // Downloads have to have stopped changing before AIM will touch them.
        Touch(download, TimeSpan.FromMinutes(1));

        var sweep = ModDropFolders.Import([_dropFolder], _modsFolder);

        Assert.Multiple(() =>
        {
            Assert.That(sweep.Imported, Has.Count.EqualTo(1));
            Assert.That(sweep.AlreadyInstalled, Is.Empty);
            Assert.That(File.ReadAllText(Path.Combine(installed, "mine.txt")), Is.EqualTo("do not lose me"));
            Assert.That(sweep.Imported[0].To, Is.EqualTo(Path.Combine(_modsFolder, "Some Mod (2)")));
        });
    }

    [Test]
    public void ShouldClearAwayStagingFoldersLeftByAnInterruptedRun()
    {
        var abandoned = Path.Combine(_modsFolder, $"{ModDropFolders.StagingPrefixForTesting}Some Mod");
        Directory.CreateDirectory(abandoned);
        File.WriteAllText(Path.Combine(abandoned, "manifest.toml"), Manifest);
        Touch(abandoned, TimeSpan.FromHours(1));

        ModDropFolders.Import([_dropFolder], _modsFolder);

        Assert.That(Directory.Exists(abandoned), Is.False);
    }

    [Test]
    public void ShouldLeaveAStagingFolderThatIsStillBeingWrittenTo()
    {
        var inProgress = Path.Combine(_modsFolder, $"{ModDropFolders.StagingPrefixForTesting}Other Mod");
        Directory.CreateDirectory(inProgress);
        File.WriteAllText(Path.Combine(inProgress, "manifest.toml"), Manifest);

        ModDropFolders.Import([_dropFolder], _modsFolder);

        Assert.That(Directory.Exists(inProgress), Is.True,
            "a copy running in another window must not have its staging folder pulled out from under it");
    }

    /// <summary>Ages a folder and everything in it, so the settle check treats it as finished.</summary>
    private static void Touch(string folder, TimeSpan age)
    {
        var when = DateTime.UtcNow - age;

        foreach (var entry in Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories))
        {
            if (Directory.Exists(entry)) Directory.SetLastWriteTimeUtc(entry, when);
            else File.SetLastWriteTimeUtc(entry, when);
        }

        Directory.SetLastWriteTimeUtc(folder, when);
    }
}
