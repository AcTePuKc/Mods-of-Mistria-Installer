using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Garethp.ModsOfMistriaInstallerLib.Nexus;

/// <summary>
/// Public registration data for AIM's Nexus OAuth client. A client ID identifies the application;
/// it is deliberately not a secret. Until Nexus Mods registers AIM, <see cref="Pending"/> keeps
/// the authorization path unavailable without providing any credential fallback.
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

    public bool IsRegistered => !string.IsNullOrWhiteSpace(ClientId);
}

/// <summary>
/// A one-time OAuth request. The verifier is retained in memory only until the callback code is
/// exchanged; it is never written to settings or logs.
/// </summary>
public sealed record NexusOAuthAuthorizationRequest(Uri AuthorizationUri, string State, string CodeVerifier);

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
