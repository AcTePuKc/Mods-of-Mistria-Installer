namespace Garethp.ModsOfMistriaInstallerLib.Nexus;

/// <summary>
/// A parsed <c>nxm://</c> link, the URI the Nexus Mods website hands to a mod manager
/// when the user clicks "Mod Manager Download".
///
/// The shape is:
///     nxm://{game}/mods/{modId}/files/{fileId}?key={key}&amp;expires={unix}&amp;user_id={id}
///
/// The query part is only present for non-premium accounts: it is a short-lived token
/// that authorises one download of that file. Premium accounts get a bare link and the
/// manager asks the API for a download URL directly.
/// </summary>
public record NxmLink(
    string Game,
    int ModId,
    int FileId,
    string? Key,
    long? Expires,
    int? UserId)
{
    /// <summary>The game domain AIM cares about. Links for other games are rejected.</summary>
    public const string MistriaGameDomain = "fieldsofmistria";

    public bool HasDownloadToken => !string.IsNullOrEmpty(Key) && Expires is not null;

    /// <summary>
    /// True once the <c>expires</c> stamp has passed. Nexus tokens are good for roughly
    /// a day; a stale one produces a 403 from the API, so it is worth catching early.
    /// </summary>
    public bool IsExpired => Expires is not null &&
                             DateTimeOffset.FromUnixTimeSeconds(Expires.Value) < DateTimeOffset.UtcNow;

    public static bool IsNxmUri(string? input) =>
        !string.IsNullOrWhiteSpace(input) &&
        input.StartsWith("nxm://", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Parses an nxm link. Returns false and fills <paramref name="error"/> with a message
    /// meant for the user rather than throwing: bad links arrive from the browser, not from
    /// our own code, so they are an expected input.
    /// </summary>
    public static bool TryParse(string? input, out NxmLink? link, out string? error)
    {
        link = null;
        error = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "Empty download link.";
            return false;
        }

        if (!Uri.TryCreate(input.Trim(), UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals("nxm", StringComparison.OrdinalIgnoreCase))
        {
            error = "That is not an nxm:// link.";
            return false;
        }

        var game = uri.Host;
        if (string.IsNullOrEmpty(game))
        {
            error = "The link does not name a game.";
            return false;
        }

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString)
            .ToArray();

        // Collections use nxm://{game}/collections/{slug}/revisions/{n} and are a different
        // install flow entirely, so they get their own message instead of "malformed link".
        if (segments.Length > 0 && segments[0].Equals("collections", StringComparison.OrdinalIgnoreCase))
        {
            error = "Nexus collections are not supported yet - download the mods individually.";
            return false;
        }

        if (segments.Length < 4 ||
            !segments[0].Equals("mods", StringComparison.OrdinalIgnoreCase) ||
            !segments[2].Equals("files", StringComparison.OrdinalIgnoreCase))
        {
            error = "The link is not a mod file download.";
            return false;
        }

        if (!int.TryParse(segments[1], out var modId) || modId <= 0 ||
            !int.TryParse(segments[3], out var fileId) || fileId <= 0)
        {
            error = "The link has an invalid mod or file id.";
            return false;
        }

        var query = ParseQuery(uri.Query);
        query.TryGetValue("key", out var key);

        long? expires = query.TryGetValue("expires", out var expiresRaw) &&
                        long.TryParse(expiresRaw, out var expiresValue)
            ? expiresValue
            : null;

        int? userId = query.TryGetValue("user_id", out var userRaw) &&
                      int.TryParse(userRaw, out var userValue)
            ? userValue
            : null;

        link = new NxmLink(game.ToLowerInvariant(), modId, fileId, string.IsNullOrEmpty(key) ? null : key, expires, userId);
        return true;
    }

    public bool IsForMistria() => Game.Equals(MistriaGameDomain, StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(query)) return result;

        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var split = pair.Split('=', 2);
            if (split.Length != 2) continue;
            result[Uri.UnescapeDataString(split[0])] = Uri.UnescapeDataString(split[1]);
        }

        return result;
    }
}
