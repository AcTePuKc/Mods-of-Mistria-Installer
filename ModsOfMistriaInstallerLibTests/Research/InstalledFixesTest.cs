using Garethp.ModsOfMistriaInstallerLib;
using Garethp.ModsOfMistriaInstallerLib.ModTypes;
using Garethp.ModsOfMistriaInstallerLib.Research;
using ModsOfMistriaInstallerLibTests.Fixtures;

namespace ModsOfMistriaInstallerLibTests.Research;

// Checking the mod list before offering to download anything.
//
// The bug these tests exist for: AIM would find a compatibility patch on Nexus, present it as the
// fix, and offer to install it - to a user who had installed it months earlier and whose setup was
// working *because* of it. The point is not only that the offer was redundant. It is that the patch
// being installed is often the reason the conflict is harmless, and AIM was discarding that fact
// and then asking the user to judge the conflict without it.
[TestFixture]
public class InstalledFixesTest
{
    private const string March = "March Expanded";
    private const string Overhaul = "ATDs Farmer Character Overhaul";

    private static readonly string[] Shared =
    [
        "images/replace/spr_portrait_march.png",
        "images/replace/spr_portrait_ari.png",
        "images/replace/spr_portrait_farmer.png",
        "fiddle/portraits.toml"
    ];

    private static MockMod Mod(string name, params string[] files) =>
        new(files.ToList())
        {
            Id = name.ToLowerInvariant().Replace(' ', '_'),
            Name = name,
            DirName = name.ToLowerInvariant().Replace(' ', '_')
        };

    private static InstalledModView View(IMod mod, int position, bool enabled = true) =>
        new(mod, position, enabled);

    /// <summary>The optional compatibility file on March Expanded's own page.</summary>
    private static PatchCandidate PortraitPatch => new(
        4211,
        "March Expanded — Portrait Compatibility Patch",
        "https://www.nexusmods.com/fieldsofmistria/mods/4211?tab=files&file_id=90210",
        "An optional file on March Expanded's own page.",
        PatchConfidence.OptionalFile) { FileId = 90210 };

    // ── The one from the screenshot ──────────────────────────────────────────────

    // The exact case: the patch is an optional file, it is installed, and AIM was still offering
    // to download it.
    [Test]
    public void ShouldRecogniseThePatchItWasAboutToOffer()
    {
        var march = Mod(March);
        var overhaul = Mod(Overhaul);
        var patch = Mod("March Expanded Portrait Compatibility Patch");

        var found = InstalledFixScanner.Scan(
            [View(march, 0), View(overhaul, 1), View(patch, 2) with { NexusModId = 4211, NexusFileId = 90210 }],
            [march, overhaul],
            Shared,
            [PortraitPatch]);

        Assert.Multiple(() =>
        {
            Assert.That(found, Has.Count.EqualTo(1));
            Assert.That(found[0].Evidence, Is.EqualTo(FixEvidence.SamePatch));
            Assert.That(found[0].Supersedes, Is.EqualTo(PortraitPatch));
            Assert.That(found[0].Effective, Is.True);
        });
    }

    // And having recognised it, the planner must stop offering it - and offer to close the issue
    // naming the patch instead.
    [Test]
    public void ShouldNotOfferToInstallWhatIsAlreadyInstalled()
    {
        var march = Mod(March);
        var overhaul = Mod(Overhaul);
        var patch = Mod("March Expanded Portrait Compatibility Patch");

        var found = InstalledFixScanner.Scan(
            [View(march, 0), View(overhaul, 1), View(patch, 2) with { NexusModId = 4211, NexusFileId = 90210 }],
            [march, overhaul],
            Shared,
            [PortraitPatch]);

        var plans = FixPlanner.Plan(
            ConflictDiagnosis.Inconclusive("not the point of this test"),
            [PortraitPatch],
            [(march.GetId(), March), (overhaul.GetId(), Overhaul)],
            found);

        Assert.Multiple(() =>
        {
            Assert.That(plans.Select(plan => plan.Kind), Has.None.EqualTo(FixKind.InstallPatch));
            Assert.That(plans[0].Kind, Is.EqualTo(FixKind.AlreadyFixed));
            Assert.That(plans[0].Title, Does.Contain("March Expanded Portrait Compatibility Patch"));
        });
    }

    // The reason the file id is checked at all. Every optional download on March Expanded's page
    // carries March Expanded's mod id, so matching the mod id alone would report a completely
    // different optional file - an alternate colour set, say - as being the portrait patch.
    [Test]
    public void ShouldNotMistakeASiblingOptionalFileForThePatch()
    {
        var march = Mod(March);
        var overhaul = Mod(Overhaul);
        var altColours = Mod("Muted Colours");

        var found = InstalledFixScanner.Scan(
            [View(march, 0), View(overhaul, 1), View(altColours, 2) with { NexusModId = 4211, NexusFileId = 88000 }],
            [march, overhaul],
            Shared,
            [PortraitPatch]);

        Assert.That(found, Is.Empty);
    }

    // The false positive the distinguishing-word rule exists for.
    //
    // "March Expanded Recolours" and "March Expanded Portrait Patch" share two words, which is
    // enough for a name match - but both of those words come from the mod they are both add-ons
    // to, so they say nothing about whether either is the other. The only word that could identify
    // the patch is "Portrait", and the recolour does not have it. Getting this wrong would suppress
    // the download offer and leave the user with an unfixed conflict and no way to find that out.
    [Test]
    public void ShouldNotMistakeAnAddOnSharingTheModsNameForThePatch()
    {
        var march = Mod(March);
        var overhaul = Mod(Overhaul);
        var addon = Mod("March Expanded Recolours");

        var borrowed = new PatchCandidate(
            4211, "March Expanded Portrait Patch", "https://example.invalid/4211",
            "An optional file.", PatchConfidence.OptionalFile) { FileId = 90210 };

        var found = InstalledFixScanner.Scan(
            [View(march, 0), View(overhaul, 1), View(addon, 2)],
            [march, overhaul],
            Shared,
            [borrowed]);

        Assert.That(found, Is.Empty);
    }

    // A patch published as its own mod has no file id, so the mod id settles it.
    [Test]
    public void ShouldMatchAStandalonePatchOnItsModIdAlone()
    {
        var march = Mod(March);
        var overhaul = Mod(Overhaul);
        var patch = Mod("Bridge");

        var standalone = new PatchCandidate(
            5000, "March x ATD Compatibility", "https://example.invalid/5000",
            "Linked from both pages.", PatchConfidence.LinkedFromBoth);

        var found = InstalledFixScanner.Scan(
            [View(march, 0), View(overhaul, 1), View(patch, 2) with { NexusModId = 5000, NexusFileId = 1 }],
            [march, overhaul],
            Shared,
            [standalone]);

        Assert.That(found.Single().Evidence, Is.EqualTo(FixEvidence.SamePatch));
    }

    // ── Patches with no Nexus provenance ─────────────────────────────────────────

    // Installed by hand from a Discord zip: no ids to match, but the name is the name.
    [Test]
    public void ShouldRecogniseAPatchInstalledByHandFromItsName()
    {
        var march = Mod(March);
        var overhaul = Mod(Overhaul);
        var patch = Mod("march expanded portrait compatibility");

        var found = InstalledFixScanner.Scan(
            [View(march, 0), View(overhaul, 1), View(patch, 2)],
            [march, overhaul],
            Shared,
            [PortraitPatch]);

        Assert.That(found.Single().Evidence, Is.EqualTo(FixEvidence.NamedLikeThePatch));
    }

    // The signal that survives when the research found nothing at all - no key, no network, or a
    // patch that was never published on Nexus. Declaring both mods as requirements is what a
    // compatibility patch does.
    [Test]
    public void ShouldRecogniseAPatchThatRequiresBothMods()
    {
        var march = Mod(March);
        var overhaul = Mod(Overhaul);

        var patch = new MockMod(new List<string>())
        {
            Id = "someone.bridge",
            Name = "Portrait Bridge",
            DirName = "portrait_bridge",
            Requirements =
            [
                new ModRequirement(March, "someone"),
                new ModRequirement(Overhaul, "atd")
            ]
        };

        var found = InstalledFixScanner.Scan(
            [View(march, 0), View(overhaul, 1), View(patch, 2)],
            [march, overhaul],
            Shared,
            []);

        Assert.Multiple(() =>
        {
            Assert.That(found.Single().Evidence, Is.EqualTo(FixEvidence.BridgesBothMods));
            Assert.That(found.Single().Why, Does.Contain("requirements"));
        });
    }

    // Requiring only one of them is just a dependency, which most mods have.
    [Test]
    public void ShouldNotCallAPlainDependencyAPatch()
    {
        var march = Mod(March);
        var overhaul = Mod(Overhaul);

        var addon = new MockMod(new List<string>())
        {
            Id = "someone.addon",
            Name = "More Hairstyles",
            DirName = "more_hairstyles",
            Requirements = [new ModRequirement(March, "someone")]
        };

        var found = InstalledFixScanner.Scan(
            [View(march, 0), View(overhaul, 1), View(addon, 2)],
            [march, overhaul],
            Shared,
            []);

        Assert.That(found, Is.Empty);
    }

    // ── A third mod already in the argument ──────────────────────────────────────

    [Test]
    public void ShouldReportAThirdModWritingTheSameFiles()
    {
        var march = Mod(March, Shared);
        var overhaul = Mod(Overhaul, Shared);
        var third = Mod("Portrait Recolours", Shared[0], Shared[1]);

        var found = InstalledFixScanner.Scan(
            [View(march, 0), View(overhaul, 1), View(third, 2)],
            [march, overhaul],
            Shared,
            []);

        Assert.Multiple(() =>
        {
            Assert.That(found.Single().Evidence, Is.EqualTo(FixEvidence.WritesTheSameFiles));
            Assert.That(found.Single().SharedFiles, Has.Count.EqualTo(2));
        });
    }

    // One file out of four is a coincidence. Interrupting for it would make the section noise, and
    // a section that is usually noise stops being read.
    [Test]
    public void ShouldStayQuietAboutAGlancingOverlap()
    {
        var march = Mod(March, Shared);
        var overhaul = Mod(Overhaul, Shared);
        var unrelated = Mod("Something Else", Shared[0]);

        var found = InstalledFixScanner.Scan(
            [View(march, 0), View(overhaul, 1), View(unrelated, 2)],
            [march, overhaul],
            Shared,
            []);

        Assert.That(found, Is.Empty);
    }

    // A third mod touching the files has promised nothing, so it is reported but never allowed to
    // close the issue on its own.
    [Test]
    public void ShouldNotCloseTheIssueOnAThirdModsBehalf()
    {
        var march = Mod(March, Shared);
        var overhaul = Mod(Overhaul, Shared);
        var third = Mod("Portrait Recolours", Shared);

        var found = InstalledFixScanner.Scan(
            [View(march, 0), View(overhaul, 1), View(third, 2)],
            [march, overhaul],
            Shared,
            []);

        var plans = FixPlanner.Plan(
            ConflictDiagnosis.Inconclusive("unread"),
            [],
            [(march.GetId(), March), (overhaul.GetId(), Overhaul)],
            found);

        Assert.That(plans.Select(plan => plan.Kind), Has.None.EqualTo(FixKind.AlreadyFixed));
    }

    // ── Installed, but not doing anything ────────────────────────────────────────

    // A patch that loads before the mods it patches is overwritten by them. From the game's side
    // that is identical to not having it, and it is the failure mode a user cannot see.
    [Test]
    public void ShouldNoticeAPatchThatLoadsTooEarly()
    {
        var march = Mod(March);
        var overhaul = Mod(Overhaul);
        var patch = Mod("March Expanded Portrait Compatibility Patch");

        var found = InstalledFixScanner.Scan(
            [View(patch, 0) with { NexusModId = 4211, NexusFileId = 90210 }, View(march, 1), View(overhaul, 2)],
            [march, overhaul],
            Shared,
            [PortraitPatch]);

        Assert.Multiple(() =>
        {
            Assert.That(found.Single().LoadsLast, Is.False);
            // Switched on, so position is the only thing wrong with it - which is exactly the case
            // the user cannot see from the mod list.
            Assert.That(found.Single().Enabled, Is.True);
        });
    }

    [Test]
    public void ShouldOfferToTurnOnAndRepositionAPatchThatIsNotWorking()
    {
        var march = Mod(March);
        var overhaul = Mod(Overhaul);
        var patch = Mod("March Expanded Portrait Compatibility Patch");

        var found = InstalledFixScanner.Scan(
            [View(patch, 0, enabled: false) with { NexusModId = 4211, NexusFileId = 90210 },
                View(march, 1), View(overhaul, 2)],
            [march, overhaul],
            Shared,
            [PortraitPatch]);

        var plans = FixPlanner.Plan(
            ConflictDiagnosis.Inconclusive("unread"),
            [PortraitPatch],
            [(march.GetId(), March), (overhaul.GetId(), Overhaul)],
            found);

        Assert.Multiple(() =>
        {
            Assert.That(plans[0].Kind, Is.EqualTo(FixKind.UseExistingFix));
            Assert.That(plans[0].TargetModId, Is.EqualTo(patch.GetId()));
            // Still nothing to download: they have it, it just is not switched on.
            Assert.That(plans.Select(plan => plan.Kind), Has.None.EqualTo(FixKind.InstallPatch));
        });
    }

    // ── Not saying anything twice ────────────────────────────────────────────────

    // "Mark this as not an issue" and "you already have the patch" are the same button. Offering
    // both makes the user choose between two identical outcomes on different reasoning.
    [Test]
    public void ShouldNotOfferTwoWaysToCloseTheSameIssue()
    {
        var march = Mod(March);
        var overhaul = Mod(Overhaul);
        var patch = Mod("March Expanded Portrait Compatibility Patch");

        var found = InstalledFixScanner.Scan(
            [View(march, 0), View(overhaul, 1), View(patch, 2) with { NexusModId = 4211, NexusFileId = 90210 }],
            [march, overhaul],
            Shared,
            [PortraitPatch]);

        var harmless = new ConflictDiagnosis(
            DiagnosisVerdict.Harmless, true, "Nothing is lost.", [], []);

        var plans = FixPlanner.Plan(
            harmless, [PortraitPatch],
            [(march.GetId(), March), (overhaul.GetId(), Overhaul)],
            found);

        Assert.That(plans.Select(plan => plan.Kind), Has.None.EqualTo(FixKind.CloseAsHarmless));
    }

    // The mods in the conflict are not candidates for fixing it, however they are named.
    [Test]
    public void ShouldNeverReportTheConflictingModsThemselves()
    {
        var march = Mod(March, Shared);
        var overhaul = Mod(Overhaul, Shared);

        var found = InstalledFixScanner.Scan(
            [View(march, 0), View(overhaul, 1)], [march, overhaul], Shared, []);

        Assert.That(found, Is.Empty);
    }

    // With nothing scanned the planner behaves exactly as it did before it knew to look, which is
    // what keeps the old callers and the no-context path honest.
    [Test]
    public void ShouldPlanAsBeforeWhenNothingWasScanned()
    {
        var plans = FixPlanner.Plan(
            ConflictDiagnosis.Inconclusive("unread"),
            [PortraitPatch],
            [("a", "Alpha"), ("b", "Beta")]);

        Assert.That(plans[0].Kind, Is.EqualTo(FixKind.InstallPatch));
    }
}
