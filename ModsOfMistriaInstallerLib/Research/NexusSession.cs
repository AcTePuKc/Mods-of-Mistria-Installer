using System.Net.Http.Headers;

namespace Garethp.ModsOfMistriaInstallerLib.Research;

/// <summary>
/// How AIM identifies itself when reading a Nexus page, and - optionally - who as.
///
/// Reading a mod page signed out is enough for the description, the comments and the bug tracker,
/// which is why the researcher has always worked without a login. It is not enough for everything:
/// a post inside a thread the author has restricted, a bug report marked private, and the pages a
/// user has hidden behind their own content filters are all invisible to a guest, and those are
/// exactly the places an obscure incompatibility ends up.
///
/// So a session cookie can be supplied, and when it is, the same reader sees what the user sees.
/// Two rules govern it, both about it being *the user's* account and not AIM's:
///
///   • It is never obtained by AIM. The user pastes their own cookie, the way they already paste
///     their own API key; AIM does not drive a browser or a login form on their behalf.
///   • It is only ever used to read. Nothing here posts, endorses, tracks, votes or downloads
///     while signed in, so the worst a bug in this file can do is read a page twice.
/// </summary>
public sealed record NexusSession(string? Cookie)
{
    /// <summary>Reading as a guest, which is the default and covers most pages.</summary>
    public static readonly NexusSession Anonymous = new((string?)null);

    public bool IsSignedIn => !string.IsNullOrWhiteSpace(Cookie);

    /// <summary>
    /// Prepares a request. The user agent names AIM rather than impersonating a browser: Nexus
    /// answers a request with no user agent with a bare 403, and a client that lies about what it
    /// is cannot be blocked selectively if it ever misbehaves.
    /// </summary>
    public void Apply(HttpRequestMessage request)
    {
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue(
            "AIM-ModsOfMistriaInstaller", InstallerVersion.ModCompatibilityVersion.ToString()));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));

        // The readme behind a mod's Docs tab is served as text/plain from a different host, so the
        // same reader has to be willing to accept something other than a web page.
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.5));

        if (IsSignedIn) request.Headers.Add("Cookie", Cookie!.Trim());
    }
}
