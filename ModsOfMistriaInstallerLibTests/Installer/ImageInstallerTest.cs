using Garethp.ModsOfMistriaInstallerLib;
using Garethp.ModsOfMistriaInstallerLib.Installer;
using Garethp.ModsOfMistriaInstallerLib.Models.SDK;
using Garethp.ModsOfMistriaInstallerLib.Utils;
using ModsOfMistriaInstallerLibTests.Fixtures;
using ModsOfMistriaInstallerLibTests.TestUtils;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Tomlyn;

namespace ModsOfMistriaInstallerLibTests.Installer;

[TestFixture]
public class ImageInstallerTest
{
    private const string GameMetaPath = "assets/animations/Title Screen/spr_test_clouds.meta.toml";
    private const string GamePngPath = "assets/animations/Title Screen/spr_test_clouds.png";

    private const string AtlasLessMeta = """
        [meta_properties]
        id = "19f4c499cafbf498"
        asset_kind = "Animation"

        [asset_properties]
        frame_size = [8, 8]
        """;

    [Test]
    public void WritesAtlasLessReplacementToStandalonePng()
    {
        var pngBytes = MakePng(8, 8);
        var (modifier, statuses) = InstallReplacement(pngBytes, AtlasLessMeta);

        Assert.Multiple(() =>
        {
            Assert.That(modifier.HasBinaryFile(GamePngPath), Is.True);
            Assert.That(modifier.GetBinaryFile(GamePngPath), Is.EqualTo(pngBytes));
            Assert.That(statuses, Has.Some.Contains("standalone PNG"));
        });

        var meta = TomlSerializer.Deserialize<SpriteMetaFile>(modifier.GetFile(GameMetaPath))
                   ?? throw new AssertionException("Expected sprite metadata");
        Assert.Multiple(() =>
        {
            Assert.That(meta.Meta?.Id, Is.EqualTo("19f4c499cafbf498"));
            Assert.That(meta.Asset?.Atlas, Is.Null);
        });
    }

    [Test]
    public void ResizesAtlasLessMetaAndStillWritesPng()
    {
        var pngBytes = MakePng(16, 16);
        var (modifier, _) = InstallReplacement(pngBytes, AtlasLessMeta);

        var meta = TomlSerializer.Deserialize<SpriteMetaFile>(modifier.GetFile(GameMetaPath))
                   ?? throw new AssertionException("Expected sprite metadata");
        Assert.Multiple(() =>
        {
            Assert.That(modifier.GetBinaryFile(GamePngPath), Is.EqualTo(pngBytes));
            Assert.That(meta.Asset?.FrameWidth, Is.EqualTo(16));
            Assert.That(meta.Asset?.FrameHeight, Is.EqualTo(16));
        });
    }

    private static (MockFileModifier, List<string>) InstallReplacement(byte[] pngBytes, string gameMeta)
    {
        var mod = new MockMod(new Dictionary<string, object>
        {
            ["images/replace/spr_test_clouds.png"] = pngBytes,
        });
        var modifier = new MockFileModifier(new Dictionary<string, string>
        {
            [GameMetaPath] = gameMeta,
        });
        var statuses = new List<string>();

        new ImageInstaller(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                new AtlasUtilities("", modifier),
                modifier)
            .Install(mod, new GeneratedInformation(), (status, _) => statuses.Add(status));

        return (modifier, statuses);
    }

    private static byte[] MakePng(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        image[0, 0] = new Rgba32(18, 1, 0, 255);
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }
}
