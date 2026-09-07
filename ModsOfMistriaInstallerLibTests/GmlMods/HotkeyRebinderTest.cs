using Garethp.ModsOfMistriaInstallerLib.GmlMods;
using Garethp.ModsOfMistriaInstallerLib.ModTypes;

namespace ModsOfMistriaInstallerLibTests.GmlMods;

// Moving one mod off a shortcut two mods are fighting over. This edits a mod's own source, so the
// tests care as much about what it refuses to touch as about what it rewrites.
[TestFixture]
public class HotkeyRebinderTest
{
    private string _root = "";

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"aim-rebind-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private FolderMod ModWith(string name, string gml)
    {
        var folder = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(folder, "gml"));
        File.WriteAllText(Path.Combine(folder, "gml", "main.gml"), gml);
        File.WriteAllText(
            Path.Combine(folder, "manifest.json"),
            $$"""{"name": "{{name}}", "version": "1.0", "author": "Tester", "minInstallerVersion": "0.12"}""");
        return FolderMod.FromManifest(folder);
    }

    private string GmlOf(string name) =>
        File.ReadAllText(Path.Combine(_root, name, "gml", "main.gml"));

    [Test]
    public void ShouldRewriteADeclaredBinding()
    {
        var mod = ModWith("tracker", "#macro TRACKER_KEY \"F1\"\nshow_debug_message(\"hi\");\n");

        var changed = HotkeyRebinder.Rebind(mod, "F1", "F9", null);

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.EqualTo(1));
            Assert.That(GmlOf("tracker"), Does.Contain("#macro TRACKER_KEY \"F9\""));
            Assert.That(GmlOf("tracker"), Does.Not.Contain("\"F1\""));
        });
    }

    // The whole safety argument rests on this: a bare vk_f1 can be a comparison, a lookup or a
    // comment, and rewriting one could stop the mod compiling.
    [Test]
    public void ShouldLeaveRawVirtualKeysAlone()
    {
        var mod = ModWith("blink", "if (keyboard_check_pressed(vk_f1)) { blink(); }\n");

        var capability = HotkeyRebinder.Inspect(mod, "F1");
        var changed = HotkeyRebinder.Rebind(mod, "F1", "F9", null);

        Assert.Multiple(() =>
        {
            Assert.That(capability.CanRebind, Is.False);
            Assert.That(capability.Blocker, Is.EqualTo(RebindBlocker.NotADeclaredBinding));
            Assert.That(changed, Is.EqualTo(0));
            Assert.That(GmlOf("blink"), Does.Contain("vk_f1"));
        });
    }

    [Test]
    public void ShouldReportWhichFilesHoldTheBinding()
    {
        var mod = ModWith("tracker", "#macro TRACKER_KEY \"F3\"\n");

        var capability = HotkeyRebinder.Inspect(mod, "F3");

        Assert.Multiple(() =>
        {
            Assert.That(capability.CanRebind, Is.True);
            Assert.That(capability.Bindings, Is.EqualTo(new[] { "gml/main.gml" }));
        });
    }

    // Offering a key another selected mod already uses would trade one clash for another.
    [Test]
    public void ShouldOnlyOfferKeysNobodyIsUsing()
    {
        var first = ModWith("tracker", "#macro TRACKER_KEY \"F1\"\n");
        var second = ModWith("blink", "if (keyboard_check_pressed(vk_f2)) { blink(); }\n");

        var free = HotkeyRebinder.FreeKeys([first, second]);

        Assert.Multiple(() =>
        {
            Assert.That(free, Does.Not.Contain("F1"));
            Assert.That(free, Does.Not.Contain("F2"));
            Assert.That(free, Does.Contain("F9"));
        });
    }

    [Test]
    public void ShouldNotRebindToTheKeyItIsAlreadyOn()
    {
        var mod = ModWith("tracker", "#macro TRACKER_KEY \"F1\"\n");

        Assert.That(HotkeyRebinder.Rebind(mod, "F1", "F1", null), Is.EqualTo(0));
    }
}
