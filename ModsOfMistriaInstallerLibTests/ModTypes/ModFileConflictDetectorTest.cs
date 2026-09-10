using Garethp.ModsOfMistriaInstallerLib.ModTypes;
using ModsOfMistriaInstallerLibTests.Fixtures;

namespace ModsOfMistriaInstallerLibTests.ModTypes;

[TestFixture]
public class ModFileConflictDetectorTest
{
    [Test]
    public void FindsSharedDestinationPathAndIgnoresManifests()
    {
        var alpha = new MockMod(new Dictionary<string, object>
        {
            ["images/replace/shared.png"] = new byte[] { 1 },
            ["manifest.toml"] = "alpha"
        }) { Id = "alpha" };
        var beta = new MockMod(new Dictionary<string, object>
        {
            ["images/replace/shared.png"] = new byte[] { 2 },
            ["manifest.toml"] = "beta"
        }) { Id = "beta" };

        var conflicts = ModFileConflictDetector.Find([alpha, beta]);

        Assert.That(conflicts, Has.Count.EqualTo(1));
        Assert.That(conflicts[0].Path, Is.EqualTo("images/replace/shared.png"));
        Assert.That(conflicts[0].ModIds, Is.EquivalentTo(new[] { "alpha", "beta" }));
        Assert.That(conflicts[0].Kind, Is.EqualTo(ModFileConflictKind.HardReplacement));
    }

    [Test]
    public void ClassifiesMergeableMetadataAndSharedLocalization()
    {
        var alpha = new MockMod(new Dictionary<string, object>
        {
            ["animations/foo.meta.toml"] = "a",
            ["localization/l10n.meta.toml"] = "a"
        }) { Id = "alpha" };
        var beta = new MockMod(new Dictionary<string, object>
        {
            ["animations/foo.meta.toml"] = "b",
            ["localization/l10n.meta.toml"] = "b"
        }) { Id = "beta" };

        var conflicts = ModFileConflictDetector.Find([alpha, beta]);

        Assert.That(conflicts.Single(x => x.Path == "animations/foo.meta.toml").Kind,
            Is.EqualTo(ModFileConflictKind.MergeableMetadata));
        Assert.That(conflicts.Single(x => x.Path == "localization/l10n.meta.toml").Kind,
            Is.EqualTo(ModFileConflictKind.SharedLocalization));
    }

    [Test]
    public void IgnoresSharedLegacyCosmeticCategoryIcon()
    {
        var alpha = new MockMod(new Dictionary<string, object>
        {
            ["animations/UI NEW/Store Menu/spr_ui_store_category_icon_moddedcosmetic.png"] = new byte[] { 1 }
        }) { Id = "alpha" };
        var beta = new MockMod(new Dictionary<string, object>
        {
            ["animations/UI NEW/Store Menu/spr_ui_store_category_icon_moddedcosmetic.png"] = new byte[] { 2 }
        }) { Id = "beta" };

        Assert.That(ModFileConflictDetector.Find([alpha, beta]), Is.Empty);
    }

    [Test]
    public void FindsFontPathConflict()
    {
        var alpha = new MockMod(new Dictionary<string, object>
        {
            ["fonts/shared.ttf"] = new byte[] { 1 },
            ["fonts/shared.meta.toml"] = FontMetadata("font-a")
        }) { Id = "alpha" };
        var beta = new MockMod(new Dictionary<string, object>
        {
            ["fonts/shared.ttf"] = new byte[] { 2 },
            ["fonts/shared.meta.toml"] = FontMetadata("font-b")
        }) { Id = "beta" };

        var conflict = ModFileConflictDetector.Find([alpha, beta])
            .Single(item => item.Path == "fonts/shared.ttf");

        Assert.That(conflict.Kind, Is.EqualTo(ModFileConflictKind.FontAsset));
    }

    [Test]
    public void FindsFontIdConflictWhenSourcePathsDiffer()
    {
        var alpha = new MockMod(new Dictionary<string, object>
        {
            ["fonts/alpha.ttf"] = new byte[] { 1 },
            ["fonts/alpha.meta.toml"] = FontMetadata("shared-font")
        }) { Id = "alpha" };
        var beta = new MockMod(new Dictionary<string, object>
        {
            ["fonts/beta.ttf"] = new byte[] { 2 },
            ["fonts/beta.meta.toml"] = FontMetadata("shared-font")
        }) { Id = "beta" };

        var conflict = ModFileConflictDetector.Find([alpha, beta])
            .Single(item => item.Path == "fonts/@id/shared-font");

        Assert.That(conflict.Kind, Is.EqualTo(ModFileConflictKind.FontAsset));
        Assert.That(conflict.ModIds, Is.EquivalentTo(new[] { "alpha", "beta" }));
    }

    private static string FontMetadata(string id) =>
        $"[meta_properties]\nasset_kind = \"Font\"\nid = \"{id}\"\n";
}
