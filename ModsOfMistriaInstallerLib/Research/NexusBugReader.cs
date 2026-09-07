using System.Net;
using System.Text.RegularExpressions;

namespace Garethp.ModsOfMistriaInstallerLib.Research;

/// <summary>
/// What a mod author decided about a bug report.
///
/// This is the part the researcher was missing entirely. A bug tracker used to be read as a flat
/// list of complaints, so "these two mods crash together" and "these two mods crash together -
/// closed, not a bug" counted the same. They are opposite answers, and the second one is the more
/// useful of the two: somebody already investigated this exact pairing and concluded there was
/// nothing wrong.
/// </summary>
public enum BugState
{
    /// <summary>Reported, nobody has ruled on it. Weak evidence at best.</summary>
    New,

    /// <summary>The author has acknowledged it. Real, and unresolved.</summary>
    Known,

    /// <summary>The author is investigating.</summary>
    BeingLookedAt,

    /// <summary>Resolved. Whatever it said, it no longer applies to the current version.</summary>
    Fixed,

    /// <summary>Already reported elsewhere. Says nothing on its own.</summary>
    Duplicate,

    /// <summary>Investigated and dismissed. Evidence *against* the conflict.</summary>
    NotABug,

    /// <summary>Real, and the author does not intend to fix it.</summary>
    WontFix,

    /// <summary>Nobody could reproduce it from what was reported.</summary>
    NeedMoreInfo,

    /// <summary>The status was not one AIM recognises.</summary>
    Unknown
}

/// <summary>One row of a mod's bug tracker, before its replies are fetched.</summary>
public sealed record NexusBugSummary(int IssueId, string Title, BugState State, int Replies);

/// <summary>
/// Reads a mod's bug tracker properly: every report with the author's ruling on it, and the full
/// reply thread for the ones that matter.
///
/// The bugs tab renders its first page server-side and pages the rest through a widget, the same
/// shape as the comments. A report's replies are not on the tab at all - they arrive from a second
/// endpoint, one request per report - which is why the reply threads went unread for so long.
///
/// Reading every thread would be dozens of requests per mod for a question that usually turns on
/// two of them, so the list is read first and only the reports worth opening are opened. Which
/// those are is decided by the caller, which knows the other mod's name.
/// </summary>
public static class NexusBugReader
{
    private const string Widgets = "https://www.nexusmods.com/Core/Libs/Common/Widgets";

    /// <summary>
    /// The bug list's page size. Ten is the site's own, and it is also the server's cap - asking
    /// for fifty returns ten - so this is a fact rather than a preference.
    /// </summary>
    private const int BugPageSize = 10;

    private static readonly Regex RowPattern = new(
        @"<tr[^>]*\bdata-issue-id=""(\d+)""(.*?)</tr>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex CellPattern = new(
        @"class=""[^""]*\btable-bug-(title|status|replies)\b[^""]*""[^>]*>(.{0,4000}?)</td>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>
    /// One page of a mod's bug reports, newest first, with the author's ruling on each.
    /// </summary>
    public static async Task<List<NexusBugSummary>> ReadBugListAsync(
        HttpClient http,
        int gameId,
        int modId,
        int page,
        NexusSession session,
        CancellationToken ct = default)
    {
        var parameters = $"id:{modId},game_id:{gameId},page_size:{BugPageSize},page:{page}";
        var url = $"{Widgets}/ModBugsTab?RH_ModBugsTab={Uri.EscapeDataString(parameters)}";

        var html = await NexusPageReader.FetchAsync(http, url, session, ct, asXhr: true);

        return html is null ? [] : ExtractBugs(html);
    }

    /// <summary>
    /// Pulls the rows out of a bug tab's HTML.
    ///
    /// Split from the fetch so it can be tested against saved markup, for the same reason the
    /// comment extractor is: a scraper nobody can test offline is a scraper nobody can trust.
    /// </summary>
    public static List<NexusBugSummary> ExtractBugs(string html)
    {
        var bugs = new List<NexusBugSummary>();
        if (string.IsNullOrWhiteSpace(html)) return bugs;

        foreach (Match row in RowPattern.Matches(html))
        {
            if (!int.TryParse(row.Groups[1].Value, out var issueId)) continue;

            var cells = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match cell in CellPattern.Matches(row.Groups[2].Value))
                cells[cell.Groups[1].Value] = PlainText(cell.Groups[2].Value);

            cells.TryGetValue("title", out var title);
            cells.TryGetValue("status", out var status);
            cells.TryGetValue("replies", out var replies);

            bugs.Add(new NexusBugSummary(
                issueId,
                title ?? "",
                ReadState(status ?? ""),
                int.TryParse(replies, out var count) ? count : 0));
        }

        return bugs;
    }

    /// <summary>
    /// The whole conversation on one bug report: the report itself and every reply.
    ///
    /// A POST, unlike everything else read here, and it takes nothing but the issue id - so it is
    /// the one endpoint that needs no game, mod or thread to address it.
    /// </summary>
    public static async Task<List<NexusPagePost>> ReadBugRepliesAsync(
        HttpClient http,
        int issueId,
        NexusSession session,
        CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{Widgets}/ModBugReplyList");
            session.Apply(request);
            request.Headers.Add("X-Requested-With", "XMLHttpRequest");
            request.Content = new FormUrlEncodedContent(
                [new KeyValuePair<string, string>("issue_id", issueId.ToString())]);

            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                Logger.Log($"Could not read the replies on bug {issueId}: {(int)response.StatusCode}");
                return [];
            }

            // The reply list is rendered with the same comment markup as the posts tab, so the
            // existing extractor reads it unchanged.
            return NexusPageReader.Extract(await response.Content.ReadAsStringAsync(ct), "bugs");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Logger.Log($"Could not read the replies on bug {issueId}: {exception.Message}");
            return [];
        }
    }

    /// <summary>
    /// What a report's status means for the conflict in front of the user.
    ///
    /// A dismissed or duplicated report is evidence the problem is not real; an acknowledged one is
    /// evidence it is. A fixed one is neither: it describes a version nobody is running any more.
    /// </summary>
    public static Polarity? WeighState(BugState state) => state switch
    {
        BugState.NotABug => Polarity.Clearance,
        BugState.Known or BugState.WontFix => Polarity.Blocker,
        BugState.BeingLookedAt or BugState.New => Polarity.Caution,

        // Fixed, Duplicate, NeedMoreInfo and Unknown say nothing on their own, so the sentence has
        // to earn its place on its own words.
        _ => null
    };

    /// <summary>The wording Nexus shows in the status column, as of the 2026 layout.</summary>
    private static BugState ReadState(string status) => status.Trim().ToLowerInvariant() switch
    {
        "new issue" or "new issues" => BugState.New,
        "known issue" or "known issues" => BugState.Known,
        "being looked at" => BugState.BeingLookedAt,
        "fixed" => BugState.Fixed,
        "duplicate" or "duplicates" => BugState.Duplicate,
        "not a bug" => BugState.NotABug,
        "won't fix" or "wont fix" => BugState.WontFix,
        "need more info" => BugState.NeedMoreInfo,
        _ => BugState.Unknown
    };

    private static string PlainText(string html)
    {
        var withoutTags = Regex.Replace(html, "<[^>]+>", " ");
        return Regex.Replace(WebUtility.HtmlDecode(withoutTags), @"\s+", " ").Trim();
    }
}
