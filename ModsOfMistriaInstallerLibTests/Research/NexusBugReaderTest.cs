using Garethp.ModsOfMistriaInstallerLib.Research;

namespace ModsOfMistriaInstallerLibTests.Research;

// Reading a mod's bug tracker, which used to be read as a flat list of complaints.
//
// The status is the point. "These two mods crash together" and "these two mods crash together —
// closed, not a bug" are opposite answers, and the second is the more useful of the two: somebody
// already investigated this exact pairing and found nothing wrong. Reporting them identically is
// how a bug tracker misleads people.
[TestFixture]
public class NexusBugReaderTest
{
    // The 2026 layout, trimmed to what the reader looks at.
    private const string BugTab = """
        <table class="table forum-bugs flex-table">
          <tr><th class="table-bug-title sorter-false">Bug title</th>
              <th class="table-bug-status sorter-false">Status</th></tr>
          <tr id="issue_1119998" data-issue-id="1119998" class="mod-issue-row">
            <td class="table-bug-title"><a class="issue-title">Can't install - error</a></td>
            <td class="table-bug-status">New issue</td>
            <td class="table-bug-replies">4</td>
          </tr>
          <tr id="issue_998124" data-issue-id="998124" class="mod-issue-row">
            <td class="table-bug-title"><a class="issue-title">Crashes with Witchy Decor</a></td>
            <td class="table-bug-status">Not a bug</td>
            <td class="table-bug-replies">2</td>
          </tr>
        </table>
        """;

    [Test]
    public void ShouldReadEachReportWithItsRuling()
    {
        var bugs = NexusBugReader.ExtractBugs(BugTab);

        Assert.That(bugs, Has.Exactly(2).Items, "the header row is not a bug report");
        Assert.Multiple(() =>
        {
            Assert.That(bugs[0].IssueId, Is.EqualTo(1119998));
            Assert.That(bugs[0].Title, Is.EqualTo("Can't install - error"));
            Assert.That(bugs[0].State, Is.EqualTo(BugState.New));
            Assert.That(bugs[0].Replies, Is.EqualTo(4));

            Assert.That(bugs[1].IssueId, Is.EqualTo(998124));
            Assert.That(bugs[1].State, Is.EqualTo(BugState.NotABug));
        });
    }

    // Every status the site offers, taken from its own filter dropdown.
    [TestCase("New issue", BugState.New)]
    [TestCase("Known issues", BugState.Known)]
    [TestCase("Being looked at", BugState.BeingLookedAt)]
    [TestCase("Fixed", BugState.Fixed)]
    [TestCase("Duplicate", BugState.Duplicate)]
    [TestCase("Not a bug", BugState.NotABug)]
    [TestCase("Won't fix", BugState.WontFix)]
    [TestCase("Need more info", BugState.NeedMoreInfo)]
    [TestCase("Something new Nexus invented", BugState.Unknown)]
    public void ShouldRecogniseEveryStatusTheSiteOffers(string status, BugState expected)
    {
        var html = $"""
            <tr data-issue-id="1"><td class="table-bug-title">x</td>
            <td class="table-bug-status">{status}</td><td class="table-bug-replies">0</td></tr>
            """;

        Assert.That(NexusBugReader.ExtractBugs(html).Single().State, Is.EqualTo(expected));
    }

    // A dismissed report argues *against* the conflict; an acknowledged one argues for it. A fixed
    // or duplicated one argues neither way and has to earn its place on its own words.
    [TestCase(BugState.NotABug, Polarity.Clearance)]
    [TestCase(BugState.Known, Polarity.Blocker)]
    [TestCase(BugState.WontFix, Polarity.Blocker)]
    [TestCase(BugState.New, Polarity.Caution)]
    [TestCase(BugState.BeingLookedAt, Polarity.Caution)]
    public void ShouldWeighAReportByWhatTheAuthorDecided(BugState state, Polarity expected)
    {
        Assert.That(NexusBugReader.WeighState(state), Is.EqualTo(expected));
    }

    [TestCase(BugState.Fixed)]
    [TestCase(BugState.Duplicate)]
    [TestCase(BugState.NeedMoreInfo)]
    [TestCase(BugState.Unknown)]
    public void ShouldReadNothingIntoAStatusThatSettlesNothing(BugState state)
    {
        Assert.That(NexusBugReader.WeighState(state), Is.Null);
    }

    // Scraping, so the important behaviour is what it does when the markup is not what it expected:
    // nothing, never nonsense.
    [TestCase("")]
    [TestCase("<html><body>Nexus redesigned the bugs tab</body></html>")]
    [TestCase("<tr data-issue-id=\"not-a-number\"><td class=\"table-bug-status\">New issue</td></tr>")]
    public void ShouldReturnNothingRatherThanNonsense(string html)
    {
        Assert.That(NexusBugReader.ExtractBugs(html), Is.Empty);
    }

    // A row with no status or reply count is still a real report and must not be dropped.
    [Test]
    public void ShouldKeepAReportWhoseColumnsAreMissing()
    {
        var bugs = NexusBugReader.ExtractBugs(
            """<tr data-issue-id="42"><td class="table-bug-title">Bare row</td></tr>""");

        Assert.Multiple(() =>
        {
            Assert.That(bugs.Single().IssueId, Is.EqualTo(42));
            Assert.That(bugs.Single().State, Is.EqualTo(BugState.Unknown));
            Assert.That(bugs.Single().Replies, Is.Zero);
        });
    }
}
