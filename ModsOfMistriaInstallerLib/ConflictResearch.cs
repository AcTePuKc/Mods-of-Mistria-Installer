using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Garethp.ModsOfMistriaInstallerLib.Nexus;
using Garethp.ModsOfMistriaInstallerLib.Research;

namespace Garethp.ModsOfMistriaInstallerLib;

/// <summary>One thing a mod's page says that bears on this conflict.</summary>
/// <param name="Where">Which part of the page it came from, for the user to judge its weight.</param>
public sealed record ResearchFinding(
    string ModName,
    string Quote,
    string Reason,
    string SourceUrl)
{
    private readonly string? _fullQuote;

    public Polarity Polarity { get; init; } = Polarity.Context;

    public string Where { get; init; } = "";

    /// <summary>True when this quote names one of the other mods in the conflict.</summary>
    public bool NamesTheOtherMod { get; init; }

    /// <summary>
    /// The whole sentence or post, before <see cref="Quote"/> was cut down to fit a window.
    ///
    /// Kept because the cut is a display decision and a wrong one often enough to matter: a
    /// condition reads "load March Expanded after…" and stops exactly where the user needed it to
    /// go on. Losing the rest meant opening the mod page and reading it to find one sentence again.
    /// Defaults to the quote itself, so a finding that was never shortened needs no second copy.
    /// </summary>
    public string FullQuote
    {
        get => string.IsNullOrEmpty(_fullQuote) ? Quote : _fullQuote;
        init => _fullQuote = value;
    }

    /// <summary>True when the displayed quote is only part of what was said.</summary>
    public bool IsShortened => FullQuote.Length > Quote.Length;

    /// <summary>
    /// A phrase short and literal enough to find this sentence again on the page it came from -
    /// what a person would type into the browser's find bar, if they had it to hand.
    ///
    /// The start of the quote rather than the middle of it, because the page renders the sentence
    /// inside markup and a browser's find will not match text that runs across a paragraph or list
    /// boundary. A short opening run almost never does.
    /// </summary>
    public string SearchPhrase => Findable(FullQuote);

    /// <summary>Roughly a line of the find bar. Long enough to be unique, short enough to survive markup.</summary>
    private const int PhraseLength = 60;

    public static string Findable(string quote)
    {
        // Bullets, quote marks and list dashes are the page's decoration, not its words, and a
        // find bar takes them literally.
        var text = quote.Trim().TrimStart('•', '·', '-', '–', '—', '*', '>', '"', '“', '\'', ' ').Trim();

        if (text.Length <= PhraseLength) return text;

        var cut = text[..PhraseLength];
        var space = cut.LastIndexOf(' ');

        return space > 20 ? cut[..space] : cut;
    }
}

/// <summary>A page worth a human's eyes, with a one-line reason to open it.</summary>
public sealed record ResearchLink(string Label, string Url, string Reason);

/// <summary>
/// A mod that looks like it exists to make this pairing work.
/// </summary>
/// <param name="Confidence">
/// How the candidate was found. A mod linked from *both* pages is close to certain; one found by
/// its title alone is a guess worth showing but not acting on unprompted.
/// </param>
public sealed record PatchCandidate(
    int ModId,
    string Title,
    string Url,
    string Why,
    PatchConfidence Confidence)
{
    /// <summary>
    /// The exact Nexus file to install, when the candidate is one specific file rather than a whole
    /// mod.
    ///
    /// It matters for optional files: those live on a mod that is *already installed*, so resolving
    /// "the latest main file" of <see cref="ModId"/> would reinstall the mod the user already has
    /// instead of the compatibility patch sitting beside it. Null for a patch published as its own
    /// mod, where the main file is the right answer.
    /// </summary>
    public int? FileId { get; init; }
}

public enum PatchConfidence
{
    /// <summary>Linked from every mod in the conflict. Almost always the patch.</summary>
    LinkedFromBoth,

    /// <summary>Linked from one page, and its title says what it is.</summary>
    Named,

    /// <summary>A file on one of the mods' own pages - an optional compatibility download.</summary>
    OptionalFile
}

public sealed record ResearchResult(
    IReadOnlyList<ResearchFinding> Findings,
    IReadOnlyList<ResearchLink> Links)
{
    public IReadOnlyList<PatchCandidate> Patches { get; init; } = [];

    /// <summary>True when nothing was read and only the links are left.</summary>
    public bool FoundNothing => Findings.Count == 0 && Patches.Count == 0;

    /// <summary>Findings that say the mods are fine together.</summary>
    public IEnumerable<ResearchFinding> Clearances =>
        Findings.Where(finding => finding.Polarity == Polarity.Clearance);

    /// <summary>Findings that say they are not.</summary>
    public IEnumerable<ResearchFinding> Blockers =>
        Findings.Where(finding => finding.Polarity == Polarity.Blocker);
}

/// <summary>
/// Helps the user answer the question the conflict report cannot: is this actually a problem, and
/// has somebody already fixed it?
///
/// It reads every part of a mod's Nexus presence that can be read without acting as the user:
/// the summary and description through the API, the release notes, the file list, and - by reading
/// the public pages - the comment thread and the bug tracker, several pages deep rather than only
/// the newest twenty posts. What it keeps from all that is not "sentences containing a keyword"
/// but sentences it can classify: an author saying the mod is standalone is filed as evidence the
/// conflict is imaginary, and a commenter saying two mods crash together is filed as evidence it
/// is real. See <see cref="CompatibilityLanguage"/> for how, and why the old keyword list missed
/// the single most common phrasing in this game's mod descriptions.
///
/// Separately, it looks for the fix rather than only the diagnosis. A compatibility patch declares
/// both mods as requirements, so Nexus lists it on both their pages; a mod linked from every page
/// in the conflict is, in practice, the patch. That is a far more reliable way to find one than
/// searching for the word "patch", and it is what <see cref="PatchConfidence.LinkedFromBoth"/>
/// means.
///
/// Everything here is best-effort by construction. The API can be down, the markup can change, the
/// user may have no key at all - and in every one of those cases the result is fewer findings and
/// the same links to the pages a person would have opened anyway. Nothing downstream treats an
/// empty result as an error.
/// </summary>
public static class ConflictResearch
{
    private const string Game = "fieldsofmistria";

    /// <summary>
    /// Nexus's numeric id for Fields of Mistria. The site is addressed by domain name almost
    /// everywhere, but the host that serves mod readmes is keyed by this instead.
    /// </summary>
    private const int GameId = 6685;

    /// <summary>
    /// How many pages of the bug list to read, at the site's own ten per page. Thirty reports is
    /// more than almost any mod here has; the cap exists so a famously buggy one cannot turn a
    /// single research run into fifty requests.
    /// </summary>
    private const int BugListPages = 3;

    /// <summary>
    /// How many bug reports to open the replies of. Each is its own request, and the answer is
    /// almost always in the first couple that looked relevant.
    /// </summary>
    private const int MaxBugsOpened = 6;

    /// <summary>Quotes kept from one mod's description, before the user is better off just reading it.</summary>
    private const int MaxDescriptionQuotes = 4;

    /// <summary>Posts kept per tab, per mod.</summary>
    private const int MaxDiscussionQuotes = 4;

    /// <summary>
    /// How many pages of *search results* to take per term.
    ///
    /// The thread search returns matches twenty at a time like any other page, so a term as common
    /// as a popular mod's name used to be truncated at twenty and the rest silently dropped. Three
    /// pages is sixty comments about one pairing, which is more than anyone will read.
    /// </summary>
    private const int SearchResultPages = 3;

    /// <summary>
    /// Reads what each mod's page says, works out what it means, and looks for an existing fix.
    /// </summary>
    /// <param name="mods">
    /// The mods in conflict, each with its Nexus mod id where AIM knows one. A mod with no id can
    /// still appear in the search link; it just has no page to read.
    /// </param>
    /// <param name="session">
    /// Whose eyes to read the pages with. <see cref="NexusSession.Anonymous"/> sees what a
    /// signed-out visitor sees, which is most of it.
    /// </param>
    public static async Task<ResearchResult> InvestigateAsync(
        IReadOnlyList<ResearchSubject> mods,
        NexusApiClient? client,
        HttpClient? pageReader = null,
        CancellationToken ct = default,
        NexusSession? session = null)
    {
        session ??= NexusSession.Anonymous;

        var findings = new List<ResearchFinding>();

        // Mod ids linked from each page, kept per mod so the ones linked from *every* page can be
        // picked out afterwards. That intersection is what finds a compatibility patch.
        var linkedFromEachPage = new List<Dictionary<int, NexusRelatedMod>>();
        var patches = new List<PatchCandidate>();

        foreach (var mod in mods.Where(mod => mod.ModId is not null))
        {
            var others = mods
                .Where(other => !ReferenceEquals(other, mod))
                .Select(other => other.Name)
                .ToList();

            var page = PageOf(mod);

            if (client is not null)
            {
                findings.AddRange(await ReadTheApiAsync(client, mod, page, others, ct));
                patches.AddRange(await ReadOptionalFilesAsync(client, mod, page, others, ct));
            }

            if (pageReader is not null)
            {
                findings.AddRange(await ScanDiscussionAsync(mod, others, pageReader, session, ct));
                findings.AddRange(await ReadTheReadmeAsync(mod, others, page, pageReader, session, ct));
                linkedFromEachPage.Add(await ReadLinkedModsAsync(mod, page, pageReader, session, ct));
            }
        }

        patches.AddRange(FindPatchesLinkedFromEveryPage(linkedFromEachPage, mods));

        return new ResearchResult(Rank(findings), BuildLinks(mods))
        {
            Patches = patches
                .GroupBy(patch => patch.ModId)
                .Select(group => group.OrderBy(patch => patch.Confidence).First())
                .OrderBy(patch => patch.Confidence)
                .ToList()
        };
    }

    private static string PageOf(ResearchSubject mod) =>
        mod.PageUrl ?? $"https://www.nexusmods.com/{Game}/mods/{mod.ModId}";

    /// <summary>
    /// Blockers first, then clearances, then everything else - and within each, quotes that name
    /// the other mod ahead of quotes that merely discuss the subject.
    ///
    /// The old result was in whatever order the pages happened to be read, so on a mod with a
    /// chatty comment thread the one sentence that answered the question could be twentieth. What
    /// the user is deciding is whether to dismiss the issue, so the two kinds of evidence that
    /// settle it belong at the top.
    /// </summary>
    private static List<ResearchFinding> Rank(List<ResearchFinding> findings) =>
        findings
            .OrderBy(finding => finding.Polarity switch
            {
                Polarity.Blocker => 0,
                Polarity.Clearance => 1,
                Polarity.Caution => 2,
                _ => 3
            })
            .ThenByDescending(finding => finding.NamesTheOtherMod)
            .ToList();

    // ── What the API will tell us ────────────────────────────────────────────────

    /// <summary>
    /// The description, the summary and the release notes.
    ///
    /// Release notes are read because "fixed a conflict with X" is a changelog line far more often
    /// than it is a description line - an author who solved a problem two versions ago has usually
    /// long since deleted the warning from the description, and the changelog is the only place the
    /// history survives.
    /// </summary>
    private static async Task<List<ResearchFinding>> ReadTheApiAsync(
        NexusApiClient client,
        ResearchSubject mod,
        string page,
        IReadOnlyList<string> others,
        CancellationToken ct)
    {
        var findings = new List<ResearchFinding>();

        try
        {
            var overview = await client.GetModOverviewAsync(Game, mod.ModId!.Value, ct);
            if (overview is not null)
                findings.AddRange(Scan(mod, $"{overview.Summary}\n{overview.Description}", others,
                    page, "description"));
        }
        catch (Exception exception)
        {
            Logger.Log($"Conflict research skipped the description of {mod.Name}: {exception.Message}");
        }

        try
        {
            var changelogs = await client.GetChangelogsAsync(Game, mod.ModId!.Value, ct);

            // Only the recent few. A mod with forty releases has forty changelogs, and a conflict
            // fixed in version 0.2 is not what the user is looking at today.
            //
            // Held to the stricter bar, because a version history is where the word "patch" means
            // something else. "Updated for the v0.11.7 patch" appears in almost every changelog
            // and says nothing whatever about the two mods in front of the user; only a stated
            // verdict, or a line naming the other mod, earns a place here.
            foreach (var entry in changelogs.Take(8))
                findings.AddRange(Scan(mod, entry.Text, others, $"{page}?tab=logs",
                    $"changelog for {entry.Version}", strict: true));
        }
        catch (Exception exception)
        {
            Logger.Log($"Conflict research skipped the changelog of {mod.Name}: {exception.Message}");
        }

        return findings;
    }

    /// <summary>
    /// Optional and miscellaneous files whose own names say they are compatibility patches.
    ///
    /// Authors publish the fix as a second file on the same page at least as often as they publish
    /// it as a separate mod, and that file is invisible to everything else here: it is not in the
    /// description, nobody links it, and the main download is the one AIM already has.
    /// </summary>
    private static async Task<List<PatchCandidate>> ReadOptionalFilesAsync(
        NexusApiClient client,
        ResearchSubject mod,
        string page,
        IReadOnlyList<string> others,
        CancellationToken ct)
    {
        try
        {
            var files = await client.GetFilesAsync(Game, mod.ModId!.Value, ct);

            return files
                .Where(file => !file.Category.Equals("MAIN", StringComparison.OrdinalIgnoreCase))
                .Where(file => LooksLikeAPatch(file.Name) ||
                               others.Any(other => ModNameMatcher.Mentions(file.Name, other)))
                .Select(file => new PatchCandidate(
                    mod.ModId!.Value,
                    $"{mod.Name} — {file.Name}",
                    $"{page}?tab=files",
                    WhyThisFile(mod, file, others),
                    PatchConfidence.OptionalFile) { FileId = file.FileId })
                .ToList();
        }
        catch (Exception exception)
        {
            Logger.Log($"Conflict research skipped the file list of {mod.Name}: {exception.Message}");
            return [];
        }
    }

    /// <summary>
    /// Why AIM is putting this file in front of the user.
    ///
    /// "An optional file on X's own page, filed under ARCHIVED" was true and useless: it described
    /// where the file sits and never said what it is or what it has to do with the conflict, so a
    /// recolour and a compatibility patch read identically. Three things fix that, in the order the
    /// user needs them - what made AIM look at it, what the author says it is, and what its
    /// category means for whether it is safe to apply today.
    /// </summary>
    private static string WhyThisFile(ResearchSubject mod, NexusFileInfo file, IReadOnlyList<string> others)
    {
        var named = others.FirstOrDefault(other => ModNameMatcher.Mentions(file.Name, other));

        // The two reasons this file was picked are worth telling apart. A file naming the other mod
        // is a near-certain patch; a file merely carrying the word "fix" or "compat" may be a fix
        // for something else entirely, and saying so is what stops the user trusting it blindly.
        var why = named is not null
            ? $"An optional file on {mod.Name}'s own page whose name mentions {named}, the other " +
              "mod in this conflict - which is how authors label a download meant to be installed " +
              "alongside it."
            : $"An optional file on {mod.Name}'s own page whose name reads like a patch or a " +
              "compatibility file. AIM has not confirmed that it is for this pairing specifically, " +
              "so read the description before applying it.";

        if (Blurb(file.Description) is { } blurb)
            why += $" The author's own note on it: \"{blurb}\"";

        return $"{why} {CategoryNote(file.Category)}";
    }

    /// <summary>What Nexus's category tells the user about applying the file.</summary>
    private static string CategoryNote(string category) => category.ToUpperInvariant() switch
    {
        "OPTIONAL" => "Nexus files it under Optional: an extra download rather than part of the mod itself.",
        "UPDATE" => "Nexus files it under Update: meant to go on top of the main file you already have.",
        "MISCELLANEOUS" => "Nexus files it under Miscellaneous, the catch-all authors use for extras and patches alike.",

        // Worth a warning rather than a label. An archived file is one the author has retired, and
        // it commonly targets a version of the mod nobody is running any more.
        "ARCHIVED" or "OLD_VERSION" =>
            "Nexus files it under Archived, meaning the author has retired it - it may be built " +
            "for an older version of the mod, so check its version against the one you have.",

        "" => "",
        _ => $"Nexus files it under {category}."
    };

    /// <summary>
    /// The author's file note, cut down to something that fits under a heading: tags stripped,
    /// whitespace collapsed, and trimmed at a word to about a line and a half of prose.
    /// </summary>
    private static string? Blurb(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;

        var text = Regex.Replace(html, "<[^>]+>", " ");
        text = WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"\s+", " ").Trim();

        if (text.Length == 0) return null;
        if (text.Length <= 220) return text;

        var cut = text[..220];
        var space = cut.LastIndexOf(' ');

        return (space > 120 ? cut[..space] : cut).TrimEnd(',', '.', ';', ':') + "...";
    }

    private static readonly Regex PatchTitle = new(
        @"\b(patch|compat\w*|fix(es)?|bridge|interop|conflict)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static bool LooksLikeAPatch(string title) =>
        !string.IsNullOrWhiteSpace(title) && PatchTitle.IsMatch(title);

    // ── What the pages will tell us ──────────────────────────────────────────────

    /// <summary>
    /// Reads a mod's bug reports and comments and keeps the posts that bear on this conflict.
    ///
    /// Far more selective than the description scan. A description is a few paragraphs by the
    /// author; a comment thread is hundreds of posts by everyone, most of them "love this mod".
    /// Only a post that names one of the other mods, or that states an actual verdict, earns a
    /// place - anything looser would bury the useful ones.
    /// </summary>
    private static async Task<List<ResearchFinding>> ScanDiscussionAsync(
        ResearchSubject mod,
        IReadOnlyList<string> otherNames,
        HttpClient http,
        NexusSession session,
        CancellationToken ct)
    {
        var findings = new List<ResearchFinding>();
        var page = PageOf(mod);

        findings.AddRange(await SearchCommentsAsync(mod, otherNames, page, http, session, ct));
        findings.AddRange(await ReadBugsAsync(mod, otherNames, page, http, session, ct));

        return findings;
    }

    /// <summary>
    /// Searches a mod's comment thread for the other mods in the conflict.
    ///
    /// The thread is searchable, and that changes what is possible here. Reading pages from the
    /// front samples the newest twenty comments, which are about the mod's latest release; the one
    /// that answers "does this work with X?" is usually months old and twelve pages back. Searching
    /// for the other mod's name returns exactly those comments, out of the entire thread, in one
    /// request - and it works signed out.
    ///
    /// The first page is still read unsearched, because the author's own statements about
    /// compatibility are usually pinned to the top and mention no other mod by name.
    /// </summary>
    private static async Task<List<ResearchFinding>> SearchCommentsAsync(
        ResearchSubject mod,
        IReadOnlyList<string> otherNames,
        string page,
        HttpClient http,
        NexusSession session,
        CancellationToken ct)
    {
        var findings = new List<ResearchFinding>();
        var source = $"{page}?tab=posts";

        var tabHtml = await NexusPageReader.FetchAsync(http, source, session, ct);
        if (tabHtml is null) return findings;

        var posts = NexusPageReader.Extract(tabHtml, "posts");
        var threadId = NexusPageReader.ExtractCommentThreadId(tabHtml);

        if (threadId is not null)
            foreach (var term in SearchTermsFor(otherNames))
            {
                await Task.Delay(250, ct);

                posts.AddRange(await NexusPageReader.SearchCommentsAsync(
                    http, GameId, mod.ModId!.Value, threadId.Value, term, session, ct,
                    SearchResultPages));
            }

        return Keep(posts, mod, otherNames, source, TabLabel("posts"), findings);
    }

    /// <summary>
    /// What to search a comment thread for: each other mod's most distinctive word.
    ///
    /// One term per mod rather than its whole title, because a thread search matches the phrase as
    /// typed and nobody types a mod's full name in a comment. The longest distinctive word is the
    /// one least likely to be a coincidence, and whatever it turns up is judged afterwards by
    /// <see cref="ModNameMatcher"/> anyway.
    /// </summary>
    private static IEnumerable<string> SearchTermsFor(IReadOnlyList<string> names) =>
        names
            .Select(name => ModNameMatcher.DistinctiveWords(name)
                .OrderByDescending(word => word.Length)
                .FirstOrDefault())
            .Where(word => word is { Length: >= 4 })
            .Select(word => word!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3);

    /// <summary>
    /// Reads the bug tracker, with the author's ruling on each report.
    ///
    /// Two things it did not do before. It reads the *status*, so a report closed as "not a bug" is
    /// filed as evidence against the conflict rather than as another complaint - which is the
    /// opposite of what the list alone suggests. And it opens the reply threads, where an
    /// incompatibility is usually pinned down, rather than reading only the titles.
    ///
    /// Only reports worth opening are opened. Fetching every thread is one request each and a busy
    /// mod has dozens; a report whose own title and status say nothing relevant almost never turns
    /// out to hide the answer.
    /// </summary>
    private static async Task<List<ResearchFinding>> ReadBugsAsync(
        ResearchSubject mod,
        IReadOnlyList<string> otherNames,
        string page,
        HttpClient http,
        NexusSession session,
        CancellationToken ct)
    {
        var findings = new List<ResearchFinding>();
        var source = $"{page}?tab=bugs";
        var bugs = new List<NexusBugSummary>();

        for (var listPage = 1; listPage <= BugListPages; listPage++)
        {
            var batch = await NexusBugReader.ReadBugListAsync(
                http, GameId, mod.ModId!.Value, listPage, session, ct);

            if (batch.Count == 0) break;

            bugs.AddRange(batch);
            if (listPage < BugListPages) await Task.Delay(250, ct);
        }

        var opened = 0;

        foreach (var bug in bugs)
        {
            var named = ModNameMatcher.FirstMentioned(bug.Title, otherNames);
            var titleSignal = CompatibilityLanguage.ClassifyDiscussion(bug.Title);

            // A report earns a request when its title names the other mod or states something, and
            // separately when the author has ruled on it either way - a dismissal is worth reading
            // even when the title is unremarkable, because it is the ruling that carries.
            var ruling = NexusBugReader.WeighState(bug.State);
            var worthOpening = named is not null || titleSignal is not null ||
                               ruling is Polarity.Clearance or Polarity.Blocker;

            if (!worthOpening || bug.Replies == 0)
            {
                // A bug report is allowed to count on the name alone, unlike a description: a
                // report filed against this mod whose title names the other one *is* somebody
                // saying the two of them went wrong together, whatever words they used.
                if (named is not null || CompatibilityLanguage.BearsOnThePairing(titleSignal, false))
                    findings.Add(BugFinding(mod, bug, bug.Title, named, titleSignal, ruling, source));
                continue;
            }

            if (opened >= MaxBugsOpened) continue;
            opened++;

            await Task.Delay(250, ct);

            foreach (var post in await NexusBugReader.ReadBugRepliesAsync(http, bug.IssueId, session, ct))
            {
                var mentioned = ModNameMatcher.FirstMentioned(post.Text, otherNames);
                var signal = mentioned is not null
                    ? CompatibilityLanguage.Classify(post.Text)
                    : CompatibilityLanguage.ClassifyDiscussion(post.Text);

                if (mentioned is null && !CompatibilityLanguage.BearsOnThePairing(signal, false)) continue;

                findings.Add(BugFinding(mod, bug, post.Text, mentioned, signal, ruling, source, post.Author));
            }
        }

        return findings;
    }

    /// <summary>
    /// One quote from the bug tracker, with the report's status folded into what it means.
    ///
    /// The status outranks the words: a comment saying "this crashes with X" inside a report the
    /// author closed as "not a bug" is not evidence of a conflict, and reporting it as one is how
    /// the tracker misleads people.
    /// </summary>
    private static ResearchFinding BugFinding(
        ResearchSubject mod,
        NexusBugSummary bug,
        string text,
        string? named,
        CompatibilitySignal? signal,
        Polarity? ruling,
        string source,
        string author = "")
    {
        var polarity = ruling switch
        {
            Polarity.Clearance => Polarity.Clearance,
            Polarity.Blocker when signal?.Polarity == Polarity.Clearance => Polarity.Caution,
            _ => signal?.Polarity ?? Polarity.Context
        };

        var reason = string.Join(", ", new[]
        {
            named is not null ? $"names \"{named}\"" : null,
            signal?.Reason,
            $"bug report marked \"{Describe(bug.State)}\""
        }.Where(part => part is not null));

        var who = string.IsNullOrWhiteSpace(author) ? mod.Name : $"{mod.Name} — {author}";

        return new ResearchFinding(who, Trim(text), reason, source)
        {
            Polarity = polarity,
            Where = $"bug report \"{Trim(bug.Title, 60)}\"",
            NamesTheOtherMod = named is not null,
            FullQuote = text
        };
    }

    private static string Describe(BugState state) => state switch
    {
        BugState.New => "new issue",
        BugState.Known => "known issue",
        BugState.BeingLookedAt => "being looked at",
        BugState.Fixed => "fixed",
        BugState.Duplicate => "duplicate",
        BugState.NotABug => "not a bug",
        BugState.WontFix => "won't fix",
        BugState.NeedMoreInfo => "need more info",
        _ => "no status"
    };

    /// <summary>Turns posts into findings, capped so one chatty thread cannot fill the window.</summary>
    private static List<ResearchFinding> Keep(
        IEnumerable<NexusPagePost> posts,
        ResearchSubject mod,
        IReadOnlyList<string> otherNames,
        string source,
        string where,
        List<ResearchFinding> into)
    {
        var kept = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var post in posts)
        {
            if (!seen.Add(post.Text)) continue;

            var named = ModNameMatcher.FirstMentioned(post.Text, otherNames);
            var signal = named is not null
                ? CompatibilityLanguage.Classify(post.Text)
                : CompatibilityLanguage.ClassifyDiscussion(post.Text);

            if (!CompatibilityLanguage.BearsOnThePairing(signal, named is not null)) continue;

            var who = string.IsNullOrWhiteSpace(post.Author) ? mod.Name : $"{mod.Name} — {post.Author}";

            // Shortened for display like every other quote - a comment runs to six hundred
            // characters - but the whole post travels with it, so the window can show all of it
            // without going back to Nexus.
            into.Add(new ResearchFinding(who, Trim(post.Text),
                named is not null ? $"names \"{named}\" and {signal!.Reason}" : signal!.Reason, source)
            {
                Polarity = signal.Polarity,
                Where = where,
                NamesTheOtherMod = named is not null,
                FullQuote = post.Text
            });

            if (++kept >= MaxDiscussionQuotes) break;
        }

        return into;
    }

    /// <summary>
    /// The readme behind the mod's Docs tab.
    ///
    /// A tab the researcher used to ignore entirely, and the one place a careful author writes
    /// things down properly - installation order, what the mod takes over, which other mods it has
    /// been tested against. Nexus serves it as a plain text file, so unlike everything else read
    /// off the site there is no markup to get wrong.
    ///
    /// Strict, for the same reason the changelog is: a readme is documentation, and documentation
    /// says "replace" and "patch" constantly about things that have nothing to do with another mod.
    /// </summary>
    private static async Task<List<ResearchFinding>> ReadTheReadmeAsync(
        ResearchSubject mod,
        IReadOnlyList<string> others,
        string page,
        HttpClient http,
        NexusSession session,
        CancellationToken ct)
    {
        var readme = await NexusPageReader.ReadReadmeAsync(http, GameId, mod.ModId!.Value, session, ct);

        return readme is null
            ? []
            : Scan(mod, readme, others, $"{page}?tab=docs", "readme", strict: true).ToList();
    }

    /// <summary>
    /// Every mod linked from this mod's page, by id. Used only for the intersection.
    /// </summary>
    private static async Task<Dictionary<int, NexusRelatedMod>> ReadLinkedModsAsync(
        ResearchSubject mod,
        string page,
        HttpClient http,
        NexusSession session,
        CancellationToken ct)
    {
        var html = await NexusPageReader.FetchAsync(http, $"{page}?tab=description", session, ct);
        if (html is null) return [];

        return NexusPageReader.ExtractRequiringMods(html, Game)
            .Where(related => related.ModId != mod.ModId)
            .GroupBy(related => related.ModId)
            .ToDictionary(group => group.Key, group => group.First());
    }

    /// <summary>
    /// The mods every page in the conflict links to.
    ///
    /// Two unrelated mods rarely link to the same third mod for unrelated reasons; when they do, it
    /// is nearly always a shared requirement - MOMI, a framework - or the patch that joins them.
    /// The shared requirements are the ones already installed, so what survives this filter is
    /// overwhelmingly the thing being looked for.
    /// </summary>
    private static List<PatchCandidate> FindPatchesLinkedFromEveryPage(
        List<Dictionary<int, NexusRelatedMod>> perPage,
        IReadOnlyList<ResearchSubject> mods)
    {
        if (perPage.Count < 2) return [];

        var conflictIds = mods.Where(mod => mod.ModId is not null).Select(mod => mod.ModId!.Value).ToHashSet();

        var shared = perPage
            .Skip(1)
            .Aggregate(
                new HashSet<int>(perPage[0].Keys),
                (common, next) =>
                {
                    common.IntersectWith(next.Keys);
                    return common;
                });

        return shared
            .Where(id => !conflictIds.Contains(id))
            .Select(id => perPage[0][id])
            .Select(related => new PatchCandidate(
                related.ModId,
                related.Title.Length > 0 ? related.Title : $"Mod {related.ModId}",
                related.Url,
                LooksLikeAPatch(related.Title)
                    ? "Linked from every mod in this conflict, and its title says it is a patch."
                    : "Linked from every mod in this conflict.",
                PatchConfidence.LinkedFromBoth))
            .ToList();
    }

    private static string TabLabel(string tab) => tab == "bugs" ? "bug reports" : "comments";

    // ── Reading prose ────────────────────────────────────────────────────────────

    /// <param name="strict">
    /// Drop sentences that merely touch on the subject without stating anything. Used for text that
    /// is not the author writing about compatibility - a changelog, a readme - where the loose bar
    /// produces far more noise than signal.
    /// </param>
    private static IEnumerable<ResearchFinding> Scan(
        ResearchSubject mod,
        string text,
        IReadOnlyList<string> otherNames,
        string sourceUrl,
        string where,
        bool strict = false)
    {
        var plain = PlainText(text);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var sentence in Sentences(plain))
        {
            // A sentence naming one of the other mods is the strongest signal there is, whatever
            // words surround it - the author is talking about exactly this pairing.
            var named = ModNameMatcher.FirstMentioned(sentence, otherNames);

            var signal = strict && named is null
                ? CompatibilityLanguage.ClassifyDiscussion(sentence)
                : CompatibilityLanguage.Classify(sentence);

            // A sentence with no verdict is not a finding, however sure we are that it is about the
            // other mod. A description mentioning another mod in passing - "also includes a darker
            // recolour of the haunted attic" - says nothing about whether the two can be installed
            // together, and putting it in the window as evidence invites the user to read a claim
            // into it that nobody made.
            if (!CompatibilityLanguage.BearsOnThePairing(signal, named is not null)) continue;
            if (!seen.Add(sentence)) continue;

            var reason = named is not null
                ? $"names \"{named}\" and {signal!.Reason}"
                : signal!.Reason;

            yield return new ResearchFinding(mod.Name, Trim(sentence), reason, sourceUrl)
            {
                Polarity = signal!.Polarity,
                Where = where,
                NamesTheOtherMod = named is not null,
                FullQuote = sentence
            };

            if (seen.Count >= MaxDescriptionQuotes) yield break;
        }
    }

    /// <summary>
    /// The BBCode tags Nexus descriptions actually use, named rather than guessed at by shape.
    ///
    /// The old pattern took anything in square brackets up to forty characters long, which is wrong
    /// in both directions. A link tag carrying a full mod URL is well past forty, so
    /// <c>[url=https://www.nexusmods.com/fieldsofmistria/mods/669]</c> survived into the quote and
    /// was shown to the user as if the author had typed it; meanwhile prose in brackets - "[see the
    /// FAQ]" - was silently eaten. Matching on the tag name fixes both.
    /// </summary>
    private const string BbCodeTags =
        "url|b|i|u|s|size|color|colour|font|center|centre|right|left|justify|quote|spoiler|code|" +
        "list|img|youtube|video|table|tr|td|th|heading|line|hr|indent|sup|sub";

    private static readonly Regex BbCode = new(
        $@"\[/?(?:{BbCodeTags})(?:[=\s][^\]]{{0,300}})?\]|\[\*\]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Nexus descriptions are BBCode rendered to HTML. Neither is worth showing raw.</summary>
    public static string PlainText(string value)
    {
        var withoutTags = Regex.Replace(value, "<[^>]+>", " ");
        var withoutBbCode = BbCode.Replace(withoutTags, " ");
        return Regex.Replace(WebUtility.HtmlDecode(withoutBbCode), @"\s+", " ").Trim();
    }

    private static IEnumerable<string> Sentences(string text) =>
        Regex.Split(text, @"(?<=[.!?])\s+|\s*[\r\n]+\s*")
            .Select(sentence => sentence.Trim())
            .Where(sentence => sentence.Length >= 12);

    /// <summary>Shortens to a given length, for a label rather than a quote.</summary>
    private static string Trim(string text, int limit) =>
        text.Length <= limit ? text : text[..limit] + "…";

    /// <summary>
    /// A quote, not an excerpt. Three hundred characters of somebody's comment is a paragraph to
    /// read before the user learns whether it mattered, and the sentence that mattered is at the
    /// front of it far more often than not.
    /// </summary>
    private static string Trim(string sentence) =>
        sentence.Length <= 180 ? sentence : sentence[..180] + "…";

    // ── Where to look next ───────────────────────────────────────────────────────

    private static List<ResearchLink> BuildLinks(IReadOnlyList<ResearchSubject> mods)
    {
        var links = new List<ResearchLink>();

        foreach (var mod in mods)
        {
            var page = mod.PageUrl ?? (mod.ModId is null ? null : $"https://www.nexusmods.com/{Game}/mods/{mod.ModId}");
            if (page is null) continue;

            links.Add(new ResearchLink($"{mod.Name} — bugs", $"{page}?tab=bugs",
                "Reported problems, where an incompatibility usually surfaces first."));
            links.Add(new ResearchLink($"{mod.Name} — posts", $"{page}?tab=posts",
                "The comment thread, where authors answer \"does this work with…\" questions."));
            links.Add(new ResearchLink($"{mod.Name} — files", $"{page}?tab=files",
                "Optional files, where a compatibility patch would be published."));
        }

        links.Add(new ResearchLink("Search the web", WebSearchUrl(mods),
            "Everything outside Nexus: Discord logs, Reddit threads, wiki pages."));

        return links;
    }

    /// <summary>
    /// A search naming every mod involved plus the game, so the results are about this pairing
    /// rather than about either mod on its own.
    /// </summary>
    public static string WebSearchUrl(IReadOnlyList<ResearchSubject> mods)
    {
        var query = new StringBuilder("Fields of Mistria");
        foreach (var mod in mods) query.Append(" \"").Append(mod.Name).Append('"');
        query.Append(" conflict OR patch OR compatibility");

        return "https://duckduckgo.com/?q=" + Uri.EscapeDataString(query.ToString());
    }
}

/// <summary>One mod as the researcher needs to see it.</summary>
public sealed record ResearchSubject(string Name, int? ModId, string? PageUrl);
