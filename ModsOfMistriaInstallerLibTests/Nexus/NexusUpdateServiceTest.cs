using Garethp.ModsOfMistriaInstallerLib.Nexus;
using ModsOfMistriaInstallerLibTests.Fixtures;

namespace ModsOfMistriaInstallerLibTests.Nexus;

// Version comparison for update checks. Mod versions are written by hand, so this has to cope with
// more than well-formed semver.
[TestFixture]
public class NexusUpdateServiceTest
{
    [TestCase("1.0", "1.1", ExpectedResult = true, TestName = "a higher minor")]
    [TestCase("1.9", "1.10", ExpectedResult = true, TestName = "ten sorts above nine")]
    [TestCase("1.2.3", "1.2.4", ExpectedResult = true, TestName = "a higher patch")]
    [TestCase("v1.0", "v1.1", ExpectedResult = true, TestName = "a leading v")]
    [TestCase("1.2", "1.2", ExpectedResult = false, TestName = "the same version")]
    [TestCase("2.0", "1.9", ExpectedResult = false, TestName = "an older release")]
    [TestCase("1.2", "1.2.0", ExpectedResult = false, TestName = "a trailing zero")]
    [TestCase("1.0", "", ExpectedResult = false, TestName = "no candidate version")]
    [TestCase("", "1.0", ExpectedResult = true, TestName = "nothing installed to compare")]
    [TestCase("alpha", "beta", ExpectedResult = true, TestName = "unparseable versions that differ")]
    [TestCase("alpha", "alpha", ExpectedResult = false, TestName = "unparseable versions that match")]
    public bool ShouldCompareVersions(string installed, string candidate) =>
        NexusUpdateService.IsVersionNewer(installed, candidate);

    // ── Ordering a changelog ─────────────────────────────────────────────────────

    [Test]
    public void ShouldOrderVersionsNewestFirst()
    {
        var versions = new List<string> { "1.9", "1.10", "1.2.0", "2.0" };
        versions.Sort(NexusUpdateService.CompareVersionsNewestFirst);

        Assert.That(versions, Is.EqualTo(new[] { "2.0", "1.10", "1.9", "1.2.0" }));
    }

    // Asking IsVersionNewer in both directions says "newer" both ways for versions with no digits,
    // which is an inconsistent comparison and something List.Sort is entitled to throw over.
    [Test]
    public void ShouldOrderNonNumericVersionsWithoutContradictingItself()
    {
        Assert.Multiple(() =>
        {
            var forward = NexusUpdateService.CompareVersionsNewestFirst("hotfix", "beta");
            var backward = NexusUpdateService.CompareVersionsNewestFirst("beta", "hotfix");

            Assert.That(forward, Is.Not.EqualTo(0));
            Assert.That(Math.Sign(forward), Is.EqualTo(-Math.Sign(backward)));
            Assert.That(NexusUpdateService.CompareVersionsNewestFirst("beta", "beta"), Is.EqualTo(0));
        });
    }

    [Test]
    public void ShouldSortAWholeChangelogWithMixedVersionsWithoutThrowing()
    {
        var versions = new List<string> { "3", "hotfix", "1.0", "beta", "2.5", "" };

        Assert.DoesNotThrow(() => versions.Sort(NexusUpdateService.CompareVersionsNewestFirst));
    }

    // ── Which file an installed one is judged against ────────────────────────────

    private static NexusFileInfo File(
        int fileId, string category, string version, int uploadedDaysAgo = 0, bool primary = false) =>
        new(fileId, $"file-{fileId}.zip", $"File {fileId}", version, 1024)
        {
            Category = category,
            IsPrimary = primary,
            UploadedAt = DateTimeOffset.UtcNow.AddDays(-uploadedDaysAgo)
        };

    private static NexusInstallRecord Record(int fileId, string? version) =>
        new("fieldsofmistria", 703, fileId, "file.zip", version, DateTimeOffset.UtcNow);

    private static MockMod Mod(string version) =>
        new(["manifest.json"]) { Version = version };

    // The bug this exists to prevent: an author numbers the mod page "3" and the manifest "1.0.2",
    // so comparing the two declared an update that installing could never satisfy.
    [Test]
    public void ShouldTrustTheFileIdOverMismatchedVersionSchemes()
    {
        var installed = File(6317, "MAIN", "3");
        var record = Record(6317, "3");

        Assert.That(
            NexusUpdateService.IsNewer(record, Mod("1.0.2"), installed, installed),
            Is.False, "the very file that is installed is not an update to itself");
    }

    [Test]
    public void ShouldReportAGenuinelyNewerFile()
    {
        var installed = File(6317, "MAIN", "3", uploadedDaysAgo: 10);
        var newer = File(6400, "MAIN", "4", uploadedDaysAgo: 1);

        Assert.That(
            NexusUpdateService.IsNewer(Record(6317, "3"), Mod("1.0.2"), newer, installed),
            Is.True);
    }

    // A withdrawn newest file must not turn into an "update" that is really a downgrade.
    [Test]
    public void ShouldNotReportAnOlderFileAsAnUpdate()
    {
        var installed = File(6400, "MAIN", "4", uploadedDaysAgo: 1);
        var older = File(6317, "MAIN", "3", uploadedDaysAgo: 10);

        Assert.That(
            NexusUpdateService.IsNewer(Record(6400, "4"), Mod("4"), older, installed),
            Is.False);
    }

    // Authors tidy old files off their pages, so the recorded file often is not listed any more.
    // Assuming that means "update" produced a list of mods whose versions had not moved at all.
    [Test]
    public void ShouldNotInventAnUpdateWhenTheInstalledFileIsNoLongerListed()
    {
        var latest = File(6400, "MAIN", "1.2", uploadedDaysAgo: 1);

        Assert.That(
            NexusUpdateService.IsNewer(Record(4169, "1.2"), Mod("1.2.0"), latest, installed: null),
            Is.False);
    }

    // A replaced archive at the same version is not a release. "2.0 → 2.0" in an update list reads
    // as a bug, and acting on it gains the user nothing.
    [Test]
    public void ShouldNotReportAReuploadAtTheSameVersion()
    {
        var installed = File(6166, "MAIN", "2.0", uploadedDaysAgo: 30);
        var reuploaded = File(6900, "MAIN", "2.0", uploadedDaysAgo: 1);

        Assert.That(
            NexusUpdateService.IsNewer(Record(6166, "2.0"), Mod("2.0"), reuploaded, installed),
            Is.False);
    }

    // Every pairing from a real "Update 14 mods?" prompt that should never have been in it. The
    // left value is what AIM recorded from Nexus, the right is what the page offers now.
    [TestCase("1.2", "1.2", TestName = "Effe's Dig and Dive Site Marker")]
    [TestCase("1", "1", TestName = "Jun's Mad Alchemist")]
    [TestCase("2.0", "2.0", TestName = "Twin's Bathroom Charcoal")]
    [TestCase("1.0.0", "1.0.0", TestName = "Dekunii's Dark Purple Stairs")]
    [TestCase("v1.1", "1.1", TestName = "Fancier Obsidian Furniture, with a leading v")]
    [TestCase("1.0.5", "1", TestName = "Farmer of Dubious Repute, already ahead")]
    [TestCase("2.0.1", "2.0.1", TestName = "Weather Crystal Ball, manifest lagging behind")]
    public void ShouldNotFlagTheseAsUpdates(string recorded, string onThePage)
    {
        var latest = File(9999, "MAIN", onThePage, uploadedDaysAgo: 1);

        Assert.That(
            NexusUpdateService.IsNewer(Record(4169, recorded), Mod("1.0.0"), latest, installed: null),
            Is.False);
    }

    // A page-only association stores the *manifest* version, which is a different numbering scheme
    // from the page's. "v1.1" against a page saying "1.15" is not an update, it is two schemes -
    // CheckAsync turns this into "could not be checked" rather than a badge that never clears.
    [Test]
    public void ShouldTreatAManifestVersionAsUncomparableToAPageVersion()
    {
        var latest = File(9999, "MAIN", "1.15", uploadedDaysAgo: 1);

        Assert.That(
            NexusUpdateService.IsNewer(Record(0, "v1.1"), Mod("v1.15"), latest, installed: null),
            Is.True,
            "IsNewer still says newer; CheckAsync is what downgrades this to Unavailable when " +
            "there is no file id to make the comparison trustworthy");
    }

    [TestCase("1.0.3", "1.0.4", TestName = "March Enhanced Portrait Compatibility Patch")]
    [TestCase("2.0.0", "2.0.1", TestName = "a genuine patch release")]
    public void ShouldStillFlagARealUpdate(string recorded, string onThePage)
    {
        var latest = File(9999, "MAIN", onThePage, uploadedDaysAgo: 1);

        Assert.That(
            NexusUpdateService.IsNewer(Record(4169, recorded), Mod("1.0.0"), latest, installed: null),
            Is.True);
    }

    // Without a file id - a mod the user associated by hand - versions are all there is.
    [Test]
    public void ShouldFallBackToVersionsWhenNoFileIdWasRecorded()
    {
        var latest = File(6400, "MAIN", "2.0");

        Assert.Multiple(() =>
        {
            Assert.That(NexusUpdateService.IsNewer(Record(0, "1.0"), Mod("1.0"), latest, null), Is.True);
            Assert.That(NexusUpdateService.IsNewer(Record(0, "2.0"), Mod("2.0"), latest, null), Is.False);
        });
    }

    // An optional file is not an out-of-date copy of the main file, and comparing the two reported
    // an update for ever.
    [Test]
    public void ShouldCompareAnOptionalFileAgainstItsOwnCategory()
    {
        var installed = File(4937, "OPTIONAL", "4", uploadedDaysAgo: 20);
        var files = new List<NexusFileInfo>
        {
            installed,
            File(6290, "MAIN", "6", uploadedDaysAgo: 1, primary: true)
        };

        var chosen = NexusUpdateService.ChooseComparableFile(files, installed);

        Assert.Multiple(() =>
        {
            Assert.That(chosen!.FileId, Is.EqualTo(4937));
            Assert.That(NexusUpdateService.IsNewer(Record(4937, "4"), Mod("4"), chosen!, installed), Is.False);
        });
    }

    [Test]
    public void ShouldPreferThePrimaryMainFileWhenNothingIsKnownAboutTheInstalledOne()
    {
        var files = new List<NexusFileInfo>
        {
            File(100, "MAIN", "1", uploadedDaysAgo: 1),
            File(200, "MAIN", "2", uploadedDaysAgo: 5, primary: true)
        };

        Assert.That(NexusUpdateService.ChooseComparableFile(files, null)!.FileId, Is.EqualTo(200));
    }

    // A category that has since been emptied still needs something to compare against.
    [Test]
    public void ShouldFallBackToMainWhenTheInstalledCategoryIsGone()
    {
        var installed = File(4937, "OLD_CATEGORY", "4", uploadedDaysAgo: 20);
        var files = new List<NexusFileInfo> { File(6290, "MAIN", "6", uploadedDaysAgo: 1) };

        Assert.That(NexusUpdateService.ChooseComparableFile(files, installed)!.FileId, Is.EqualTo(6290));
    }
}
