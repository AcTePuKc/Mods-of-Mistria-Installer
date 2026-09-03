using Garethp.ModsOfMistriaInstallerLib;

namespace ModsOfMistriaInstallerLibTests;

// The part of "find a fix" that needs no network: which pages the user is pointed at, and how the
// search that covers everything outside Nexus is phrased.
[TestFixture]
public class ConflictResearchTest
{
    private static readonly ResearchSubject Remind =
        new("Remind Me", 812, "https://www.nexusmods.com/fieldsofmistria/mods/812");

    private static readonly ResearchSubject Gift =
        new("The Perfect Gift", 640, null);

    [Test]
    public async Task ShouldPointAtEachModsBugsPostsAndFiles()
    {
        var result = await ConflictResearch.InvestigateAsync([Remind, Gift], null);

        var urls = result.Links.Select(link => link.Url).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(urls, Does.Contain("https://www.nexusmods.com/fieldsofmistria/mods/812?tab=bugs"));
            Assert.That(urls, Does.Contain("https://www.nexusmods.com/fieldsofmistria/mods/812?tab=posts"));
            Assert.That(urls, Does.Contain("https://www.nexusmods.com/fieldsofmistria/mods/812?tab=files"));
            // A mod with an id but no recorded page URL still gets a page built from the id.
            Assert.That(urls, Does.Contain("https://www.nexusmods.com/fieldsofmistria/mods/640?tab=bugs"));
        });
    }

    // A mod AIM has no Nexus identity for has no page to open, but it must still reach the search.
    [Test]
    public async Task ShouldStillOfferASearchForAModWithNoNexusIdentity()
    {
        var handmade = new ResearchSubject("Hand Made Mod", null, null);

        var result = await ConflictResearch.InvestigateAsync([Remind, handmade], null);

        Assert.Multiple(() =>
        {
            Assert.That(result.Links.Where(link => link.Url.Contains("nexusmods.com/fieldsofmistria/mods")),
                Has.Exactly(3).Items);
            Assert.That(result.Links.Select(link => link.Url), Has.One.Contains("duckduckgo.com"));
        });
    }

    [Test]
    public void ShouldNameEveryModAndTheGameInTheWebSearch()
    {
        var url = ConflictResearch.WebSearchUrl([Remind, Gift]);
        var query = Uri.UnescapeDataString(url["https://duckduckgo.com/?q=".Length..]);

        Assert.Multiple(() =>
        {
            Assert.That(query, Does.Contain("Fields of Mistria"));
            Assert.That(query, Does.Contain("\"Remind Me\""));
            Assert.That(query, Does.Contain("\"The Perfect Gift\""));
            Assert.That(query, Does.Contain("compatibility"));
        });
    }

    // With no API key there is no client, and the window says so rather than pretending it looked.
    [Test]
    public async Task ShouldFindNothingWithoutAnApiClient()
    {
        var result = await ConflictResearch.InvestigateAsync([Remind, Gift], null);

        Assert.That(result.FoundNothing, Is.True);
    }
}
