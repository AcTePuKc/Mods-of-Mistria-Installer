using Garethp.ModsOfMistriaInstallerLib.Nexus;

namespace ModsOfMistriaInstallerLibTests.Nexus;

// The copies AIM keeps so an update can be undone.
[TestFixture]
public class ModBackupStoreTest
{
    private string _modsFolder = "";

    [SetUp]
    public void SetUp()
    {
        _modsFolder = Path.Combine(Path.GetTempPath(), $"aim-backup-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_modsFolder);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_modsFolder)) Directory.Delete(_modsFolder, true);
    }

    private string CreateMod(string name, string marker)
    {
        var path = Path.Combine(_modsFolder, name);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "manifest.toml"), marker);
        return path;
    }

    [Test]
    public void ShouldMoveTheOldCopyOutOfTheModsFolder()
    {
        var store = new ModBackupStore(_modsFolder);
        var mod = CreateMod("Some Mod", "version one");

        var backup = store.Archive(mod, "1.0");

        Assert.That(backup, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(mod), Is.False, "the folder is moved, not copied");
            Assert.That(File.ReadAllText(Path.Combine(backup!.Path, "manifest.toml")), Is.EqualTo("version one"));
            Assert.That(backup.Version, Is.EqualTo("1.0"));
            // Hidden from the installer's own scan of the mods folder.
            Assert.That(backup.Path, Does.Contain(ModBackupStore.DirectoryName));
        });
    }

    [Test]
    public void ShouldRestoreTheBackupOverTheCurrentCopy()
    {
        var store = new ModBackupStore(_modsFolder);
        var mod = CreateMod("Some Mod", "version one");
        var backup = store.Archive(mod, "1.0")!;

        CreateMod("Some Mod", "version two");
        store.Restore(backup, mod);

        Assert.That(File.ReadAllText(Path.Combine(mod, "manifest.toml")), Is.EqualTo("version one"));
    }

    [Test]
    public void ShouldKeepTheReplacedCopySoARollbackIsUndoable()
    {
        var store = new ModBackupStore(_modsFolder);
        var mod = CreateMod("Some Mod", "version one");
        var backup = store.Archive(mod, "1.0")!;
        CreateMod("Some Mod", "version two");

        store.Restore(backup, mod);

        // Rolling back to 1.0 must not throw away version two.
        var remaining = store.List("Some Mod");
        Assert.That(remaining, Has.Count.EqualTo(1));
        Assert.That(File.ReadAllText(Path.Combine(remaining[0].Path, "manifest.toml")), Is.EqualTo("version two"));
    }

    [Test]
    public void ShouldKeepOnlyTheMostRecentBackups()
    {
        var store = new ModBackupStore(_modsFolder);

        for (var i = 0; i < 3; i++)
        {
            var mod = CreateMod("Some Mod", $"version {i}");
            store.Archive(mod, $"1.{i}", keep: 2);
            Thread.Sleep(1100);  // the backup folder name is stamped to the second
        }

        var backups = store.List("Some Mod");

        Assert.That(backups, Has.Count.EqualTo(2));
        Assert.That(backups.Select(backup => backup.Version), Is.EqualTo(new[] { "1.2", "1.1" }),
            "backups come back newest first");
    }

    [Test]
    public void ShouldRestoreTheOldestBackupWithoutPruningItAway()
    {
        // Archiving the copy being replaced prunes old backups. Restoring the oldest one must not
        // let that pruning delete the very folder being restored.
        var store = new ModBackupStore(_modsFolder);

        for (var i = 0; i < 3; i++)
        {
            var copy = CreateMod("Some Mod", $"version {i}");
            store.Archive(copy, $"1.{i}", keep: 3);
            Thread.Sleep(1100);
        }

        var mod = CreateMod("Some Mod", "current");
        var oldest = store.List("Some Mod").Last();

        store.Restore(oldest, mod);

        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(mod), Is.True);
            Assert.That(File.ReadAllText(Path.Combine(mod, "manifest.toml")), Is.EqualTo("version 0"));
        });
    }

    [Test]
    public void ShouldFindBackupsForAModInstalledAsAnArchive()
    {
        var store = new ModBackupStore(_modsFolder);
        store.Archive(CreateMod("Some Mod", "version one"), "1.0");

        // The same mod may be present as Some Mod.zip; its backups are still its own.
        Assert.That(store.HasBackups(ModBackupStore.ModNameFor(Path.Combine(_modsFolder, "Some Mod.zip"))), Is.True);
    }

    [Test]
    public void ShouldReportNothingForAModThatWasNeverBackedUp()
    {
        var store = new ModBackupStore(_modsFolder);

        Assert.Multiple(() =>
        {
            Assert.That(store.List("Never Installed"), Is.Empty);
            Assert.That(store.HasBackups("Never Installed"), Is.False);
            Assert.That(store.Archive(Path.Combine(_modsFolder, "Never Installed"), "1.0"), Is.Null);
        });
    }
}
