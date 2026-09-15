using System.Text;
using Garethp.ModsOfMistriaInstallerLib.Operations;
using Garethp.ModsOfMistriaInstallerLib.Seam;
using Newtonsoft.Json.Linq;

namespace ModsOfMistriaInstallerLibTests.Operations;

[TestFixture]
public class SeamDifferTest
{
    private const string CatalogToml = """
        version = 2

        [[hook]]
        name = "test.filter"
        kind = "filter"
        doc = "Test filter."

        [[hook]]
        name = "test.event"
        kind = "event"
        doc = "Test event."

        [[seam]]
        id = "filter"
        file = "gml/F.gml"
        target = { fn = "regen", at = "head" }
        op = "filter"
        hook = "test.filter"
        var = "amount"
        ctx = "{ cap: maximum }"

        [[seam]]
        id = "event"
        file = "gml/G.gml"
        context_before = '''
            play_sound(track);
        '''
        op = "emit"
        hook = "test.event"
        ctx = "{ track: track }"
        """;

    private const string Regen = """
        function regen(amount, maximum) {
            hp = min(hp + amount, maximum);
        }
        """;

    private const string Music = """
        function play_music(track) {
            var volume = 0.8;
            play_sound(track);
        }
        """;

    private static readonly SeamCatalog Catalog =
        SeamCatalogLoader.Load(Encoding.UTF8.GetBytes(CatalogToml), "synthetic");

    private static MemoryPristineSource Source(string regen, string music) => new(new Dictionary<string, byte[]>
    {
        ["assets/gml/F.gml"] = Encoding.UTF8.GetBytes(regen),
        ["assets/gml/G.gml"] = Encoding.UTF8.GetBytes(music),
    });

    private static SeamDiffResult Diff(string oldRegen, string oldMusic, string newRegen, string newMusic) =>
        SeamDiffer.Diff(Source(oldRegen, oldMusic), Source(newRegen, newMusic), Catalog);

    [Test]
    public void PassesIdenticalBuilds()
    {
        var result = Diff(Regen, Music, Regen, Music);

        Assert.That(result.Ok, Is.True);
        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Entries, Has.All.Matches<SeamRegionDiff>(entry =>
            entry.Status == SeamRegionStatus.Unchanged));
    }

    [Test]
    public void IgnoresWhitespaceAndCommentDrift()
    {
        var reformatted = Regen.Replace("    hp = min", "        hp = min // harmless comment\n    ");

        Assert.That(Diff(Regen, Music, reformatted, Music).Ok, Is.True);
    }

    [Test]
    public void ReportsChangedTargetFunction()
    {
        var changed = Regen.Replace("hp + amount, maximum", "hp + amount * 2, maximum");
        var result = Diff(Regen, Music, changed, Music);
        var entry = result.Entries.Single(item => item.EntryId == "filter");

        Assert.That(entry.Status, Is.EqualTo(SeamRegionStatus.Changed));
        Assert.That(entry.Region, Is.EqualTo("function 'regen'"));
        Assert.That(entry.DiffLines, Has.Some.Contains("amount * 2"));
        Assert.That(result.ExitCode, Is.EqualTo(1));
    }

    [Test]
    public void IncludesTheFunctionAroundATextAnchor()
    {
        var changed = Music.Replace("var volume = 0.8", "var volume = 0.5");
        var result = Diff(Regen, Music, Regen, changed);
        var entry = result.Entries.Single(item => item.EntryId == "event");

        Assert.That(entry.Status, Is.EqualTo(SeamRegionStatus.Changed));
        Assert.That(entry.Region, Is.EqualTo("function 'play_music' around the anchor"));
    }

    [Test]
    public void ReportsRenamedTargetAsMissing()
    {
        var renamed = Regen.Replace("function regen(", "function regenerate(");
        var entry = Diff(Regen, Music, renamed, Music).Entries.Single(item => item.EntryId == "filter");

        Assert.That(entry.Status, Is.EqualTo(SeamRegionStatus.NewMissing));
        Assert.That(entry.Note, Does.Contain("defined 0x"));
    }

    [Test]
    public void ElidesUnchangedLinesInALargeChangedFunction()
    {
        var filler = string.Concat(Enumerable.Range(0, 30).Select(index => $"    filler_{index}();\n"));
        var oldRegen = "function regen(amount, maximum) {\n" + filler
                       + "    hp = min(hp + amount, maximum);\n}\n";
        var newRegen = oldRegen.Replace("hp + amount", "hp + amount * 2");
        var entry = Diff(oldRegen, Music, newRegen, Music).Entries.Single(item => item.EntryId == "filter");

        Assert.That(entry.DiffLines, Has.Some.Contains("unchanged lines"));
        Assert.That(entry.DiffLines, Has.None.Contains("filler_5();"));
        Assert.That(entry.DiffLines, Has.Some.Contains("amount * 2"));
    }

    [Test]
    public void RefusesModdedArchivesAndCarriesTheJsonContract()
    {
        var modded = Regen.Replace("    hp = min", "    // mmapi_filter\n    hp = min");
        var result = Diff(Regen, Music, modded, Music);
        var json = JObject.Parse(SeamDiffer.ToJson(result, "old.zip", "new.zip"));

        Assert.That(result.ModdedMarkers, Has.Count.EqualTo(1));
        Assert.That(SeamDiffer.RenderText(result, "old.zip", "new.zip"), Does.Contain("REFUSED"));
        Assert.That(json["modded_markers"], Is.Not.Null);
    }
}
