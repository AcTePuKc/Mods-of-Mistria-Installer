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

    // ── Checking a batch of mods ─────────────────────────────────────────────────

    private static NexusUpdateService ServiceIn(string modsLocation) =>
        new(_ => Task.FromResult<string?>(null), modsLocation);

    private static string TempModsFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"aim-update-many-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        return folder;
    }

    // A manifest id is not unique on disk. The same mod installed twice, a folder sitting beside
    // the .zip it came from, or two copies a user edited separately all present the same id - and
    // results used to be filed under it, so the second answer overwrote the first. One row lost its
    // status entirely and the other could be shown one belonging to its twin.
    [Test]
    public async Task ShouldGiveEveryRowItsOwnResultWhenTwoModsShareAnId()
    {
        var folder = TempModsFolder();

        try
        {
            var asFolder = new MockMod(["manifest.toml"])
            {
                Id = "tester.same_mod",
                DirName = Path.Combine(folder, "Same Mod"),
                UpdateUrl = "https://www.nexusmods.com/fieldsofmistria/mods/703"
            };

            // The same id, but nothing tying it to a Nexus page - so its answer differs, which is
            // what makes an overwrite visible rather than merely possible.
            var strayCopy = new MockMod(["manifest.toml"])
            {
                Id = "tester.same_mod",
                DirName = Path.Combine(folder, "Same Mod copy")
            };

            var results = await ServiceIn(folder).CheckManyAsync([asFolder, strayCopy]);

            Assert.Multiple(() =>
            {
                Assert.That(results, Has.Count.EqualTo(2), "one row must not erase another's result");
                Assert.That(results[0].Mod, Is.SameAs(asFolder));
                Assert.That(results[1].Mod, Is.SameAs(strayCopy));
                Assert.That(results[0].Status.State, Is.EqualTo(NexusUpdateState.Unavailable),
                    "no account is connected, so a mod AIM can identify cannot be checked");
                Assert.That(results[1].Status.State, Is.EqualTo(NexusUpdateState.NotFromNexus));
            });
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    // A stopped sweep still has to answer for every row it was given: a row with no result at all
    // would leave the list showing a stale badge with nothing to say why.
    [Test]
    public async Task ShouldStillAnswerEveryRowWhenTheSweepIsStoppedEarly()
    {
        var folder = TempModsFolder();

        try
        {
            var mods = Enumerable.Range(1, 5)
                .Select(number => new MockMod(["manifest.toml"])
                {
                    Id = "tester.same_mod",
                    DirName = Path.Combine(folder, $"Mod {number}")
                })
                .ToList();

            using var stopped = new CancellationTokenSource();
            await stopped.CancelAsync();

            var results = await ServiceIn(folder).CheckManyAsync(
                [.. mods],
                new Progress<(int Done, int Total)>(_ => { }),
                stopped.Token);

            Assert.Multiple(() =>
            {
                Assert.That(results, Has.Count.EqualTo(mods.Count));
                Assert.That(results[0].Mod, Is.SameAs(mods[0]), "results stay in the order they were asked for");
                Assert.That(results[^1].Mod, Is.SameAs(mods[^1]));
                Assert.That(results.Select(result => result.Status.State),
                    Is.All.EqualTo(NexusUpdateState.Unavailable));
                Assert.That(results.Select(result => result.Status.Message),
                    Is.All.EqualTo("The update check stopped early."));
            });
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    // ── Adopting a file id for a mod associated by page URL ──────────────────────

    private static MockMod PageOnlyMod(string folder) =>
        new(["manifest.toml"])
        {
            Id = "tester.page_only",
            Version = "1.0.0",
            DirName = Path.Combine(folder, "Page Only"),
            UpdateUrl = "https://www.nexusmods.com/fieldsofmistria/mods/703"
        };

    // "AIM cannot tell which file this is" must not be read as "the installed copy is the newest
    // one". Recording the newest file id there makes the next check compare that id against itself,
    // so a mod that is genuinely several versions behind reports up to date from then on - and the
    // user is never told again.
    [Test]
    public void ShouldNotAdoptTheNewestFileForAModItCouldNotIdentify()
    {
        var folder = TempModsFolder();

        try
        {
            var service = ServiceIn(folder);
            var mod = PageOnlyMod(folder);

            // The exact shape CheckAsync returns when the page has a newer version but the install
            // has no file id to compare against.
            var couldNotTell = new NexusUpdateStatus(
                NexusUpdateState.Unavailable, service.Resolve(mod),
                "2.0", 6400, "file-6400.zip", "AIM does not know which file...");

            service.RecordCurrentFileIdentity(mod, couldNotTell);

            Assert.That(service.Index.Get(mod.GetSourcePath())?.FileId ?? 0, Is.EqualTo(0),
                "an unanswered check must not be written down as an answer");
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    // The case adoption exists for: Nexus has confirmed the installed copy is the current file, so
    // recording which file that is puts the mod on the reliable path from then on.
    [Test]
    public void ShouldAdoptTheCurrentFileWhenTheCheckSaysTheInstalledCopyIsUpToDate()
    {
        var folder = TempModsFolder();

        try
        {
            var service = ServiceIn(folder);
            var mod = PageOnlyMod(folder);

            var upToDate = new NexusUpdateStatus(
                NexusUpdateState.UpToDate, service.Resolve(mod), "1.0.0", 6400, "file-6400.zip");

            service.RecordCurrentFileIdentity(mod, upToDate);

            Assert.That(service.Index.Get(mod.GetSourcePath())?.FileId, Is.EqualTo(6400));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    // ── Lineage: matching a mod to its own newer file ────────────────────────────

    private static NexusFileInfo Named(int fileId, string name, string fileName, string version) =>
        new(fileId, fileName, name, version, 1024)
        {
            Category = "MAIN",
            IsPrimary = true,
            UploadedAt = DateTimeOffset.UtcNow
        };

    // Real file names from a real mods folder. The version is welded onto the last word, so
    // stripping metadata off the end of the name could never reach it: the lineage moved with every
    // release, no file on the page ever matched, and AIM said "the file this mod came from is no
    // longer on its page" for ever - on a mod whose update was sitting right there.
    [TestCase(
        "AlteredTown_AIO2.1.3 677 2.1.3 2026-08-14T06-44Z z9f5yiVWq.zip",
        "AlteredTownAIO2.1 677 2.1 2026-09-01T00-00Z aB3dE5fG7.zip",
        TestName = "a version welded onto the name")]
    [TestCase(
        "ChooseGiftFromChests 1.3.3 MOMI 956 1.3.3 2026-08-27T04-06Z NfwKtlHDd.zip",
        "ChooseGiftFromChests 1.4.0 MOMI 956 1.4.0 2026-09-02T00-00Z Qz8rT2vXm.zip",
        TestName = "a version in the middle of the name")]
    [TestCase(
        "MistriaChestRelocate V1.0.10 965 1 2026-08-26T11-50Z 8nMdw5I5m.zip",
        "MistriaChestRelocate V1.0.11 965 2 2026-09-02T00-00Z Lp4wS9tRe.zip",
        TestName = "a v-prefixed version in the middle")]
    public void ShouldRecogniseAModsOwnNewerFile(string installed, string onThePage) =>
        Assert.That(NexusUpdateService.LineageOf(installed),
            Is.EqualTo(NexusUpdateService.LineageOf(onThePage)));

    // Nexus's per-file token is base62, so about one in five has no digit in it. "NfwKtlHDd" is a
    // real one, off a real install. Recognising these by shape alone missed it, the scan stopped on
    // its first step, and the mod id, version and upload stamp all ended up welded into the lineage.
    [Test]
    public void ShouldStripANexusTokenThatHappensToHaveNoDigitInIt()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                NexusUpdateService.LineageOf("Some Mod 956 1.3.3 2026-08-27T04-06Z NfwKtlHDd.zip"),
                Is.EqualTo("somemod"));

            // And the same name with a token that does have a digit, which always worked.
            Assert.That(
                NexusUpdateService.LineageOf("Some Mod 956 1.3.3 2026-08-27T04-06Z Nfw3tlHDd.zip"),
                Is.EqualTo("somemod"));
        });
    }

    // Position is what identifies the token, so an ordinary nine-letter word is safe: it is only
    // taken as a token when an upload stamp sits immediately in front of it.
    [Test]
    public void ShouldNotMistakeANineLetterTitleWordForANexusToken()
    {
        Assert.That(NexusUpdateService.LineageOf("Mistria Buildings"), Is.EqualTo("mistriabuildings"));
    }

    // The guard this whole mechanism exists for. Page 669 hosts several genuinely separate mods, and
    // offering one as an update to another installed it alongside the mod it was meant to replace.
    // Loosening the version stripping must not start collapsing these into each other.
    [Test]
    public void ShouldStillTellApartSeparateModsOnOnePage()
    {
        var lineages = new[]
        {
            "March Expanded 669 2.0.12 2026-08-21T00-48Z UeRMzf4uu.zip",
            "Portrait Compatibility Patch 669 1.0.3 2026-08-16T02-16Z Pwjkaog0D.zip",
            "Butch March Compatibility Patch 669 1.0.1 2026-08-16T02-20Z Kwjfaog1X.zip"
        }.Select(name => NexusUpdateService.LineageOf(name)).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(lineages.Distinct().Count(), Is.EqualTo(3), "three mods, three lineages");
            Assert.That(lineages, Has.None.Empty);
        });
    }

    // A bare number is part of a title far more often than it is a release number, so it stays.
    // Losing it would make these two mods the same mod.
    [Test]
    public void ShouldKeepABareNumberThatBelongsToTheTitle()
    {
        Assert.Multiple(() =>
        {
            Assert.That(NexusUpdateService.LineageOf("Portal 2 Decor 500 1.0 2026-08-01T00-00Z aB3dE5fG7.zip"),
                Is.Not.EqualTo(NexusUpdateService.LineageOf("Portal 3 Decor 500 1.0 2026-08-01T00-00Z aB3dE5fG7.zip")));
            Assert.That(NexusUpdateService.LineageOf("Portal 2 Decor"), Is.EqualTo("portal2decor"));
        });
    }

    // End to end through the picker: the installed file has been taken off the page, and the mod's
    // own newer release must be the one chosen - not nothing, and not a different mod.
    [Test]
    public void ShouldChooseAModsOwnNewerFileWhenTheInstalledOneIsGone()
    {
        var files = new List<NexusFileInfo>
        {
            Named(9001, "AlteredTownAIO2.1", "AlteredTownAIO2.1 677 2.1 2026-09-01T00-00Z aB3dE5fG7.zip", "2.1"),
            Named(9002, "Something Else", "Something Else 677 1.0 2026-09-01T00-00Z Qz8rT2vXm.zip", "1.0")
        };

        var chosen = NexusUpdateService.ChooseComparableFile(
            files, installed: null,
            recordedFileName: "AlteredTown_AIO2.1.3 677 2.1.3 2026-08-14T06-44Z z9f5yiVWq.zip");

        Assert.That(chosen?.FileId, Is.EqualTo(9001));
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
