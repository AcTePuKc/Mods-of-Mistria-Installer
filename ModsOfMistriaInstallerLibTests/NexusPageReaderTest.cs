using Garethp.ModsOfMistriaInstallerLib;

namespace ModsOfMistriaInstallerLibTests;

// Pulling posts out of a Nexus page's HTML. This is scraping, so the tests care most about what it
// does when the markup is not what it expected - the answer has to be "nothing", never "nonsense".
[TestFixture]
public class NexusPageReaderTest
{
    private const string CommentPage = """
        <html><body>
          <div class="comment">
            <div class="comment-user"><a href="/users/1">Roland</a></div>
            <div class="comment-content"><p>Does this work with <b>The Perfect Gift</b>? I get a crash on load.</p></div>
          </div>
          <div class="comment">
            <div class="comment-user"><a href="/users/2">milfhvnter</a></div>
            <div class="comment-content"><p>They are incompatible for now, a patch is coming.</p></div>
          </div>
        </body></html>
        """;

    // Reading past page one of a comment thread needs the thread's own id, because the widget the
    // site's pager calls is addressed by thread rather than by mod. It is written into the markup
    // several different ways depending on where it appears, so the reader is deliberately loose
    // about the separator.
    [TestCase(@"<div data-thread_id=""14624073"">", 14624073)]
    [TestCase(@"{""thread_id"":14624073,""page"":1}", 14624073)]
    [TestCase(@"RH_CommentContainer=game_id:6685,thread_id:14624073,page:2", 14624073)]
    [TestCase(@"var thread_id = 14624073;", 14624073)]
    public void ShouldFindTheCommentThreadIdHoweverItIsWritten(string html, int expected)
    {
        Assert.That(NexusPageReader.ExtractCommentThreadId(html), Is.EqualTo(expected));
    }

    // No thread id is a normal answer - a mod with no comments, or a layout change - and means the
    // first page is all anyone gets. It must never be a crash or a wrong number.
    [TestCase("")]
    [TestCase("<html><body>no comments here</body></html>")]
    [TestCase(@"<div data-thread=""not a number"">")]
    public void ShouldReturnNoThreadIdRatherThanGuessing(string html)
    {
        Assert.That(NexusPageReader.ExtractCommentThreadId(html), Is.Null);
    }

    [Test]
    public void ShouldPullOutEachPostAsPlainText()
    {
        var posts = NexusPageReader.Extract(CommentPage, "posts");

        Assert.Multiple(() =>
        {
            Assert.That(posts, Has.Exactly(2).Items);
            Assert.That(posts[0].Text, Is.EqualTo("Does this work with The Perfect Gift ? I get a crash on load."));
            Assert.That(posts[0].Tab, Is.EqualTo("posts"));
        });
    }

    [Test]
    public void ShouldAttributePostsToTheirAuthors()
    {
        var posts = NexusPageReader.Extract(CommentPage, "posts");

        Assert.Multiple(() =>
        {
            Assert.That(posts[0].Author, Is.EqualTo("Roland"));
            Assert.That(posts[1].Author, Is.EqualTo("milfhvnter"));
        });
    }

    // Pairing names to posts by their order in the list desynchronises as soon as anything yields a
    // name without a kept post - here, a "thanks!" too short to keep - and every later quote is
    // then put in the wrong person's mouth.
    [Test]
    public void ShouldNotMisattributeAPostAfterASkippedOne()
    {
        var posts = NexusPageReader.Extract("""
            <div class="comment-user">Alice</div>
            <div class="comment-content">thanks!</div>
            <div class="comment-user">Bob</div>
            <div class="comment-content">This is incompatible with Remind Me, sadly.</div>
            """, "posts");

        Assert.Multiple(() =>
        {
            Assert.That(posts, Has.Exactly(1).Items);
            Assert.That(posts[0].Author, Is.EqualTo("Bob"));
        });
    }

    // The whole safety argument: a layout change must produce no posts, not wrong ones.
    [Test]
    public void ShouldFindNothingInMarkupItDoesNotRecognise()
    {
        var posts = NexusPageReader.Extract(
            "<html><body><div class=\"something-else\">a comment nobody can find</div></body></html>", "posts");

        Assert.That(posts, Is.Empty);
    }

    [Test]
    public void ShouldSurviveEmptyAndMalformedInput()
    {
        Assert.Multiple(() =>
        {
            Assert.That(NexusPageReader.Extract("", "posts"), Is.Empty);
            Assert.That(NexusPageReader.Extract("<div class=\"comment-content\">", "posts"), Is.Empty);
        });
    }

    // Script bodies survive a naive tag strip as a wall of JavaScript.
    [Test]
    public void ShouldNotTreatScriptContentAsProse()
    {
        var posts = NexusPageReader.Extract(
            "<div class=\"comment-content\"><script>var x = 'incompatible with everything';</script>" +
            "They are incompatible, use the patch instead.</div>", "posts");

        Assert.Multiple(() =>
        {
            Assert.That(posts, Has.Exactly(1).Items);
            Assert.That(posts[0].Text, Does.Not.Contain("var x"));
            Assert.That(posts[0].Text, Does.StartWith("They are incompatible"));
        });
    }

    [Test]
    public void ShouldIgnoreVeryShortPosts()
    {
        var posts = NexusPageReader.Extract("<div class=\"comment-content\">thanks!</div>", "posts");

        Assert.That(posts, Is.Empty);
    }

    [Test]
    public void ShouldReadBugTrackerMarkupToo()
    {
        var posts = NexusPageReader.Extract(
            "<div class=\"bug-content\">Crashes when Remind Me is also installed.</div>", "bugs");

        Assert.Multiple(() =>
        {
            Assert.That(posts, Has.Exactly(1).Items);
            Assert.That(posts[0].Tab, Is.EqualTo("bugs"));
        });
    }
}
