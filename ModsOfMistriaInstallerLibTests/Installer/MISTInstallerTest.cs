using ModsOfMistriaInstallerLibTests.Fixtures;
using ModsOfMistriaInstallerLibTests.TestUtils;

namespace ModsOfMistriaInstallerLibTests.Installer;

[TestFixture]
public class MISTInstallerTest
{
    [Test]
    public void CreatesMistMetadataForInstalledScene()
    {
        var mod = new MockMod(new Dictionary<string, object>
        {
            ["mist_scripts/gameplay_events/test_scene.mist"] = "scene(town);"
        });
        var modifier = new MockFileModifier(new Dictionary<string, string>());

        new MockInstaller().InstallMod(mod, modifier);

        var metadata = modifier.GetFile("assets/mist_scripts/gameplay_events/test_scene.meta.toml");
        Assert.That(metadata, Does.Contain("asset_kind = \"Mist\""));
        Assert.That(metadata, Does.Match("id = \\\"[0-9a-f]{16}\\\""));
    }
}
