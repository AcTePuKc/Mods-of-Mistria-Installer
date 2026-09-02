using Garethp.ModsOfMistriaInstallerLib;
using Garethp.ModsOfMistriaInstallerLib.Installer;
using ModsOfMistriaInstallerLibTests.Fixtures;
using ModsOfMistriaInstallerLibTests.TestUtils;

namespace ModsOfMistriaInstallerLibTests.Installer;

[TestFixture]
public class TOMLInstallerTest
{
    [Test]
    public void ShouldFoldARetiredAtlasCategoryInTheInstalledMeta()
    {
        var installed = InstallAnimationMeta("""
            [asset_properties]
            frame_size = [8, 8]
            atlas = "Shadow"
            """);

        Assert.That(installed, Does.Contain("atlas = \"Default\""));
        Assert.That(installed, Does.Not.Contain("Shadow"));
    }

    [Test]
    public void ShouldKeepACustomAtlasCategory()
    {
        var installed = InstallAnimationMeta("""
            [asset_properties]
            frame_size = [8, 8]
            atlas = "MossCavernWorld"
            """);

        Assert.That(installed, Does.Contain("atlas = \"MossCavernWorld\""));
    }

    // Installs one animation meta and returns the text written into assets/
    private static string InstallAnimationMeta(string metaToml)
    {
        var mod = new MockMod(new Dictionary<string, object>
        {
            { "animations/Modded/spr_test_thing.meta.toml", metaToml },
        });
        var modifier = new MockFileModifier(new Dictionary<string, string>());

        new MockInstaller().InstallMod(mod, modifier);

        return modifier.GetFile(Path.Combine("assets", "animations", "Modded", "spr_test_thing.meta.toml"));
    }

    [Test]
    public void ShouldNotWriteAnImplicitParentTableWhenMergingGameToml()
    {
        var mod = new MockMod(new Dictionary<string, object>
        {
            ["fiddle/animals.toml"] = """
                [production.male]
                days_to_produce = 1
                """
        });
        var files = new MockFileModifier(new Dictionary<string, string>
        {
            ["assets/fiddle/animals.toml"] = """
                [production.male]
                days_to_produce = 3
                normal_product = "rabbit_wool"

                [production.female]
                days_to_produce = 3
                normal_product = "rabbit_wool"
                """
        });
        var information = new GeneratedInformation
        {
            Toml =
            [
                new GeneratedTomlItem
                {
                    FilePath = "fiddle/animals.toml",
                    ReadFilePath = "fiddle/animals.toml"
                }
            ]
        };

        new TOMLInstaller([], files).Install(mod, information, (_, _) => { });

        var output = files.GetFile("assets/fiddle/animals.toml");
        Assert.That(output, Does.Not.Contain("[production]\n"));
        Assert.That(output, Does.Contain("days_to_produce = 1"));
        Assert.That(output, Does.Contain("[production.female]"));
    }
}
