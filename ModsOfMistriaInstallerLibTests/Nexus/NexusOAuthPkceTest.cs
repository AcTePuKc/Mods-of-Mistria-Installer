using System.Net;
using System.Net.Sockets;
using System.Text;
using Garethp.ModsOfMistriaInstallerLib.Nexus;

namespace ModsOfMistriaInstallerLibTests.Nexus;

[TestFixture]
public class NexusOAuthPkceTest
{
    private static readonly NexusOAuthRegistration Registration = new(
        "aim-test-client-id", NexusOAuthRegistration.LoopbackRedirectUri);

    private string _settingsDirectory = "";

    [SetUp]
    public void SetUp()
    {
        _settingsDirectory = Path.Combine(Path.GetTempPath(), $"aim-oauth-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_settingsDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_settingsDirectory)) Directory.Delete(_settingsDirectory, true);
    }

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

    [Test]
    public async Task ShouldCompletePkceSignInThroughTheLoopbackCallback()
    {
        var registration = new NexusOAuthRegistration("aim-test-client-id", NewLoopbackRedirectUri());
        var handler = new TokenResponseHandler();
        var settings = new NexusSettings(_settingsDirectory);
        var service = new NexusOAuthService(settings, registration, new HttpClient(handler));

        var tokens = await service.SignInAsync(async authorizationUri =>
        {
            var query = ParseQuery(authorizationUri);
            using var browser = new HttpClient();
            var callback = new UriBuilder(registration.RedirectUri)
            {
                Query = $"code=one-time-code&state={Uri.EscapeDataString(query["state"])}"
            }.Uri;
            var response = await browser.GetAsync(callback);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        });

        Assert.Multiple(() =>
        {
            Assert.That(tokens.AccessToken, Is.EqualTo("access-token"));
            Assert.That(settings.GetOAuthTokens(), Is.EqualTo(tokens));
            Assert.That(handler.LastForm["grant_type"], Is.EqualTo("authorization_code"));
            Assert.That(handler.LastForm["code"], Is.EqualTo("one-time-code"));
            Assert.That(handler.LastForm["redirect_uri"], Is.EqualTo(registration.RedirectUri.AbsoluteUri));
            Assert.That(handler.LastForm["code_verifier"], Is.Not.Empty);
        });
    }

    [Test]
    public async Task ShouldIgnoreAMismatchedLoopbackStateUntilTheCorrectCallbackArrives()
    {
        var registration = new NexusOAuthRegistration("aim-test-client-id", NewLoopbackRedirectUri());
        var handler = new TokenResponseHandler();
        var service = new NexusOAuthService(new NexusSettings(_settingsDirectory), registration, new HttpClient(handler));

        var tokens = await service.SignInAsync(async authorizationUri =>
        {
            using var browser = new HttpClient();
            var wrong = new UriBuilder(registration.RedirectUri) { Query = "code=attacker-code&state=wrong-state" }.Uri;
            var wrongResponse = await browser.GetAsync(wrong);
            Assert.That(wrongResponse.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

            var query = ParseQuery(authorizationUri);
            var correct = new UriBuilder(registration.RedirectUri)
            {
                Query = $"code=one-time-code&state={Uri.EscapeDataString(query["state"])}"
            }.Uri;
            var correctResponse = await browser.GetAsync(correct);
            Assert.That(correctResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        });

        Assert.Multiple(() =>
        {
            Assert.That(tokens.AccessToken, Is.EqualTo("access-token"));
            Assert.That(handler.CallCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ShouldIgnoreAWrongStateErrorCallbackUntilTheCorrectCallbackArrives()
    {
        var registration = new NexusOAuthRegistration("aim-test-client-id", NewLoopbackRedirectUri());
        var handler = new TokenResponseHandler();
        var service = new NexusOAuthService(new NexusSettings(_settingsDirectory), registration, new HttpClient(handler));

        var tokens = await service.SignInAsync(async authorizationUri =>
        {
            using var browser = new HttpClient();
            var wrong = new UriBuilder(registration.RedirectUri)
            {
                Query = "error=access_denied&state=wrong-state"
            }.Uri;
            var wrongResponse = await browser.GetAsync(wrong);
            Assert.That(wrongResponse.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

            var query = ParseQuery(authorizationUri);
            var correct = new UriBuilder(registration.RedirectUri)
            {
                Query = $"code=one-time-code&state={Uri.EscapeDataString(query["state"])}"
            }.Uri;
            var correctResponse = await browser.GetAsync(correct);
            Assert.That(correctResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        });

        Assert.Multiple(() =>
        {
            Assert.That(tokens.AccessToken, Is.EqualTo("access-token"));
            Assert.That(handler.CallCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void ShouldStopSignInForAValidStateErrorCallback()
    {
        var registration = new NexusOAuthRegistration("aim-test-client-id", NewLoopbackRedirectUri());
        var handler = new TokenResponseHandler();
        var service = new NexusOAuthService(new NexusSettings(_settingsDirectory), registration, new HttpClient(handler));

        var error = Assert.ThrowsAsync<NexusApiException>(async () => await service.SignInAsync(async authorizationUri =>
        {
            var query = ParseQuery(authorizationUri);
            using var browser = new HttpClient();
            var callback = new UriBuilder(registration.RedirectUri)
            {
                Query = $"error=access_denied&state={Uri.EscapeDataString(query["state"])}"
            }.Uri;
            var response = await browser.GetAsync(callback);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        }));

        Assert.Multiple(() =>
        {
            Assert.That(error!.Message, Does.Contain("cancelled or denied"));
            Assert.That(handler.CallCount, Is.Zero);
        });
    }

    [Test]
    public void ShouldCancelSignInWhenTheCallbackDoesNotArrive()
    {
        var registration = new NexusOAuthRegistration("aim-test-client-id", NewLoopbackRedirectUri());
        var service = new NexusOAuthService(new NexusSettings(_settingsDirectory), registration, new HttpClient(new TokenResponseHandler()));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        Assert.That(async () => await service.SignInAsync(_ => Task.CompletedTask, cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public void ShouldPreserveCallerCancellationWhileRefreshingAnExpiredSession()
    {
        var settings = new NexusSettings(_settingsDirectory);
        settings.SetOAuthTokens(new NexusOAuthTokens("access-token", "refresh-token", DateTimeOffset.UtcNow.AddMinutes(-1)));
        var service = new NexusOAuthService(settings, Registration, new HttpClient(new CancellingTokenResponseHandler()));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        Assert.That(async () => await service.GetAccessTokenAsync(cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public async Task ShouldReturnNoTokenWhenRefreshFailsWithoutCancellation()
    {
        var settings = new NexusSettings(_settingsDirectory);
        settings.SetOAuthTokens(new NexusOAuthTokens("access-token", "refresh-token", DateTimeOffset.UtcNow.AddMinutes(-1)));
        var service = new NexusOAuthService(settings, Registration, new HttpClient(new FailedTokenResponseHandler()));

        Assert.That(await service.GetAccessTokenAsync(), Is.Null);
    }

    [Test]
    public void ShouldRejectANonLoopbackCallbackAddress()
    {
        Assert.Throws<ArgumentException>(() => new NexusOAuthLoopbackListener(
            new Uri("http://example.test:17892/callback")));
    }

    private static Dictionary<string, string> ParseQuery(Uri uri) =>
        uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(
                pair => Uri.UnescapeDataString(pair[0]),
                pair => pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : "");

    private static Uri NewLoopbackRedirectUri()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        return new Uri($"http://127.0.0.1:{port}/callback");
    }

    private sealed class TokenResponseHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public Dictionary<string, string> LastForm { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            foreach (var pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split('=', 2);
                LastForm[Uri.UnescapeDataString(parts[0].Replace('+', ' '))] =
                    parts.Length == 2 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : "";
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"access_token\":\"access-token\",\"refresh_token\":\"refresh-token\",\"expires_in\":3600}", Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class CancellingTokenResponseHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable after cancellation.");
        }
    }

    private sealed class FailedTokenResponseHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
    }
}
