using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace Garethp.ModsOfMistriaInstallerLib;

/// <summary>One post from a mod's bug tracker or comment thread.</summary>
public sealed record NexusPagePost(string Author, string Text, string Tab);

/// <summary>Another mod linked from a mod's page - a requirement, a patch, a recommendation.</summary>
public sealed record NexusRelatedMod(int ModId, string Title, string Url);

/// <summary>
/// Reads the parts of a mod page the Nexus API does not expose: its bug reports and its comments.
///
/// This is scraping, and it is treated as such. The public API has no endpoint for either, yet that
/// is exactly where "does this work with X?" gets answered - so the choice is between reading the
/// HTML and telling the user to go and read it themselves. What makes it acceptable here is that
/// nothing depends on it: every extraction is best-effort, a layout change produces no posts rather
/// than wrong ones, and the caller always shows the links to the real pages regardless.
///
/// It deliberately does not log in, follow pagination, or pretend to be a browser beyond a plain
/// user agent. It reads the first page of what a signed-out visitor would see.
/// </summary>
public static class NexusPageReader
{
    private const int MaxPostsPerTab = 40;
    private const int MaxPostLength = 600;

    // Nexus renders each comment and bug body inside an element carrying one of these classes. More
    // than one is listed because the two tabs differ and the markup has changed before; whichever
    // matches, matches.
    private static readonly string[] BodyClasses =
        ["comment-content", "comment-body", "bug-content", "forum-post-content", "post-content"];

    private static readonly Regex AuthorPattern = new(
        "class=\"[^\"]*\\b(?:comment-user|comment-author|username|user-name)\\b[^\"]*\"[^>]*>(?:\\s*<[^>]+>)*\\s*([^<]{1,60})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Fetches one tab of a mod page and pulls out the posts it can find.
    /// </summary>
    /// <returns>An empty list whenever anything at all goes wrong. This is never an error path.</returns>
    public static async Task<List<NexusPagePost>> ReadTabAsync(
        HttpClient http, string pageUrl, string tab, CancellationToken ct = default) =>
        await ReadTabAsync(http, pageUrl, tab, 1, Research.NexusSession.Anonymous, ct);

    /// <summary>
    /// Reads a tab across several pages of comments or bug reports.
    ///
    /// The first page used to be the whole of it, and on a popular mod that is the *newest* twenty
    /// posts - which are about the mod's latest release, not about the mod you happen to have
    /// installed alongside something else. The answer to "does this work with X" is usually months
    /// old and several pages back, so a single page systematically missed exactly the posts the
    /// researcher exists to find.
    ///
    /// It still stops early rather than reading a thread to the end: a page that yields nothing new
    /// ends the walk, because that is what both the last page and a layout change look like, and
    /// there is no reason to keep asking Nexus for either.
    /// </summary>
    /// <param name="maxPages">
    /// How deep to go. Only the comments actually paginate; the bug tracker is read as one page
    /// because its tab renders whole and there is no equivalent widget to call.
    /// </param>
    /// <param name="gameId">
    /// Nexus's numeric game id, needed to address the comment widget. Zero reads page one only.
    /// </param>
    /// <param name="modId">The mod whose comments these are. Zero reads page one only.</param>
    public static async Task<List<NexusPagePost>> ReadTabAsync(
        HttpClient http,
        string pageUrl,
        string tab,
        int maxPages,
        Research.NexusSession session,
        CancellationToken ct = default,
        int gameId = 0,
        int modId = 0)
    {
        var all = new List<NexusPagePost>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Page one always comes from the tab itself, which renders server-side for both tabs.
        var firstPage = await FetchAsync(http, $"{pageUrl}?tab={tab}", session, ct);
        if (firstPage is null) return all;

        foreach (var post in Extract(firstPage, tab).Where(post => seen.Add(post.Text)))
            all.Add(post);

        var comments = tab.Equals("posts", StringComparison.OrdinalIgnoreCase);
        if (!comments || maxPages <= 1 || gameId <= 0 || modId <= 0) return all;

        // The rest come from the widget the site's own pager calls. Without a thread id there is
        // no way to ask for them, and page one is simply what the user gets.
        var threadId = ExtractCommentThreadId(firstPage);
        if (threadId is null)
        {
            Logger.Log($"No comment thread id on {pageUrl}, so only the first page was read.");
            return all;
        }

        for (var page = 2; page <= maxPages; page++)
        {
            // A short pause between pages. Nothing here is urgent, and a mod manager should not
            // look like a scraper hammering somebody else's server.
            await Task.Delay(250, ct);

            var posts = await ReadCommentPageAsync(
                http, gameId, modId, threadId.Value, page, session, ct);

            // An empty answer is the end of the thread, and a page of nothing but posts already
            // seen is what a widget that ignored the page number would return. Both stop the walk
            // rather than being read as an error.
            var fresh = posts.Where(post => seen.Add(post.Text)).ToList();
            if (fresh.Count == 0) break;

            all.AddRange(fresh);
        }

        return all;
    }

    /// <summary>
    /// Fetches a page, or null when anything goes wrong. Shared by every reader here so they all
    /// identify themselves the same way and all fail the same silent way.
    /// </summary>
    /// <param name="asXhr">
    /// Marks the request as the background fetch it is. The comment widget is only ever called
    /// this way by the site's own pager, and saying so is more accurate than pretending a person
    /// typed the URL in.
    /// </param>
    public static async Task<string?> FetchAsync(
        HttpClient http, string url, Research.NexusSession session, CancellationToken ct = default,
        bool asXhr = false)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            session.Apply(request);

            if (asXhr) request.Headers.Add("X-Requested-With", "XMLHttpRequest");

            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                Logger.Log($"Could not read {url}: {(int)response.StatusCode}");
                return null;
            }

            return await response.Content.ReadAsStringAsync(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Logger.Log($"Could not read {url}: {exception.Message}");
            return null;
        }
    }

    /// <summary>
    /// The comment thread's own id, read out of the mod page.
    ///
    /// Needed because the widget that serves page two of the comments is addressed by thread rather
    /// than by mod, and nothing else on the site exposes the mapping - the page's own JavaScript
    /// reads it from the markup exactly like this.
    /// </summary>
    public static int? ExtractCommentThreadId(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;

        // Written as JSON, as a JS object literal and inside the widget's own query string in
        // different places on the page, so the separator is left loose rather than guessed at.
        var match = Regex.Match(html, @"thread_id[""'\s:=]{1,4}(\d{3,})", RegexOptions.IgnoreCase);

        return match.Success && int.TryParse(match.Groups[1].Value, out var id) ? id : null;
    }

    /// <summary>
    /// One page of a mod's comments, from the widget the site's own pager calls.
    ///
    /// The comment pager is not a link. Its hrefs are literally <c>javascript:;</c>, and
    /// <c>?tab=posts&amp;page=2</c> serves page one again - which is why reading "several pages" of
    /// comments quietly read one page for a while. What the page actually calls is this: a widget
    /// endpoint taking a single parameter whose value is a comma-separated list of key:value pairs.
    ///
    /// The parameters, and the page size, are the site's own rather than anything invented here.
    /// Asking for a bigger page than the site asks for would fetch more per request but is exactly
    /// the sort of thing that gets a client blocked, and the point of this is to read what a
    /// visitor can already see.
    /// </summary>
    /// <param name="objectId">The mod's id. The widget calls it an object id because it serves
    /// comments for images and collections through the same endpoint.</param>
    /// <param name="search">
    /// Restricts the answer to comments containing this text, which is what makes reading a long
    /// thread practical at all - see <see cref="SearchCommentsAsync"/>. Empty returns the page as
    /// the site would show it.
    /// </param>
    public static async Task<List<NexusPagePost>> ReadCommentPageAsync(
        HttpClient http,
        int gameId,
        int objectId,
        int threadId,
        int page,
        Research.NexusSession session,
        CancellationToken ct = default,
        string search = "")
    {
        // Exactly the parameters the site's own pager sends, in its order. comment_id is not
        // optional: without it the widget answers every page with page one, which is precisely the
        // bug that made an earlier attempt at this silently read nothing new.
        var parameters = string.Join(",",
            "comment_id:0",
            $"game_id:{gameId}",
            $"object_id:{objectId}",
            "object_type:1",
            $"thread_id:{threadId}",
            $"search_text:{Sanitise(search)}",
            "tabbed:1",
            "skip_opening_post:0",
            "display_title:0",
            "user_is_blocked:false",
            "searchable:true",
            $"page_size:{CommentPageSize}",
            $"page:{page}");

        var url = "https://www.nexusmods.com/Core/Libs/Common/Widgets/CommentContainer" +
                  $"?RH_CommentContainer={Uri.EscapeDataString(parameters)}";

        var html = await FetchAsync(http, url, session, ct, asXhr: true);

        return html is null ? [] : Extract(html, "posts");
    }

    /// <summary>The page size the site itself asks for.</summary>
    private const int CommentPageSize = 20;

    /// <summary>
    /// A comma and a colon are the widget's own separators, so a search term containing either
    /// would be read as more parameters. Nothing else needs escaping - the whole value is URL
    /// encoded by the caller.
    /// </summary>
    private static string Sanitise(string term) =>
        string.IsNullOrWhiteSpace(term) ? "" : term.Replace(',', ' ').Replace(':', ' ').Trim();

    /// <summary>
    /// Every comment in a mod's thread that mentions a given phrase.
    ///
    /// This is the difference between reading a mod's comments and merely sampling them. A busy
    /// mod has five hundred comments across twenty-odd pages, and the one that answers "does this
    /// work with X?" is usually months old and nowhere near the first page - so walking pages from
    /// the front is a lottery. The thread is searchable, and searching it for the *other mod's
    /// name* returns the handful of comments that are actually about this pairing, out of the whole
    /// thread, in one request.
    ///
    /// It needs no account: the site hides the search box behind a login prompt, but the endpoint
    /// answers a signed-out request identically. That was worth checking rather than assuming,
    /// because it is the difference between this working for everybody and working for nobody.
    /// </summary>
    public static Task<List<NexusPagePost>> SearchCommentsAsync(
        HttpClient http,
        int gameId,
        int modId,
        int threadId,
        string phrase,
        Research.NexusSession session,
        CancellationToken ct = default) =>
        ReadCommentPageAsync(http, gameId, modId, threadId, 1, session, ct, phrase);

    /// <summary>
    /// The readme behind a mod's Docs tab, as plain text.
    ///
    /// Worth a special case rather than being scraped off the tab like everything else, because
    /// Nexus publishes it ready-made: the "View as plain text" link on that tab points at a static
    /// file on its metadata host, so there is no markup to strip, no layout to break, and nothing
    /// to get wrong. An author who documents "do not use this with X" anywhere other than the
    /// description usually does it here.
    /// </summary>
    /// <param name="gameId">
    /// Nexus's numeric id for the game, which is what this host is keyed by - not the
    /// <c>fieldsofmistria</c> domain name the rest of the site uses.
    /// </param>
    /// <returns>Null when the mod has no readme, which is the common case and not an error.</returns>
    public static Task<string?> ReadReadmeAsync(
        HttpClient http, int gameId, int modId, Research.NexusSession session, CancellationToken ct = default) =>
        FetchAsync(http, $"https://file-metadata.nexusmods.com/file/nexus-readmes/{gameId}/{modId}/readme.txt",
            session, ct);

    /// <summary>
    /// The mods listed on a mod's page as requiring it.
    ///
    /// This is the one place a compatibility patch reliably announces itself. A patch for A and B
    /// declares both as requirements, so Nexus lists it under "Mods requiring this file" on *both*
    /// pages - and a mod appearing on both lists is, with very few exceptions, exactly the patch
    /// the user is looking for. Finding it this way needs no search engine and no guessing at
    /// titles, which is why it is preferred to searching for the word "patch".
    /// </summary>
    public static List<NexusRelatedMod> ExtractRequiringMods(string html, string game)
    {
        var related = new List<NexusRelatedMod>();
        if (string.IsNullOrWhiteSpace(html)) return related;

        // Every link from the page to another mod in the same game, deduplicated by id. Casting a
        // wider net than the "requiring" table alone is deliberate: authors also link the patch
        // straight from their description, and the two lists are used the same way - as candidates
        // that have to appear on both mods' pages before anything is claimed.
        var pattern = new Regex(
            $@"href=""(?:https://www\.nexusmods\.com)?/(?:games/)?{Regex.Escape(game)}/mods/(\d+)[^""]*""[^>]*>(?:\s*<[^>]+>)*\s*([^<]{{0,120}})",
            RegexOptions.IgnoreCase);

        var seen = new HashSet<int>();

        foreach (Match match in pattern.Matches(html))
        {
            if (!int.TryParse(match.Groups[1].Value, out var id) || !seen.Add(id)) continue;

            var title = WebUtility.HtmlDecode(match.Groups[2].Value).Trim();
            related.Add(new NexusRelatedMod(id, title,
                $"https://www.nexusmods.com/{game}/mods/{id}"));
        }

        return related;
    }

    /// <summary>
    /// Pulls post bodies out of a page's HTML.
    ///
    /// Split out from the fetch so it can be tested against saved markup - the only way to keep a
    /// scraper honest without a network.
    /// </summary>
    public static List<NexusPagePost> Extract(string html, string tab)
    {
        var posts = new List<NexusPagePost>();
        if (string.IsNullOrWhiteSpace(html)) return posts;

        // Where each name appears, not just the order they appear in. Pairing by index would
        // desynchronise the moment anything produced a name without a kept post - a one-word
        // comment below the length floor, a deleted post, the "uploaded by" box - and every
        // later quote would then be attributed to the wrong person. Wrong attribution is worse
        // than none, so position decides it.
        var authors = AuthorPattern.Matches(html)
            .Select(match => (match.Index, Name: WebUtility.HtmlDecode(match.Groups[1].Value).Trim()))
            .Where(entry => entry.Name.Length > 0)
            .ToList();

        foreach (var body in BodyClasses)
        {
            // Non-greedy to the next closing div: nested markup inside a post is stripped to text
            // afterwards anyway, so the exact boundary matters less than not swallowing the page.
            var pattern = new Regex(
                $"class=\"[^\"]*\\b{Regex.Escape(body)}\\b[^\"]*\"[^>]*>(.{{0,8000}}?)</div>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            foreach (Match match in pattern.Matches(html))
            {
                var text = PlainText(match.Groups[1].Value);
                if (text.Length < 15) continue;

                // The nearest name above this post in the document is the one who wrote it.
                var author = authors
                    .Where(entry => entry.Index < match.Index)
                    .Select(entry => entry.Name)
                    .LastOrDefault() ?? "";

                posts.Add(new NexusPagePost(author, Trim(text), tab));

                if (posts.Count >= MaxPostsPerTab) return posts;
            }

            if (posts.Count > 0) break;
        }

        return posts;
    }

    private static string PlainText(string html)
    {
        // Scripts and styles first: their contents are not prose and would otherwise survive the
        // tag strip as a wall of JavaScript.
        var withoutScripts = Regex.Replace(html, "<(script|style)[^>]*>.*?</\\1>", " ",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        var withoutTags = Regex.Replace(withoutScripts, "<[^>]+>", " ");

        return Regex.Replace(WebUtility.HtmlDecode(withoutTags), @"\s+", " ").Trim();
    }

    private static string Trim(string text) =>
        text.Length <= MaxPostLength ? text : text[..MaxPostLength] + "…";
}
