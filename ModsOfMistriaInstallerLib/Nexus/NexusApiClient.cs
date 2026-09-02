using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using Garethp.ModsOfMistriaInstallerLib.Lang;
using Newtonsoft.Json.Linq;

namespace Garethp.ModsOfMistriaInstallerLib.Nexus;

public record NexusUser(int UserId, string Name, bool IsPremium, bool IsSupporter);

public record NexusFileInfo(int FileId, string FileName, string Name, string? Version, long SizeInBytes)
{
    public DateTimeOffset UploadedAt { get; init; } = DateTimeOffset.MinValue;

    /// <summary>The category Nexus files it under: MAIN, UPDATE, OPTIONAL, OLD_VERSION...</summary>
    public string Category { get; init; } = "";

    public bool IsPrimary { get; init; }
}

public record NexusRateLimit(int? HourlyRemaining, int? DailyRemaining);

/// <summary>
/// Raised for any Nexus API failure that the user can act on. <see cref="StatusCode"/> is
/// null for transport-level problems.
/// </summary>
public class NexusApiException(string message, HttpStatusCode? statusCode = null, Exception? inner = null)
    : Exception(message, inner)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
}

/// <summary>
/// A thin client for the parts of the Nexus Mods v1 REST API that a mod manager needs:
/// validating the user's OAuth access token, looking up file metadata, and turning an
/// nxm:// link into a real CDN download URL.
///
/// Authentication is a user-authorized OAuth bearer token obtained through Authorization Code +
/// PKCE. AIM never accepts or sends a personal Nexus API key.
/// Non-premium accounts additionally need the <c>key</c>/<c>expires</c> pair carried by the
/// nxm link; without it the API refuses to mint a download URL (that restriction is what
/// makes the website's "Mod Manager Download" button the only entry point for free users).
/// </summary>
public class NexusApiClient
{
    private const string BaseUrl = "https://api.nexusmods.com/v1";

    private readonly string _accessToken;
    private readonly HttpClient _http;

    public NexusRateLimit? LastRateLimit { get; private set; }

    public NexusApiClient(string accessToken, HttpClient? http = null)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new ArgumentException("An OAuth access token is required", nameof(accessToken));

        _accessToken = accessToken.Trim();
        _http = http ?? DefaultClient();
    }

    private static HttpClient DefaultClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"AIM/{Version}");
        return client;
    }

    // Nexus asks clients to identify themselves; the library's own assembly is used rather
    // than the entry assembly so the value stays right under the test host too.
    private static string Version =>
        typeof(NexusApiClient).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version ?? "0.0.0";

    // ── Endpoints ────────────────────────────────────────────────────────────────

    /// <summary>Confirms the OAuth token works and tells us who it belongs to.</summary>
    public async Task<NexusUser> ValidateAccessTokenAsync(CancellationToken ct = default)
    {
        var json = await GetJsonAsync($"{BaseUrl}/users/validate.json", ct);

        return new NexusUser(
            json.Value<int?>("user_id") ?? 0,
            json.Value<string>("name") ?? "",
            json.Value<bool?>("is_premium") ?? json.Value<bool?>("is_premium?") ?? false,
            json.Value<bool?>("is_supporter") ?? json.Value<bool?>("is_supporter?") ?? false
        );
    }

    public async Task<NexusFileInfo> GetFileInfoAsync(NxmLink link, CancellationToken ct = default)
    {
        var json = await GetJsonAsync(
            $"{BaseUrl}/games/{link.Game}/mods/{link.ModId}/files/{link.FileId}.json", ct);

        return new NexusFileInfo(
            json.Value<int?>("file_id") ?? link.FileId,
            json.Value<string>("file_name") ?? $"{link.ModId}-{link.FileId}.zip",
            json.Value<string>("name") ?? "",
            json.Value<string>("version"),
            json.Value<long?>("size_in_bytes") ?? (json.Value<long?>("size_kb") ?? 0) * 1024
        );
    }

    /// <summary>
    /// The file a visitor to the mod page would download: the author's primary file if they marked
    /// one, otherwise the newest file in the MAIN category. Update and optional files are ignored -
    /// they are patches and extras, not the mod itself.
    /// </summary>
    public async Task<NexusFileInfo?> GetLatestMainFileAsync(string game, int modId, CancellationToken ct = default)
    {
        var json = await GetJsonAsync($"{BaseUrl}/games/{game}/mods/{modId}/files.json", ct);

        var files = (json["files"] as JArray ?? [])
            .OfType<JObject>()
            .Select(ReadFile)
            .Where(file => file.Category.Equals("MAIN", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (files.Count == 0) return null;

        return files.FirstOrDefault(file => file.IsPrimary)
               ?? files.OrderByDescending(file => file.UploadedAt).First();
    }

    private static NexusFileInfo ReadFile(JObject entry) =>
        new(
            entry.Value<int?>("file_id") ?? 0,
            entry.Value<string>("file_name") ?? "",
            entry.Value<string>("name") ?? "",
            entry.Value<string>("version"),
            entry.Value<long?>("size_in_bytes") ?? (entry.Value<long?>("size_kb") ?? 0) * 1024)
        {
            Category = entry.Value<string>("category_name") ?? "",
            IsPrimary = entry.Value<bool?>("is_primary") ?? false,
            UploadedAt = entry.Value<long?>("uploaded_timestamp") is { } stamp
                ? DateTimeOffset.FromUnixTimeSeconds(stamp)
                : DateTimeOffset.MinValue
        };

    public async Task<string?> GetModNameAsync(NxmLink link, CancellationToken ct = default)
    {
        try
        {
            var json = await GetJsonAsync($"{BaseUrl}/games/{link.Game}/mods/{link.ModId}.json", ct);
            return json.Value<string>("name");
        }
        catch (NexusApiException)
        {
            // A missing mod name is cosmetic - the download can still go ahead.
            return null;
        }
    }

    /// <summary>
    /// Asks Nexus for the CDN URLs for a file. Several are usually returned (one per
    /// mirror); the first is the user's preferred server.
    /// </summary>
    public async Task<List<string>> GetDownloadUrlsAsync(NxmLink link, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/games/{link.Game}/mods/{link.ModId}/files/{link.FileId}/download_link.json";

        if (link.HasDownloadToken)
            url += $"?key={Uri.EscapeDataString(link.Key!)}&expires={link.Expires}";

        JToken json;
        try
        {
            json = await GetTokenAsync(url, ct);
        }
        catch (NexusApiException e) when (e.StatusCode == HttpStatusCode.Forbidden && !link.HasDownloadToken)
        {
            throw new NexusApiException(
                Resources.ResourceManager.GetString(
                    "GUINexusFreeAccountDownloadNeedsVortex",
                    Resources.Culture) ??
                "Nexus refused to generate a download link. Free accounts can only download through the " +
                "\"Mod Manager Download\" button on the website - a link opened by hand has no download token.",
                e.StatusCode, e);
        }

        var urls = json
            .Children<JObject>()
            .Select(entry => entry.Value<string>("URI") ?? entry.Value<string>("uri"))
            .Where(uri => !string.IsNullOrEmpty(uri))
            .Select(uri => uri!)
            .ToList();

        if (urls.Count == 0)
            throw new NexusApiException("Nexus returned no download servers for that file.");

        return urls;
    }

    // ── Plumbing ─────────────────────────────────────────────────────────────────

    private async Task<JObject> GetJsonAsync(string url, CancellationToken ct)
    {
        var token = await GetTokenAsync(url, ct);
        if (token is JObject obj) return obj;
        throw new NexusApiException("Nexus returned an unexpected response.");
    }

    private async Task<JToken> GetTokenAsync(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        request.Headers.Add("Application-Name", "AIM - Mods of Mistria Installer");
        request.Headers.Add("Application-Version", Version);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new NexusApiException("The Nexus API did not respond in time.");
        }
        catch (HttpRequestException e)
        {
            throw new NexusApiException($"Could not reach the Nexus API: {e.Message}", null, e);
        }

        using (response)
        {
            RecordRateLimit(response);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
                throw new NexusApiException(DescribeFailure(response.StatusCode, body), response.StatusCode);

            try
            {
                return JToken.Parse(body);
            }
            catch (Exception e)
            {
                throw new NexusApiException("Could not read the response from Nexus.", response.StatusCode, e);
            }
        }
    }

    private void RecordRateLimit(HttpResponseMessage response)
    {
        LastRateLimit = new NexusRateLimit(
            ReadIntHeader(response, "X-RL-Hourly-Remaining"),
            ReadIntHeader(response, "X-RL-Daily-Remaining"));
    }

    private static int? ReadIntHeader(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) &&
        int.TryParse(values.FirstOrDefault(), out var value)
            ? value
            : null;

    private static string DescribeFailure(HttpStatusCode status, string body)
    {
        var message = TryReadMessage(body);

        return status switch
        {
            HttpStatusCode.Unauthorized =>
                "Nexus rejected the account session. Connect your Nexus account again.",
            HttpStatusCode.Forbidden =>
                message ?? "Nexus refused the request. The download link may have expired - try clicking it again.",
            HttpStatusCode.NotFound =>
                "That mod or file no longer exists on Nexus.",
            HttpStatusCode.TooManyRequests =>
                "You have hit the Nexus API rate limit. Wait a while before downloading again.",
            _ => message ?? $"Nexus returned an error ({(int)status})."
        };
    }

    private static string? TryReadMessage(string body)
    {
        try
        {
            return JObject.Parse(body).Value<string>("message");
        }
        catch
        {
            return null;
        }
    }
}
