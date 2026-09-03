using Garethp.ModsOfMistriaInstallerLib.Research;

namespace ModsOfMistriaInstallerLibTests.Research;

// Reading a sentence off a mod page and working out which way it points.
//
// The first test here is the bug this class was written for. "Sushi's Witchy Weapons and Tools"
// (Nexus mod 455) answers the compatibility question outright in its description, and "find a fix"
// reported that it had found nothing - because the old keyword list looked for "compatible",
// "compatibility" and "incompatible", and the author wrote the plurals. None of the three words is
// a substring of "compatibilities" or "incompatibilities", so the one sentence on the page worth
// reading was invisible.
[TestFixture]
public class CompatibilityLanguageTest
{
    // Verbatim from https://www.nexusmods.com/fieldsofmistria/mods/455
    private const string Mod455 =
        "No known compatibilities / incompatibilities at this time. It is a standalone mod";

    [Test]
    public void ShouldReadThePluralFormThatTheOldKeywordListMissed()
    {
        var signal = CompatibilityLanguage.Classify(Mod455);

        Assert.That(signal, Is.Not.Null, "the sentence that started all this must be found at all");
        Assert.That(signal!.Polarity, Is.EqualTo(Polarity.Clearance),
            "\"no known incompatibilities\" is evidence against the conflict, not merely a mention");
    }

    [TestCase("compatible")]
    [TestCase("compatibility")]
    [TestCase("compatibilities")]
    [TestCase("Compatibilities")]
    [TestCase("incompatible")]
    [TestCase("incompatibilities")]
    public void ShouldMatchEveryInflectionOfTheWord(string word)
    {
        Assert.That(CompatibilityLanguage.Classify($"A note about {word} here."), Is.Not.Null);
    }

    // The direction is the whole point: an author clearing a pairing and one condemning it used to
    // be reported identically, so the user had to read every quote to find out which they had.
    [TestCase("This mod is incompatible with The Perfect Gift.", Polarity.Blocker)]
    [TestCase("This is not compatible with Remind Me.", Polarity.Blocker)]
    [TestCase("Do not use with Remind Me.", Polarity.Blocker)]
    [TestCase("It conflicts with Remind Me.", Polarity.Blocker)]
    [TestCase("No known issues at this time", Polarity.Clearance)]
    [TestCase("It is a standalone mod", Polarity.Clearance)]
    [TestCase("Fully compatible with Remind Me.", Polarity.Clearance)]
    [TestCase("Works fine with The Perfect Gift.", Polarity.Clearance)]
    [TestCase("Load this after The Perfect Gift.", Polarity.Caution)]
    [TestCase("Use the compatibility patch below.", Polarity.Caution)]
    [TestCase("This Mod Replaces: Bakery Dessert Case, Basic Chair, Explorer Lamp.", Polarity.Caution)]
    public void ShouldSayWhichWayASentencePoints(string sentence, Polarity expected)
    {
        Assert.That(CompatibilityLanguage.Classify(sentence)?.Polarity, Is.EqualTo(expected));
    }

    // Sentences seen on real mod pages that the researcher reported and should not have. Each was
    // a keyword firing on text that has nothing to do with the two mods being compared.
    [TestCase("First up is my old patchless version, more hopefully coming soon?",
        TestName = "\"patchless\" is not a patch")]
    [TestCase("V1.2 - Updated for the v0.11.7 patch V1.1 - Updated for the v0.11.6 patch Adds 36 new items.",
        TestName = "a game version patch is not a compatibility patch")]
    [TestCase("Requires MOMI to be installed.",
        TestName = "every mod requires MOMI, so saying so is not a finding")]
    [TestCase("This mod is a dependency of nothing in particular.",
        TestName = "a bare dependency mention is not a finding")]
    public void ShouldNotReportTextThatIsNotAboutCompatibility(string sentence)
    {
        Assert.That(CompatibilityLanguage.Classify(sentence), Is.Null);
    }

    // The counterpart: a patch mention only counts when something makes it a patch between mods.
    [TestCase("Use the compatibility patch below.")]
    [TestCase("There is a patch for Remind Me.")]
    [TestCase("Download the patch to make them work together.")]
    [TestCase("A patch exists on the files tab.")]
    public void ShouldStillReportARealCompatibilityPatch(string sentence)
    {
        Assert.That(CompatibilityLanguage.Classify(sentence)?.Polarity, Is.EqualTo(Polarity.Caution));
    }

    // Two sentences that contain the same words as a clearance but mean the opposite. Both used to
    // be a coin toss and both are now settled by rule order.
    [Test]
    public void ShouldNotReadTheAbsenceOfAFixAsTheAbsenceOfAProblem()
    {
        Assert.That(CompatibilityLanguage.Classify("There is no compatibility patch yet.")?.Polarity,
            Is.Not.EqualTo(Polarity.Clearance));
    }

    [Test]
    public void ShouldNotReadANegatedStandaloneAsAClearance()
    {
        Assert.That(CompatibilityLanguage.Classify("This is not a standalone mod, it needs Foo.")?.Polarity,
            Is.Not.EqualTo(Polarity.Clearance));
    }

    [Test]
    public void ShouldIgnoreASentenceThatSaysNothingAboutCompatibility()
    {
        Assert.That(CompatibilityLanguage.Classify("I love this mod so much, thank you!"), Is.Null);
    }

    // A comment thread is mostly praise and bug reports that have nothing to do with any other mod,
    // so the bar for keeping a post is higher than for keeping a line of the author's description.
    [Test]
    public void ShouldHoldCommentsToAHigherBarThanDescriptions()
    {
        const string vague = "It replaces the sprite for the axe.";

        Assert.Multiple(() =>
        {
            Assert.That(CompatibilityLanguage.Classify(vague), Is.Not.Null,
                "in a description this is worth showing");
            Assert.That(CompatibilityLanguage.ClassifyDiscussion(vague), Is.Not.Null,
                "and it states a mechanism, so it survives the stricter bar too");
            Assert.That(CompatibilityLanguage.ClassifyDiscussion("Anyone know about compatibility?"), Is.Null,
                "but a question that merely mentions the subject does not");
        });
    }
}
