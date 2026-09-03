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

    /// <summary>
    /// The blurb the author wrote under the file on the Files tab, as HTML.
    ///
    /// The one place that says what a file actually is. A file called "Voidstril V1.0" tells the
    /// user nothing about why it is being offered to them; the author's own line under it usually
    /// says outright that it is a recolour, a standalone variant or a patch for something.
    /// </summary>
    public string Description { get; init; } = "";
}

public record NexusRateLimit(int? HourlyRemaining, int? DailyRemaining);

/// <summary>The text of a mod's Nexus page, as far as the public API exposes it.</summary>
public record NexusModOverview(string Name, string Summary, string Description, string Version);

/// <summary>One version's release notes, as the mod's author wrote them.</summary>
public record ModChangelogEntry(string Version, List<string> Lines)
{
    /// <summary>The notes as one block, for a tooltip that cannot hold a list.</summary>
    public string Text => string.Join("\n", Lines.Select(line => $"• {line}"));
}

/// <summary>
/// Raised for any Nexus API failure that the user can act on. <see cref="StatusCode"/> is
/// null for transport-level problems.
/// </summary>
public class NexusApiException(string message, HttpStatusCode? statusCode = null, Exception? inner = null)
    : Exception(message, inner)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;

    /// <summary>
    /// True only when the remedy really is the website's "Mod Manager Download" button.
    ///
    /// Every other failure - a dead CDN mirror, a corrupt archive, a folder AIM could not write -
    /// used to be reported with the same "open the mod page" prompt, which sent people off to
    /// download by hand for problems that had nothing to do with their account.
    /// </summary>
    public bool RequiresWebsiteDownload { get; init; }
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
    /// A mod's release notes, newest version first.
    ///
    /// Nexus returns an object keyed by version, whose ordering is not defined, so the versions are
    /// sorted here with the same comparison used for update checks rather than trusted as they
    /// arrive. A mod whose author never wrote release notes returns an empty list, which is a
    /// normal answer and not an error.
    /// </summary>
    public async Task<List<ModChangelogEntry>> GetChangelogsAsync(
        string game, int modId, CancellationToken ct = default)
    {
        JObject json;
        try
        {
            json = await GetJsonAsync($"{BaseUrl}/games/{game}/mods/{modId}/changelogs.json", ct);
        }
        catch (NexusApiException exception)
        {
            // A mod with no changelog at all answers 404. That is "nothing to show", not a failure
            // worth surfacing to the user.
            if (exception.StatusCode == HttpStatusCode.NotFound) return [];
            throw;
        }

        var entries = new List<ModChangelogEntry>();

        foreach (var property in json.Properties())
        {
            var lines = (property.Value as JArray ?? [])
                .Select(line => line.ToString().Trim())
                .Where(line => line.Length > 0)
                .ToList();

            if (lines.Count > 0) entries.Add(new ModChangelogEntry(property.Name, lines));
        }

        // Newest first, by the same version rules update checks use - so "1.10" sorts above "1.9".
        entries.Sort((left, right) =>
            NexusUpdateService.CompareVersionsNewestFirst(left.Version, right.Version));

        return entries;
    }

    /// <summary>
    /// Every file on a mod's page, in every category.
    ///
    /// The caller needs the whole list, not just the main files: a mod folder may have come from an
    /// optional or miscellaneous file, and comparing that against the main file would report an
    /// update for ever.
    /// </summary>
    public async Task<List<NexusFileInfo>> GetFilesAsync(string game, int modId, CancellationToken ct = default)
    {
        var json = await GetJsonAsync($"{BaseUrl}/games/{game}/mods/{modId}/files.json", ct);

        return (json["files"] as JArray ?? [])
            .OfType<JObject>()
            .Select(ReadFile)
            .ToList();
    }

    /// <summary>
    /// The file a visitor to the mod page would download: the author's primary file if they marked
    /// one, otherwise the newest file in the MAIN category. Update and optional files are ignored -
    /// they are patches and extras, not the mod itself.
    /// </summary>
    public async Task<NexusFileInfo?> GetLatestMainFileAsync(string game, int modId, CancellationToken ct = default)
    {
        var files = (await GetFilesAsync(game, modId, ct))
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
            Description = entry.Value<string>("description") ?? "",
            IsPrimary = entry.Value<bool?>("is_primary") ?? false,
            UploadedAt = entry.Value<long?>("uploaded_timestamp") is { } stamp
                ? DateTimeOffset.FromUnixTimeSeconds(stamp)
                : DateTimeOffset.MinValue
        };

    /// <summary>
    /// The prose on a mod's page: its one-line summary and its full description.
    ///
    /// This is what the conflict researcher reads, because it is the only part of a mod page the
    /// public API exposes. Authors put "incompatible with X", "requires the patch below" and
    /// "works fine alongside Y" here more often than anywhere else, so it answers a useful share of
    /// compatibility questions on its own. Bug reports and comments are web pages only - the API has
    /// no endpoint for either - so those stay a link the user opens.
    /// </summary>
    public async Task<NexusModOverview?> GetModOverviewAsync(string game, int modId, CancellationToken ct = default)
    {
        try
        {
            var json = await GetJsonAsync($"{BaseUrl}/games/{game}/mods/{modId}.json", ct);
            return new NexusModOverview(
                json.Value<string>("name") ?? "",
                json.Value<string>("summary") ?? "",
                json.Value<string>("description") ?? "",
                json.Value<string>("version") ?? "");
        }
        catch (NexusApiException exception)
        {
            // The researcher degrades to "we could not read the page, here is a link to it".
            Logger.Log($"Could not read the Nexus page for {game}/{modId}: {exception.Message}");
            return null;
        }
    }

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
            // Nexus issues direct download links only to Premium accounts, so this is the usual
            // answer for a free one. It is not the only one - an expired or revoked API key gives
            // the same status - so Nexus's own words are kept rather than replaced with a guess.
            throw new NexusApiException(
                Resources.ResourceManager.GetString(
                    "GUINexusFreeAccountDownloadNeedsVortex",
                    Resources.Culture) ??
                "Nexus refused to generate a download link. Free accounts can only download through the " +
                "\"Mod Manager Download\" button on the website - a link opened by hand has no download token.",
                e.StatusCode, e)
            {
                RequiresWebsiteDownload = true
            };
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
