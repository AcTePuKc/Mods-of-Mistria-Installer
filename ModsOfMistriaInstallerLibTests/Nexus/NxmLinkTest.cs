using Garethp.ModsOfMistriaInstallerLib.Nexus;

namespace ModsOfMistriaInstallerLibTests.Nexus;

// The nxm:// links the Nexus website hands over. They arrive from a browser rather than from our
// own code, so parsing has to reject nonsense with a message instead of throwing.
[TestFixture]
public class NxmLinkTest
{
    private const string FreeUserLink =
        "nxm://fieldsofmistria/mods/78/files/9910?key=abc123&expires=4102444800&user_id=42";

    [Test]
    public void ShouldParseALinkWithADownloadToken()
    {
        Assert.That(NxmLink.TryParse(FreeUserLink, out var link, out var error), Is.True, error);

        Assert.Multiple(() =>
        {
            Assert.That(link!.Game, Is.EqualTo("fieldsofmistria"));
            Assert.That(link.ModId, Is.EqualTo(78));
            Assert.That(link.FileId, Is.EqualTo(9910));
            Assert.That(link.Key, Is.EqualTo("abc123"));
            Assert.That(link.Expires, Is.EqualTo(4102444800));
            Assert.That(link.UserId, Is.EqualTo(42));
            Assert.That(link.HasDownloadToken, Is.True);
            Assert.That(link.IsExpired, Is.False);
            Assert.That(link.IsForMistria(), Is.True);
        });
    }

    [Test]
    public void ShouldParseAPremiumLinkWithoutAToken()
    {
        Assert.That(NxmLink.TryParse("nxm://fieldsofmistria/mods/78/files/9910", out var link, out _), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(link!.HasDownloadToken, Is.False);
            Assert.That(link.Key, Is.Null);
            Assert.That(link.IsExpired, Is.False);
        });
    }

    [Test]
    public void ShouldRecogniseAnExpiredToken()
    {
        NxmLink.TryParse("nxm://fieldsofmistria/mods/78/files/9910?key=abc&expires=1000000000", out var link, out _);

        Assert.That(link!.IsExpired, Is.True);
    }

    [Test]
    public void ShouldKeepLinksForOtherGamesButFlagThem()
    {
        Assert.That(NxmLink.TryParse("nxm://stardewvalley/mods/1/files/2", out var link, out _), Is.True);

        Assert.That(link!.IsForMistria(), Is.False);
    }

    [Test]
    public void ShouldRejectCollectionsWithTheirOwnMessage()
    {
        Assert.That(NxmLink.TryParse(
            "nxm://fieldsofmistria/collections/abc123/revisions/4", out var link, out var error), Is.False);

        Assert.Multiple(() =>
        {
            Assert.That(link, Is.Null);
            Assert.That(error, Does.Contain("collections"));
        });
    }

    [TestCase("", TestName = "empty")]
    [TestCase("https://www.nexusmods.com/fieldsofmistria/mods/78", TestName = "a web link")]
    [TestCase("nxm://fieldsofmistria/mods/78", TestName = "no file part")]
    [TestCase("nxm://fieldsofmistria/mods/abc/files/9910", TestName = "a non-numeric mod id")]
    [TestCase("nxm://fieldsofmistria/mods/0/files/9910", TestName = "a zero mod id")]
    public void ShouldRejectMalformedLinks(string input)
    {
        Assert.That(NxmLink.TryParse(input, out var link, out var error), Is.False);

        Assert.Multiple(() =>
        {
            Assert.That(link, Is.Null);
            Assert.That(error, Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public void ShouldIgnoreUnparseableQueryValuesRatherThanFailing()
    {
        Assert.That(NxmLink.TryParse(
            "nxm://fieldsofmistria/mods/78/files/9910?key=abc&expires=soon&user_id=", out var link, out _), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(link!.Expires, Is.Null);
            Assert.That(link.UserId, Is.Null);
            Assert.That(link.HasDownloadToken, Is.False, "a key without an expiry is not a usable token");
        });
    }

    [Test]
    public void ShouldRecogniseNxmUris()
    {
        Assert.Multiple(() =>
        {
            Assert.That(NxmLink.IsNxmUri(FreeUserLink), Is.True);
            Assert.That(NxmLink.IsNxmUri("NXM://fieldsofmistria/mods/1/files/2"), Is.True);
            Assert.That(NxmLink.IsNxmUri("https://example.com"), Is.False);
            Assert.That(NxmLink.IsNxmUri(null), Is.False);
        });
    }
}
