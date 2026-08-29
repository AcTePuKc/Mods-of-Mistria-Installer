using Garethp.ModsOfMistriaInstallerLib.Nexus;

namespace ModsOfMistriaInstallerLibTests.Nexus;

[TestFixture]
public class NexusOAuthPkceTest
{
    private static readonly NexusOAuthRegistration Registration = new(
        "aim-test-client-id", NexusOAuthRegistration.LoopbackRedirectUri);

    [Test]
    public void ShouldRefuseToCreateARequestBeforeNexusRegistersAim()
    {
        Assert.That(
            () => NexusOAuthPkce.CreateAuthorizationRequest(NexusOAuthRegistration.Pending),
            Throws.InvalidOperationException);
    }

    [Test]
    public void ShouldBuildAS256AuthorizationCodeRequestForAPublicClient()
    {
        var request = NexusOAuthPkce.CreateAuthorizationRequest(Registration);
        var query = ParseQuery(request.AuthorizationUri);

        Assert.Multiple(() =>
        {
            Assert.That(request.AuthorizationUri.GetLeftPart(UriPartial.Path),
                Is.EqualTo("https://users.nexusmods.com/oauth/authorize"));
            Assert.That(query["client_id"], Is.EqualTo("aim-test-client-id"));
            Assert.That(query["response_type"], Is.EqualTo("code"));
            Assert.That(query["redirect_uri"], Is.EqualTo(NexusOAuthRegistration.LoopbackRedirectUri.AbsoluteUri));
            Assert.That(query["code_challenge_method"], Is.EqualTo("S256"));
            Assert.That(query["state"], Is.EqualTo(request.State));
            Assert.That(query["code_challenge"], Is.Not.EqualTo(request.CodeVerifier));
        });
    }

    [Test]
    public void ShouldGenerateDistinctUrlSafeStateAndVerifierForEachAttempt()
    {
        var first = NexusOAuthPkce.CreateAuthorizationRequest(Registration);
        var second = NexusOAuthPkce.CreateAuthorizationRequest(Registration);

        Assert.Multiple(() =>
        {
            Assert.That(first.State, Is.Not.EqualTo(second.State));
            Assert.That(first.CodeVerifier, Is.Not.EqualTo(second.CodeVerifier));
            Assert.That(first.State, Does.Match("^[A-Za-z0-9_-]+$"));
            Assert.That(first.CodeVerifier, Does.Match("^[A-Za-z0-9_-]+$"));
            Assert.That(first.CodeVerifier.Length, Is.GreaterThanOrEqualTo(43));
        });
    }

    private static Dictionary<string, string> ParseQuery(Uri uri) =>
        uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(
                pair => Uri.UnescapeDataString(pair[0]),
                pair => pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : "");
}
