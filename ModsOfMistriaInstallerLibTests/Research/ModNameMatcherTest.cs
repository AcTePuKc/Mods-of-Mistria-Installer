using Garethp.ModsOfMistriaInstallerLib.Research;

namespace ModsOfMistriaInstallerLibTests.Research;

// Deciding whether a comment is talking about a particular mod.
//
// The old check was text.Contains(name), which only ever matched when someone typed the mod's Nexus
// title exactly - and almost nobody does. It was simultaneously too strict for a real comment
// thread and too loose for a mod whose title is a common word.
[TestFixture]
public class ModNameMatcherTest
{
    private const string Witchy = "Sushi's Witchy Weapons and Tools";

    [TestCase("Sushi's Witchy Weapons and Tools")]
    [TestCase("witchy weapons and tools")]
    [TestCase("Does this work with Witchy Weapons?")]
    [TestCase("suushiico_witchy_weapons_tools")]
    public void ShouldFindTheModHoweverSomeoneWroteIt(string text)
    {
        Assert.That(ModNameMatcher.Mentions(text, Witchy), Is.True);
    }

    [TestCase("I love the weapons in this game")]
    [TestCase("Great tools, thanks!")]
    [TestCase("Sushi is my favourite food")]
    public void ShouldNotClaimAMentionFromOneCommonWord(string text)
    {
        Assert.That(ModNameMatcher.Mentions(text, Witchy), Is.False);
    }

    // Filler is dropped so it cannot carry a match on its own, and a title made only of filler can
    // never be claimed as mentioned at all.
    [Test]
    public void ShouldIgnoreTheWordsEveryModTitleContains()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ModNameMatcher.DistinctiveWords(Witchy),
                Is.EquivalentTo(new[] { "Sushi", "Witchy", "Weapons", "Tools" }));
            Assert.That(ModNameMatcher.DistinctiveWords("Fields of Mistria Mod Pack"), Is.Empty);
            Assert.That(ModNameMatcher.Mentions("a mod for fields of mistria", "Fields of Mistria Mod"),
                Is.False);
        });
    }

    // A one-word title is genuinely identified by its one word, so the bar drops to it.
    [Test]
    public void ShouldMatchASingleWordTitleOnThatWord()
    {
        Assert.That(ModNameMatcher.Mentions("Chromatic broke for me", "Chromatic"), Is.True);
    }

    // A plural or short inflection counts; a longer word that merely starts the same does not.
    [Test]
    public void ShouldAllowAnInflectionButNotADifferentWord()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ModNameMatcher.Mentions("the witchy weapon and tool set", Witchy), Is.True);
            Assert.That(ModNameMatcher.Mentions("weaponsmithing toolkits", "Weapon Tool"), Is.False);
        });
    }

    // The false claim this rule exists for. "Find a fix" told the user that Effe's Witchy Decor
    // "names Patchless Haunted Attic Set (Replacer)" in a sentence that is about a room in the
    // game: two of the title's four words are ordinary Fields of Mistria vocabulary, and the two
    // that actually identify the mod - "patchless" and "replacer" - appear nowhere in it.
    [Test]
    public void ShouldNotClaimAMentionFromASharedSubjectMatter()
    {
        const string patchless = "Patchless Haunted Attic Set (Replacer)";

        Assert.Multiple(() =>
        {
            Assert.That(ModNameMatcher.Mentions(
                    "Also includes a darker recolour of the Haunted Attic - Dark wallpaper and flooring.",
                    patchless),
                Is.False, "two trailing words of a title are the subject, not the mod");

            Assert.That(ModNameMatcher.Mentions("does the patchless haunted attic set work with this?", patchless),
                Is.True, "the words that identify it still find it");
        });
    }

    // A title's leading possessive is a signature, not a quarter of the name. Counting it as one
    // made "Witchy Decor" look like weak evidence for a five-word title it plainly identifies.
    [Test]
    public void ShouldTreatTheAuthorsNameAsASignature()
    {
        const string effe = "Effe's Witchy Decor - Water Features Compatible";

        Assert.Multiple(() =>
        {
            Assert.That(ModNameMatcher.Mentions("does this work with Effe's Witchy Decor?", effe), Is.True);
            Assert.That(ModNameMatcher.Mentions("the water in this game is lovely", effe), Is.False);
        });
    }

    [Test]
    public void ShouldReportWhichOfSeveralModsWasNamed()
    {
        Assert.That(
            ModNameMatcher.FirstMentioned("crashes with The Perfect Gift", [Witchy, "The Perfect Gift"]),
            Is.EqualTo("The Perfect Gift"));
    }
}
