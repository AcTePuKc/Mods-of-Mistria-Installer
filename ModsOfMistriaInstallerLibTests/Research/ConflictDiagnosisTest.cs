using Garethp.ModsOfMistriaInstallerLib.Research;
using ModsOfMistriaInstallerLibTests.Fixtures;

namespace ModsOfMistriaInstallerLibTests.Research;

// Working out whether a reported conflict is a real one by reading the files.
//
// Every claim here has to be derivable from what AIM's own installers do, so these tests are as
// much about what the diagnoser refuses to say as about what it says. A wrong "this is fine" is
// worse than the conservative report it replaces: the user dismisses the issue on AIM's word and
// then loses half a mod.
[TestFixture]
public class ConflictDiagnosisTest
{
    private static MockMod Mod(string id, Dictionary<string, object> files) =>
        new(files) { Id = id, Name = id };

    // ── Certainly harmless ───────────────────────────────────────────────────────

    [Test]
    public void ShouldClearTwoModsShippingTheSameFile()
    {
        var files = new Dictionary<string, object> { ["images/replace/axe.png"] = new byte[] { 1, 2, 3 } };

        var diagnosis = ConflictDiagnoser.Diagnose(
            ["images/replace/axe.png"], [Mod("alpha", files), Mod("beta", files)]);

        Assert.Multiple(() =>
        {
            Assert.That(diagnosis.Verdict, Is.EqualTo(DiagnosisVerdict.Harmless));
            Assert.That(diagnosis.Certain, Is.True);
            Assert.That(diagnosis.Files[0].Outcome, Is.EqualTo(FileOutcome.Identical));
        });
    }

    // TOMLInstaller merges each mod's file into the destination, so keys only one mod sets all
    // survive. Two mods adding different entries to the same table is not a conflict at all.
    [Test]
    public void ShouldClearTomlFilesThatSetDifferentKeys()
    {
        var alpha = Mod("alpha", new Dictionary<string, object>
        {
            ["fiddle/prices.toml"] = "[items]\nwitchy_axe = 500\n"
        });
        var beta = Mod("beta", new Dictionary<string, object>
        {
            ["fiddle/prices.toml"] = "[items]\nglass_hoe = 300\n"
        });

        var diagnosis = ConflictDiagnoser.Diagnose(["fiddle/prices.toml"], [alpha, beta]);

        Assert.Multiple(() =>
        {
            Assert.That(diagnosis.Verdict, Is.EqualTo(DiagnosisVerdict.Harmless));
            Assert.That(diagnosis.Files[0].Outcome, Is.EqualTo(FileOutcome.MergesCleanly));
        });
    }

    [Test]
    public void ShouldClearJsonObjectsThatSetDifferentKeys()
    {
        var alpha = Mod("alpha", new Dictionary<string, object>
        {
            ["data/shop.json"] = """{ "witchy_axe": { "price": 500 } }"""
        });
        var beta = Mod("beta", new Dictionary<string, object>
        {
            ["data/shop.json"] = """{ "glass_hoe": { "price": 300 } }"""
        });

        var diagnosis = ConflictDiagnoser.Diagnose(["data/shop.json"], [alpha, beta]);

        Assert.That(diagnosis.Verdict, Is.EqualTo(DiagnosisVerdict.Harmless));
    }

    // ── Real, but only about which mod wins ──────────────────────────────────────

    [Test]
    public void ShouldSayLoadOrderDecidesWhenAnImageIsReplaced()
    {
        var alpha = Mod("alpha", new Dictionary<string, object>
        {
            ["images/replace/axe.png"] = new byte[] { 1 }
        });
        var beta = Mod("beta", new Dictionary<string, object>
        {
            ["images/replace/axe.png"] = new byte[] { 2 }
        });

        var diagnosis = ConflictDiagnoser.Diagnose(["images/replace/axe.png"], [alpha, beta]);

        Assert.Multiple(() =>
        {
            Assert.That(diagnosis.Verdict, Is.EqualTo(DiagnosisVerdict.OrderDecides));
            Assert.That(diagnosis.Files[0].Outcome, Is.EqualTo(FileOutcome.LastWins));
            // Mods arrive in load order, so the last one is the one that wins as things stand.
            Assert.That(diagnosis.Files[0].WinnerModId, Is.EqualTo("beta"));
        });
    }

    [Test]
    public void ShouldNameTheKeysTwoModsBothSet()
    {
        var alpha = Mod("alpha", new Dictionary<string, object>
        {
            ["fiddle/prices.toml"] = "[items]\nwitchy_axe = 500\nglass_hoe = 300\n"
        });
        var beta = Mod("beta", new Dictionary<string, object>
        {
            ["fiddle/prices.toml"] = "[items]\nwitchy_axe = 900\nrake = 100\n"
        });

        var diagnosis = ConflictDiagnoser.Diagnose(["fiddle/prices.toml"], [alpha, beta]);

        Assert.Multiple(() =>
        {
            Assert.That(diagnosis.Verdict, Is.EqualTo(DiagnosisVerdict.PartialOverride));
            Assert.That(diagnosis.Files[0].Outcome, Is.EqualTo(FileOutcome.MergesWithOverride));
            // The one key they disagree on, and not the two they do not.
            Assert.That(diagnosis.Files[0].ContestedKeys, Is.EqualTo(new[] { "items.witchy_axe" }));
        });
    }

    // A three-way conflict where the first mod is not involved in the disagreement. Comparing every
    // mod against the first one would have missed this entirely.
    [Test]
    public void ShouldSeeADisagreementThatDoesNotInvolveTheFirstMod()
    {
        var alpha = Mod("alpha", new Dictionary<string, object>
        {
            ["fiddle/prices.toml"] = "[items]\nrake = 100\n"
        });
        var beta = Mod("beta", new Dictionary<string, object>
        {
            ["fiddle/prices.toml"] = "[items]\nhoe = 200\n"
        });
        var gamma = Mod("gamma", new Dictionary<string, object>
        {
            ["fiddle/prices.toml"] = "[items]\nhoe = 900\n"
        });

        var diagnosis = ConflictDiagnoser.Diagnose(["fiddle/prices.toml"], [alpha, beta, gamma]);

        Assert.That(diagnosis.Files[0].ContestedKeys, Is.EqualTo(new[] { "items.hoe" }));
    }

    // JSONInstaller writes a file whose root is an array over the destination rather than merging
    // it, so unlike an object this really does lose one mod's copy.
    [Test]
    public void ShouldNotClearJsonWhoseRootIsAList()
    {
        var alpha = Mod("alpha", new Dictionary<string, object> { ["data/list.json"] = """["a"]""" });
        var beta = Mod("beta", new Dictionary<string, object> { ["data/list.json"] = """["b"]""" });

        var diagnosis = ConflictDiagnoser.Diagnose(["data/list.json"], [alpha, beta]);

        Assert.Multiple(() =>
        {
            Assert.That(diagnosis.Files[0].Outcome, Is.EqualTo(FileOutcome.LastWins));
            Assert.That(diagnosis.Verdict, Is.EqualTo(DiagnosisVerdict.OrderDecides));
        });
    }

    // ── Refusing to guess ────────────────────────────────────────────────────────

    [Test]
    public void ShouldAdmitItCannotReadAMalformedFile()
    {
        var alpha = Mod("alpha", new Dictionary<string, object> { ["fiddle/x.toml"] = "[items]\na = 1\n" });
        var beta = Mod("beta", new Dictionary<string, object> { ["fiddle/x.toml"] = "this is not toml [[[" });

        var diagnosis = ConflictDiagnoser.Diagnose(["fiddle/x.toml"], [alpha, beta]);

        Assert.Multiple(() =>
        {
            Assert.That(diagnosis.Certain, Is.False);
            Assert.That(diagnosis.Files[0].Outcome, Is.EqualTo(FileOutcome.Unreadable));
        });
    }

    [Test]
    public void ShouldNotGuessAtAFileTypeItHasNoRuleFor()
    {
        var alpha = Mod("alpha", new Dictionary<string, object> { ["scripts/thing.gml"] = "a" });
        var beta = Mod("beta", new Dictionary<string, object> { ["scripts/thing.gml"] = "b" });

        var diagnosis = ConflictDiagnoser.Diagnose(["scripts/thing.gml"], [alpha, beta]);

        Assert.Multiple(() =>
        {
            Assert.That(diagnosis.Files[0].Outcome, Is.EqualTo(FileOutcome.Unreadable));
            Assert.That(diagnosis.Certain, Is.False);
        });
    }

    [Test]
    public void ShouldSayNothingAboutASingleMod()
    {
        var alpha = Mod("alpha", new Dictionary<string, object> { ["a.toml"] = "x = 1" });

        Assert.That(ConflictDiagnoser.Diagnose(["a.toml"], [alpha]).Verdict,
            Is.EqualTo(DiagnosisVerdict.Unresolved));
    }

    // One unreadable file must not launder the rest into a confident verdict, and must not throw
    // away what could be read either.
    [Test]
    public void ShouldStayUncertainWhenOnlySomeFilesCouldBeRead()
    {
        var alpha = Mod("alpha", new Dictionary<string, object>
        {
            ["fiddle/ok.toml"] = "a = 1",
            ["scripts/thing.gml"] = "a"
        });
        var beta = Mod("beta", new Dictionary<string, object>
        {
            ["fiddle/ok.toml"] = "b = 2",
            ["scripts/thing.gml"] = "b"
        });

        var diagnosis = ConflictDiagnoser.Diagnose(
            ["fiddle/ok.toml", "scripts/thing.gml"], [alpha, beta]);

        Assert.Multiple(() =>
        {
            Assert.That(diagnosis.Certain, Is.False);
            Assert.That(diagnosis.Files, Has.Count.EqualTo(2));
            Assert.That(diagnosis.Files[0].Outcome, Is.EqualTo(FileOutcome.MergesCleanly));
        });
    }
}
