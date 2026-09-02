using System.Net;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Garethp.ModsOfMistriaInstallerLib.Nexus;

/// <summary>
/// Public registration data for AIM's Nexus OAuth client. A client ID identifies the application;
/// it is deliberately not a secret. <see cref="Production"/> is the public registration supplied
/// by Nexus Mods; <see cref="Pending"/> remains available for tests and source-review scenarios.
/// </summary>
public sealed record NexusOAuthRegistration(string ClientId, Uri RedirectUri)
{
    /// <summary>
    /// The loopback callback that will be submitted to Nexus when AIM is registered. Loopback is
    /// used only to deliver the browser's authorization response to the locally running app.
    /// </summary>
    public static readonly Uri LoopbackRedirectUri = new("http://127.0.0.1:17892/callback");

    /// <summary>
    /// Source-review default. The actual public client ID is supplied by Nexus only after they
    /// approve and register the application.
    /// </summary>
    public static NexusOAuthRegistration Pending { get; } = new("", LoopbackRedirectUri);

    /// <summary>
    /// AIM's public Nexus OAuth registration. Native desktop applications use PKCE and must not
    /// rely on a client secret embedded in the distributed executable.
    /// </summary>
    public static NexusOAuthRegistration Production { get; } =
        new("alternative_installer_for_mistria", LoopbackRedirectUri);

    public bool IsRegistered => !string.IsNullOrWhiteSpace(ClientId);
}

/// <summary>
/// A one-time OAuth request. The verifier is retained in memory only until the callback code is
/// exchanged; it is never written to settings or logs.
/// </summary>
public sealed record NexusOAuthAuthorizationRequest(Uri AuthorizationUri, string State, string CodeVerifier);

/// <summary>
/// The only values AIM accepts from the loopback redirect. The authorization code is deliberately
/// kept in memory and is never written to settings or diagnostic logs.
/// </summary>
public sealed record NexusOAuthCallback(string State, string AuthorizationCode);

/// <summary>
/// OAuth tokens obtained through the Authorization Code + PKCE flow. They are stored only by
/// <see cref="NexusSettings"/>; the verifier and authorization state are deliberately excluded.
/// </summary>
public sealed record NexusOAuthTokens(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt)
{
    public bool IsUsable => !string.IsNullOrWhiteSpace(AccessToken);
    public bool NeedsRefresh(TimeSpan? safetyWindow = null) =>
        ExpiresAt <= DateTimeOffset.UtcNow.Add(safetyWindow ?? TimeSpan.FromMinutes(1));
}

/// <summary>
/// Performs the token part of Nexus OAuth for a public desktop client. It has no client secret:
/// PKCE proves that the local AIM process that started the browser request is the one exchanging
/// the returned authorization code.
/// </summary>
public sealed class NexusOAuthService(NexusSettings settings, NexusOAuthRegistration registration, HttpClient? http = null)
{
    private static readonly Uri TokenEndpoint = new("https://users.nexusmods.com/oauth/token");
    private readonly HttpClient _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

    public NexusOAuthRegistration Registration { get; } = registration;

    public bool IsRegistered => Registration.IsRegistered;
    public bool HasSession => settings.HasOAuthTokens();

    public NexusOAuthAuthorizationRequest CreateAuthorizationRequest() =>
        NexusOAuthPkce.CreateAuthorizationRequest(Registration);

    /// <summary>
    /// Completes the browser portion of the Authorization Code + PKCE flow. The listener is bound
    /// to the registered loopback redirect before the browser opens, so another local process
    /// cannot win a callback race. The callback state is verified here and again by the token
    /// exchange boundary.
    /// </summary>
    public async Task<NexusOAuthTokens> SignInAsync(
        Func<Uri, Task> openBrowser,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(openBrowser);

        var request = CreateAuthorizationRequest();
        using var listener = new NexusOAuthLoopbackListener(Registration.RedirectUri);
        listener.Start();

        // Begin accepting before opening the browser. A browser (and the focused regression test)
        // waits for the callback response, so waiting only after its launch task returns would
        // deadlock the sign-in flow.
        var callbackTask = listener.WaitForCallbackAsync(request.State, ct);
        await openBrowser(request.AuthorizationUri);
        var callback = await callbackTask;
        return await ExchangeCodeAsync(request, callback.State, callback.AuthorizationCode, ct);
    }

    /// <summary>Returns a current access token, refreshing it with the saved refresh token first.</summary>
    public async Task<string?> GetAccessTokenAsync(CancellationToken ct = default)
    {
        var tokens = settings.GetOAuthTokens();
        if (tokens is null || !tokens.IsUsable) return null;
        if (!tokens.NeedsRefresh()) return tokens.AccessToken;

        try
        {
            return (await RefreshAsync(tokens, ct)).AccessToken;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            Logger.Log($"Could not refresh the Nexus OAuth session: {e.Message}");
            return null;
        }
    }

    /// <summary>Exchanges a state-validated loopback callback code for a local OAuth session.</summary>
    public async Task<NexusOAuthTokens> ExchangeCodeAsync(
        NexusOAuthAuthorizationRequest request, string callbackState, string authorizationCode, CancellationToken ct = default)
    {
        if (!string.Equals(request.State, callbackState, StringComparison.Ordinal))
            throw new NexusApiException("Nexus sign-in was cancelled because the callback state did not match.");
        if (string.IsNullOrWhiteSpace(authorizationCode))
            throw new NexusApiException("Nexus did not return an authorization code.");

        var tokens = await RequestTokensAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = Registration.ClientId,
            ["redirect_uri"] = Registration.RedirectUri.AbsoluteUri,
            ["code"] = authorizationCode,
            ["code_verifier"] = request.CodeVerifier
        }, ct);
        settings.SetOAuthTokens(tokens);
        return tokens;
    }

    public void Disconnect() => settings.SetOAuthTokens(null);

    private async Task<NexusOAuthTokens> RefreshAsync(NexusOAuthTokens current, CancellationToken ct)
    {
        if (!IsRegistered || string.IsNullOrWhiteSpace(current.RefreshToken))
            throw new NexusApiException("The Nexus account session needs to be connected again.");

        var refreshed = await RequestTokensAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = Registration.ClientId,
            ["refresh_token"] = current.RefreshToken
        }, ct);
        settings.SetOAuthTokens(refreshed);
        return refreshed;
    }

    private async Task<NexusOAuthTokens> RequestTokensAsync(Dictionary<string, string> values, CancellationToken ct)
    {
        if (!IsRegistered)
            throw new NexusApiException("Nexus OAuth registration for AIM is still pending.");

        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(values)
        };
        using var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new NexusApiException("Nexus could not complete account sign-in.", response.StatusCode);

        try
        {
            var json = JObject.Parse(body);
            var accessToken = json.Value<string>("access_token");
            var refreshToken = json.Value<string>("refresh_token");
            var expiresIn = json.Value<long?>("expires_in") ?? 3600;
            if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(refreshToken))
                throw new NexusApiException("Nexus returned an incomplete account session.");

            return new NexusOAuthTokens(accessToken, refreshToken,
                DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, expiresIn)));
        }
        catch (NexusApiException)
        {
            throw;
        }
        catch (Exception e)
        {
            throw new NexusApiException("Could not read the Nexus account response.", response.StatusCode, e);
        }
    }
}

/// <summary>
/// Receives one OAuth redirect on the IPv4 loopback interface. It accepts only the exact callback
/// path registered for AIM, then closes immediately; it is not a general local web server.
/// </summary>
public sealed class NexusOAuthLoopbackListener : IDisposable
{
    private readonly Uri _redirectUri;
    private readonly HttpListener _listener = new();
    private bool _started;

    public NexusOAuthLoopbackListener(Uri redirectUri)
    {
        ArgumentNullException.ThrowIfNull(redirectUri);
        if (!redirectUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
            !IPAddress.TryParse(redirectUri.Host, out var address) ||
            !IPAddress.IsLoopback(address) ||
            redirectUri.Port is <= 0 or > 65535)
            throw new ArgumentException("The Nexus OAuth redirect must be a loopback HTTP address.", nameof(redirectUri));

        _redirectUri = redirectUri;
        _listener.Prefixes.Add(new UriBuilder(redirectUri.Scheme, redirectUri.Host, redirectUri.Port, "/").Uri.AbsoluteUri);
    }

    public void Start()
    {
        if (_started) throw new InvalidOperationException("The Nexus OAuth callback listener has already started.");
        _listener.Start();
        _started = true;
    }

    public async Task<NexusOAuthCallback> WaitForCallbackAsync(string expectedState, CancellationToken ct = default)
    {
        if (!_started) throw new InvalidOperationException("Start the Nexus OAuth callback listener before waiting for a callback.");
        if (string.IsNullOrWhiteSpace(expectedState)) throw new ArgumentException("An OAuth state value is required.", nameof(expectedState));

        while (true)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().WaitAsync(ct);
            }
            catch (ObjectDisposedException) when (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }

            if (!string.Equals(context.Request.Url?.AbsolutePath, _redirectUri.AbsolutePath, StringComparison.Ordinal))
            {
                await RespondAsync(context.Response, HttpStatusCode.NotFound, "AIM is waiting for its Nexus sign-in callback.");
                continue;
            }

            var state = GetQueryValue(context.Request.Url, "state");
            var code = GetQueryValue(context.Request.Url, "code");
            var error = GetQueryValue(context.Request.Url, "error");

            if (!FixedTimeEquals(expectedState, state))
            {
                await RespondAsync(context.Response, HttpStatusCode.BadRequest, "The Nexus sign-in response did not match this AIM session.");
                // A different local process can reach the loopback listener too. Reject its
                // callback, but keep waiting: it must not be able to cancel the real browser
                // response merely by sending a request with a random state value.
                continue;
            }

            if (!string.IsNullOrWhiteSpace(error))
            {
                await RespondAsync(context.Response, HttpStatusCode.BadRequest, "Nexus sign-in was cancelled. You can return to AIM.");
                throw new NexusApiException("Nexus sign-in was cancelled or denied by the browser.");
            }

            if (string.IsNullOrWhiteSpace(code))
            {
                await RespondAsync(context.Response, HttpStatusCode.BadRequest, "Nexus did not return an authorization code.");
                throw new NexusApiException("Nexus did not return an authorization code.");
            }

            await RespondAsync(context.Response, HttpStatusCode.OK, "Nexus sign-in completed. You can return to AIM.");
            return new NexusOAuthCallback(state!, code);
        }
    }

    public void Dispose()
    {
        _listener.Close();
    }

    private static string? GetQueryValue(Uri? uri, string name)
    {
        if (uri is null) return null;
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 0 || !string.Equals(Uri.UnescapeDataString(parts[0]), name, StringComparison.Ordinal)) continue;
            return parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : "";
        }

        return null;
    }

    private static bool FixedTimeEquals(string expected, string? actual)
    {
        if (actual is null) return false;
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(actual);
        return expectedBytes.Length == actualBytes.Length && CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    private static async Task RespondAsync(HttpListenerResponse response, HttpStatusCode status, string message)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        response.StatusCode = (int)status;
        response.ContentType = "text/plain; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }
}

/// <summary>
/// Builds Authorization Code + PKCE requests for AIM, a public desktop client. This is deliberately
/// independent from HTTP, browser and UI code so the cryptographic request contract is testable.
/// </summary>
public static class NexusOAuthPkce
{
    private const string AuthorizeEndpoint = "https://users.nexusmods.com/oauth/authorize";

    public static NexusOAuthAuthorizationRequest CreateAuthorizationRequest(NexusOAuthRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        if (!registration.IsRegistered)
            throw new InvalidOperationException("AIM has not been registered as a Nexus OAuth application yet.");

        var verifier = RandomUrlSafeValue(64);
        var state = RandomUrlSafeValue(32);
        var challenge = ToBase64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        var query = new Dictionary<string, string>
        {
            ["client_id"] = registration.ClientId,
            ["response_type"] = "code",
            ["scope"] = "",
            ["redirect_uri"] = registration.RedirectUri.AbsoluteUri,
            ["state"] = state,
            ["code_challenge_method"] = "S256",
            ["code_challenge"] = challenge
        };

        var authorizationUri = new UriBuilder(AuthorizeEndpoint)
        {
            Query = string.Join("&", query.Select(pair =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"))
        }.Uri;

        return new NexusOAuthAuthorizationRequest(authorizationUri, state, verifier);
    }

    private static string RandomUrlSafeValue(int byteCount) =>
        ToBase64Url(RandomNumberGenerator.GetBytes(byteCount));

    private static string ToBase64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
