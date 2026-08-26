using Garethp.ModsOfMistriaInstallerLib.Generator;
using ModsOfMistriaInstallerLibTests.Fixtures;

namespace ModsOfMistriaInstallerLibTests.Generator;

[TestFixture]
public class LegacyCosmeticCompatibilityDetectorTest
{
    [Test]
    public void IgnoresModsWithoutLegacyCosmetics()
    {
        var result = LegacyCosmeticCompatibilityDetector.Analyze(new MockMod(new Dictionary<string, object>
        {
            ["momi/cosmetics/new-format.toml"] = "[cosmetic]"
        }));

        Assert.That(result.UsesLegacyFormat, Is.False);
        Assert.That(result.Issues, Is.Empty);
    }

    [Test]
    public void RecognizesLegacyFormatWithoutTreatingItAsAnError()
    {
        var result = LegacyCosmeticCompatibilityDetector.Analyze(new MockMod(new Dictionary<string, object>
        {
            ["momi/outfit/test.toml"] = """
                [test_hair]
                ui_slot = "hair"
                """
        }));

        Assert.That(result.UsesLegacyFormat, Is.True);
        Assert.That(result.Issues, Is.Not.Empty);
        Assert.That(result.Issues, Has.Some.Contains("missing UI sprite"));
    }

    [Test]
    public void ReportsUnsupportedLegacySlot()
    {
        var result = LegacyCosmeticCompatibilityDetector.Analyze(new MockMod(new Dictionary<string, object>
        {
            ["momi/outfit/test.toml"] = """
                [test_item]
                ui_slot = "not_a_real_slot"
                """
        }));

        Assert.That(result.UsesLegacyFormat, Is.True);
        Assert.That(result.Issues, Has.Some.Contains("unsupported ui_slot"));
    }
}
