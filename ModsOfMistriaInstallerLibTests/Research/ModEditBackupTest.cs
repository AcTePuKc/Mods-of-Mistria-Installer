using Garethp.ModsOfMistriaInstallerLib.Nexus;
using Garethp.ModsOfMistriaInstallerLib.Research;

namespace ModsOfMistriaInstallerLibTests.Research;

// Editing a mod, and getting back to how it was.
//
// The rule these tests exist to hold is that AIM never changes a file inside somebody else's mod
// without a full copy of that mod already sitting in the same store the version dropdown reads
// from. Undoing an AIM edit has to be the same gesture as rolling back an update - not a separate
// mechanism the user has to discover.
[TestFixture]
public class ModEditBackupTest
{
    private string _modsFolder = "";

    [SetUp]
    public void SetUp()
    {
        _modsFolder = Path.Combine(Path.GetTempPath(), $"aim-edit-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_modsFolder);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_modsFolder)) Directory.Delete(_modsFolder, true);
    }

    private string CreateMod(string name)
    {
        var path = Path.Combine(_modsFolder, name);
        Directory.CreateDirectory(Path.Combine(path, "images", "replace"));
        File.WriteAllText(Path.Combine(path, "manifest.toml"), "name = \"Witchy\"");
        File.WriteAllText(Path.Combine(path, "images", "replace", "axe.png"), "the original sprite");
        return path;
    }

    // Archive moves, because an update is about to overwrite the folder. Snapshot must copy: the
    // mod stays installed and only one file inside it is about to change.
    [Test]
    public void ShouldCopyRatherThanMoveTheModWhenTakingAnEditRestorePoint()
    {
        var store = new ModBackupStore(_modsFolder);
        var mod = CreateMod("Witchy Tools");

        var backup = store.Snapshot(mod, "2.1.0 before AIM's fix");

        Assert.That(backup, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(mod), Is.True, "the mod stays installed");
            Assert.That(File.Exists(Path.Combine(mod, "images", "replace", "axe.png")), Is.True);
            Assert.That(
                File.ReadAllText(Path.Combine(backup!.Path, "images", "replace", "axe.png")),
                Is.EqualTo("the original sprite"),
                "the copy is complete, not just the top level");
        });
    }

    // The whole point of using the existing store: the restore point turns up in the same list the
    // version dropdown is built from, labelled so it is obvious what it is.
    [Test]
    public void ShouldOfferTheRestorePointInTheVersionDropdown()
    {
        var store = new ModBackupStore(_modsFolder);
        var mod = CreateMod("Witchy Tools");

        store.Snapshot(mod, "2.1.0 before AIM's fix");

        var listed = store.List(ModBackupStore.ModNameFor(mod));

        Assert.That(listed, Has.Exactly(1).Items);
        Assert.That(listed[0].Describe(), Does.Contain("2.1.0 before AIM's fix"));
    }

    // Restoring puts the file back exactly as the author shipped it, through the ordinary restore
    // path rather than anything the editor owns.
    [Test]
    public void ShouldPutTheModBackFromTheDropdownAfterAnEdit()
    {
        var store = new ModBackupStore(_modsFolder);
        var mod = CreateMod("Witchy Tools");
        var sprite = Path.Combine(mod, "images", "replace", "axe.png");

        var backup = store.Snapshot(mod, "2.1.0 before AIM's fix");
        File.Move(sprite, sprite + ModFileEditor.DisabledSuffix);

        store.Restore(backup!, mod);

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(sprite), Is.EqualTo("the original sprite"));
            Assert.That(File.Exists(sprite + ModFileEditor.DisabledSuffix), Is.False);
        });
    }

    // Every edit is written down against the mod, because the risk is not the edit going wrong at
    // the time - it is nobody remembering it was ever made.
    [Test]
    public void ShouldRememberWhatItEditedAcrossRestarts()
    {
        var store = new AppliedEditStore(_modsFolder);
        store.Record(new AppliedEdit(
            "suushiico.witchy", "Set aside the shared axe sprite",
            ["images/replace/axe.png"], "/backups/x", DateTimeOffset.UtcNow));

        var reopened = new AppliedEditStore(_modsFolder);

        Assert.Multiple(() =>
        {
            Assert.That(reopened.WasEdited("suushiico.witchy"), Is.True);
            Assert.That(reopened.DescribeEdits("suushiico.witchy"), Does.Contain("shared axe sprite"));
            Assert.That(reopened.WasEdited("someone.else"), Is.False);
        });
    }

    // An update or a rollback replaces the folder, so the edits are gone and the marker would be
    // claiming something untrue.
    [Test]
    public void ShouldForgetTheEditsWhenTheModIsReplaced()
    {
        var store = new AppliedEditStore(_modsFolder);
        store.Record(new AppliedEdit("mod.a", "something", ["x"], null, DateTimeOffset.UtcNow));

        store.Forget("mod.a");

        Assert.That(new AppliedEditStore(_modsFolder).WasEdited("mod.a"), Is.False);
    }
}
