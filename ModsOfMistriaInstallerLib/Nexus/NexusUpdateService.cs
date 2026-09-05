using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using Garethp.ModsOfMistriaInstallerLib.ModTypes;

namespace Garethp.ModsOfMistriaInstallerLib.Nexus;

public enum NexusUpdateState
{
    /// <summary>The installed file is the one the mod page offers.</summary>
    UpToDate,

    UpdateAvailable,

    /// <summary>The user asked AIM to leave this mod on the version it is on.</summary>
    Frozen,

    /// <summary>
    /// A newer version exists for a mod AIM froze because it had edited the files.
    ///
    /// Deliberately not <see cref="UpdateAvailable"/>. An update here is not a thing to apply and
    /// forget: it replaces the mod folder, which discards AIM's fix, so it is worth taking only if
    /// the new version fixes the problem the patch was working around. Sweeping it into "update
    /// everything" would silently undo a repair the user watched AIM make, and the crash would come
    /// back with nothing on screen explaining why.
    /// </summary>
    UpdateMayFixEdit,

    /// <summary>AIM has no way to tell which Nexus mod this is.</summary>
    NotFromNexus,

    /// <summary>Nexus could not be asked - no account session, no network, rate limited.</summary>
    Unavailable
}

public record NexusUpdateStatus(
    NexusUpdateState State,
    NexusInstallRecord? Record = null,
    string? LatestVersion = null,
    int? LatestFileId = null,
    string? LatestFileName = null,
    string? Message = null)
{
    /// <summary>
    /// An update AIM may apply on the user's say-so as part of an ordinary update run. A mod frozen
    /// around a fix is excluded on purpose: see <see cref="NexusUpdateState.UpdateMayFixEdit"/>.
    /// </summary>
    public bool HasUpdate => State == NexusUpdateState.UpdateAvailable;

    /// <summary>There is a newer version, whether or not it is safe to take without asking.</summary>
    public bool AnyNewerVersion =>
        State is NexusUpdateState.UpdateAvailable or NexusUpdateState.UpdateMayFixEdit;
}

/// <summary>
/// Checks installed mods against their Nexus pages, and updates them when the account is allowed to.
///
/// Checking works for every account: file listings are ordinary API calls. Downloading is not -
/// Nexus only mints a download link for a free account when the request carries the token from a
/// "Mod Manager Download" click, so for those users an update ends at "here is the page". That
/// asymmetry is Nexus policy, not something AIM can work around, so the update path reports it
/// plainly rather than failing in a way that looks like a bug.
/// </summary>
public class NexusUpdateService
{
    private const int MaxParallelChecks = 4;

    private readonly Func<CancellationToken, Task<string?>> _accessTokenProvider;
    private readonly NexusInstallIndex _index;
    private readonly HttpClient _api;

    public NexusUpdateService(
        Func<CancellationToken, Task<string?>> accessTokenProvider,
        string modsLocation,
        HttpClient? apiClient = null)
    {
        _accessTokenProvider = accessTokenProvider;
        _index = new NexusInstallIndex(modsLocation);
        _api = apiClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public NexusInstallIndex Index => _index;

    /// <summary>
    /// Works out which Nexus mod an installed mod is. Mods AIM downloaded are in the index; mods
    /// installed by hand can still be identified when their manifest points at a Nexus page.
    /// </summary>
    public NexusInstallRecord? Resolve(IMod mod)
    {
        var recorded = _index.Get(mod.GetSourcePath());
        if (recorded is not null) return recorded;

        foreach (var url in new[] { mod.GetUpdateUrl(), mod.GetDownloadUrl() })
        {
            if (!NexusInstallIndex.TryReadNexusUrl(url, out var game, out var modId)) continue;

            // No file id: this mod was not installed through AIM, so comparison falls back to the
            // version in its manifest.
            return new NexusInstallRecord(game, modId, 0, "", mod.GetVersion(), DateTimeOffset.MinValue,
                _index.IsFrozen(mod.GetSourcePath()));
        }

        return null;
    }

    public async Task<NexusUpdateStatus> CheckAsync(IMod mod, CancellationToken ct = default)
    {
        var record = Resolve(mod);
        if (record is null) return new NexusUpdateStatus(NexusUpdateState.NotFromNexus);

        // A freeze the user set means "stop asking", and is answered here without troubling Nexus.
        // A freeze AIM set because it patched the mod means the opposite: the patch is a stopgap,
        // and the version that makes it unnecessary is the thing the user most wants to hear about.
        // So that one is checked, and reported as its own state further down.
        var patched = _index.FreezeReason(mod.GetSourcePath());

        if ((record.Frozen || _index.IsFrozen(mod.GetSourcePath())) && patched is null)
            return new NexusUpdateStatus(NexusUpdateState.Frozen, record);

        var accessToken = await _accessTokenProvider(ct);
        if (string.IsNullOrEmpty(accessToken))
            return new NexusUpdateStatus(NexusUpdateState.Unavailable, record,
                Message: "No Nexus account is connected yet.");

        try
        {
            var client = new NexusApiClient(accessToken, _api);
            var files = await client.GetFilesAsync(record.Game, record.ModId, ct);

            var installed = files.FirstOrDefault(file => file.FileId == record.FileId);
            var latest = ChooseComparableFile(files, installed, record.FileName);

            if (latest is null)
                return new NexusUpdateStatus(NexusUpdateState.Unavailable, record,
                    Message: installed is null && LineageOf(record.FileName).Length > 0
                        ? "The file this mod came from is no longer on its Nexus page, and none of " +
                          "the files still there is a newer version of it. That page hosts several " +
                          "separate mods, so AIM will not offer one of the others as an update - " +
                          "check the page by hand if you think there is a newer release."
                        : "That mod page has no main file to compare against.");

            var newer = IsNewer(record, mod, latest, installed);

            // A mod associated by pasting a page URL has no file id, so the only version AIM holds
            // for it came from the mod's own manifest - and authors number that separately from
            // their Nexus files. When those two disagree it is not evidence of an update, it is
            // evidence that AIM does not know which file is installed. Saying so is honest and
            // actionable; a badge that no amount of updating clears is neither.
            //
            // Agreement still means up to date, which lets RecordCurrentFileIdentity adopt the
            // real file id and put the mod on the reliable path from then on.
            if (newer && record.FileId <= 0)
                return new NexusUpdateStatus(NexusUpdateState.Unavailable, record,
                    latest.Version, latest.FileId, latest.FileName,
                    "AIM does not know which file on this page this mod came from, so it cannot " +
                    "tell whether the version on the page is newer. Download it once through AIM, " +
                    "or right-click the mod and associate it with a specific file, and update " +
                    "checks become reliable.");

            if (!newer) return new NexusUpdateStatus(
                NexusUpdateState.UpToDate, record, latest.Version, latest.FileId, latest.FileName);

            return new NexusUpdateStatus(
                patched is null ? NexusUpdateState.UpdateAvailable : NexusUpdateState.UpdateMayFixEdit,
                record, latest.Version, latest.FileId, latest.FileName, patched);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (NexusApiException e)
        {
            return new NexusUpdateStatus(NexusUpdateState.Unavailable, record, Message: e.Message);
        }
    }

    /// <summary>
    /// Completes provenance for a manually associated mod when the current Nexus file is already
    /// the installed version. This must not replace a recorded file or download anything.
    /// </summary>
    public void RecordCurrentFileIdentity(IMod mod, NexusUpdateStatus status)
    {
        if (status.HasUpdate || status.LatestFileId is not > 0 ||
            string.IsNullOrWhiteSpace(status.LatestFileName)) return;

        var record = Resolve(mod);
        if (record is null || record.FileId > 0) return;

        _index.Record(mod.GetSourcePath(), record with
        {
            FileId = status.LatestFileId.Value,
            FileName = status.LatestFileName,
            Version = status.LatestVersion ?? record.Version,
            InstalledAt = record.InstalledAt == DateTimeOffset.MinValue
                ? DateTimeOffset.UtcNow
                : record.InstalledAt
        });
    }

    /// <summary>
    /// Checks several mods, a few at a time. Nexus rate-limits by the hour, so a rate-limit reply
    /// stops the whole sweep instead of burning the remaining allowance on requests that will fail.
    /// </summary>
    public async Task<Dictionary<string, NexusUpdateStatus>> CheckManyAsync(
        IReadOnlyList<IMod> mods,
        IProgress<(int Done, int Total)>? progress = null,
        CancellationToken ct = default)
    {
        var results = new Dictionary<string, NexusUpdateStatus>(StringComparer.OrdinalIgnoreCase);
        if (mods.Count == 0) return results;

        using var stopEverything = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var gate = new SemaphoreSlim(MaxParallelChecks);
        var done = 0;

        var checks = mods.Select(async mod =>
        {
            try
            {
                // The wait belongs inside the guard: once the sweep is stopped, every mod still
                // queued here throws, and those have to become "stopped early" results rather than
                // faulting the whole batch and discarding the answers already paid for.
                await gate.WaitAsync(stopEverything.Token);

                try
                {
                    var status = await CheckAsync(mod, stopEverything.Token);

                    if (status.Message?.Contains("rate limit", StringComparison.OrdinalIgnoreCase) == true)
                        await stopEverything.CancelAsync();

                    lock (results)
                    {
                        results[mod.GetId()] = status;
                        progress?.Report((++done, mods.Count));
                    }
                }
                finally
                {
                    gate.Release();
                }
            }
            catch (OperationCanceledException)
            {
                lock (results)
                {
                    results[mod.GetId()] = new NexusUpdateStatus(NexusUpdateState.Unavailable, Resolve(mod),
                        Message: "The update check stopped early.");
                }
            }
        });

        await Task.WhenAll(checks);
        return results;
    }

    /// <summary>
    /// Downloads and installs the newest file, keeping the previous copy as a backup. Throws
    /// <see cref="NexusApiException"/> when the account is not allowed to download directly, which
    /// is the cue to send the user to the mod page instead.
    /// </summary>
    public async Task<NxmDownloadResult> UpdateAsync(
        IMod mod,
        NexusUpdateStatus status,
        string modsLocation,
        IProgress<NxmDownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (status.Record is null || status.LatestFileId is null)
            throw new NexusApiException("There is nothing to update to.");

        var link = new NxmLink(status.Record.Game, status.Record.ModId, status.LatestFileId.Value, null, null, null);

        var service = new NxmDownloadService(_accessTokenProvider, apiClient: _api);

        // A mod installed as a .zip is read through a handle this process keeps open, and Windows
        // will not move a file that is still open. Without this the new version unpacks fine, the
        // old archive cannot be put aside, and the mod list shows the old and new copies side by
        // side. The list is rebuilt after an update, so the closed reader is not used again.
        (mod as IDisposable)?.Dispose();

        // Replacing without asking is the point here: the user chose to update this specific mod,
        // and the previous copy is kept as a backup rather than destroyed.
        //
        // The mod's own path goes along so the new files land on top of it. Without that the
        // download is named after the Nexus file, which carries the file id and an upload stamp and
        // so is different for every release - and the "update" appears as a second copy of the mod.
        return await service.DownloadAndInstallAsync(
            link, modsLocation, progress, _ => Task.FromResult(true), ct,
            previousVersion: mod.GetVersion(),
            replacePath: mod.GetSourcePath());
    }

    /// <param name="reason">
    /// Why AIM froze it, when AIM is the one freezing. Null - the default, and what the user's own
    /// freeze menu passes - marks it as the user's decision, which AIM does not then argue with.
    /// </param>
    public void SetFrozen(IMod mod, bool frozen, string? reason = null) =>
        _index.SetFrozen(mod.GetSourcePath(), frozen, reason);

    public bool IsFrozen(IMod mod) => _index.IsFrozen(mod.GetSourcePath());

    /// <summary>Why AIM froze this mod, or null when it was the user or it is not frozen.</summary>
    public string? FreezeReason(IMod mod) => _index.FreezeReason(mod.GetSourcePath());

    // ── Version comparison ───────────────────────────────────────────────────────

    /// <summary>
    /// The file on the page that the installed one should be judged against.
    ///
    /// Its own category first. A mod folder that came from an optional or miscellaneous file is not
    /// an out-of-date copy of the main file, and comparing the two reported an update that no
    /// amount of updating could ever clear.
    ///
    /// Then its own name. A Nexus mod page often hosts several genuinely separate mods - "March
    /// Expanded", "Portrait Compatibility Patch" and "Butch March Compatibility Patch" all live on
    /// page 669, all in the same category - and each keeps its name across releases while its file
    /// id and version move. Category alone put those in one pot and handed back whichever was
    /// primary, so AIM offered a completely different mod as an "update" and then installed it
    /// alongside the one it was supposed to be updating.
    /// </summary>
    /// <param name="recordedFileName">
    /// The file name AIM stored at install time. It is what identifies the mod when the installed
    /// file itself has been taken off the page, which is when the wrong-mod bug used to bite.
    /// </param>
    public static NexusFileInfo? ChooseComparableFile(
        List<NexusFileInfo> files, NexusFileInfo? installed, string? recordedFileName = null)
    {
        var category = installed?.Category;

        // "OLD_VERSION" and "UPDATE" are where Nexus files go once they have been superseded, which
        // is the normal fate of a main file after a new release. Comparing an installed file
        // against its fellow archived files would report "up to date" for ever, or offer another
        // old file as the update - so those two mean "compare against MAIN" rather than
        // "compare within this category".
        if (string.IsNullOrWhiteSpace(category) || IsSuperseded(category)) category = "MAIN";

        var candidates = files
            .Where(file => file.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // An installed file whose category has since been emptied still needs something to compare
        // against, and the main file is the best guess left.
        if (candidates.Count == 0)
            candidates = files
                .Where(file => file.Category.Equals("MAIN", StringComparison.OrdinalIgnoreCase))
                .ToList();

        if (candidates.Count == 0) return null;

        var lineage = installed is not null
            ? LineageOf(installed.Name, installed.FileName)
            : LineageOf(recordedFileName);

        if (lineage.Length == 0) return Best(candidates);

        var kin = candidates.Where(file => LineageOf(file.Name, file.FileName) == lineage).ToList();
        if (kin.Count > 0) return Best(kin);

        // The page still has files in this category, but none of them is this mod. Offering one of
        // the others is the bug this guard exists for, so AIM says nothing instead - and CheckAsync
        // turns that into a message explaining why, rather than a silent "up to date".
        return null;
    }

    /// <summary>
    /// Where Nexus files put a file once it has been replaced. Comparing an installed file against
    /// its fellow archived files would report "up to date" for ever, or offer another old file as
    /// the update, so these mean "compare against MAIN" instead of "compare within this category".
    /// </summary>
    private static bool IsSuperseded(string? category) =>
        category is not null &&
        (category.Equals("OLD_VERSION", StringComparison.OrdinalIgnoreCase) ||
         category.Equals("UPDATE", StringComparison.OrdinalIgnoreCase));

    private static NexusFileInfo Best(List<NexusFileInfo> candidates) =>
        candidates.FirstOrDefault(file => file.IsPrimary)
        ?? candidates.OrderByDescending(file => file.UploadedAt).First();

    /// <summary>
    /// The name a file keeps across its own releases, with everything that moves stripped off.
    ///
    /// Nexus file names carry the mod id, the version, an upload stamp and a random token -
    /// "Portrait Compatibility Patch 669 1.0.3 2026-08-16T02-16Z Pwjkaog0D.zip" - and the display
    /// name is sometimes suffixed with a version too. What is left after removing those is the part
    /// the author keeps stable, and that is what identifies one mod among several on a page.
    /// </summary>
    public static string LineageOf(string? name, string? fallback = null)
    {
        var cleaned = Clean(name);
        return cleaned.Length > 0 ? cleaned : Clean(fallback);
    }

    private static string Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";

        var text = value.Trim();
        if (ModArchiveInstaller.LooksLikeArchive(text)) text = Path.GetFileNameWithoutExtension(text);

        // Words are dropped from the end while they look like metadata rather than part of a title,
        // which keeps a name that genuinely ends in a number ("Portal 2 Decor") intact.
        var words = text.Split([' ', '_', '-'], StringSplitOptions.RemoveEmptyEntries).ToList();
        while (words.Count > 1 && IsMetadata(words[^1])) words.RemoveAt(words.Count - 1);

        var joined = string.Join(" ", words).ToLowerInvariant();

        // Punctuation and spacing drift between a display name and a file name for the same file,
        // so neither survives into the comparison.
        return new string(joined.Where(char.IsLetterOrDigit).ToArray());
    }

    /// <summary>Whether a trailing word is version/id/stamp noise rather than part of the name.</summary>
    private static bool IsMetadata(string word)
    {
        // A version-ish token: "1.0.3", "v2", "V1.1.2", "669", "6".
        if (Regex.IsMatch(word, @"^[vV]?\d+(\.\d+)*$")) return true;

        // An upload stamp: "2026-08-16T02-16Z" arrives as "2026", "08", "16T02", "16Z" once split
        // on the hyphens, so the time separator has to be allowed inside the token too.
        if (Regex.IsMatch(word, @"^\d+[Tt]?\d*[Zz]?$")) return true;

        // The random per-file token Nexus appends: mixed case, no vowel pattern to speak of, and
        // always exactly nine characters, so it is matched on shape rather than guessed at.
        return Regex.IsMatch(word, @"^[A-Za-z0-9]{9}$") &&
               word.Any(char.IsUpper) && word.Any(char.IsLower) && word.Any(char.IsDigit);
    }

    /// <summary>
    /// Whether the page is offering something newer than what is installed.
    ///
    /// File ids decide it whenever AIM knows which file it put on disk. Version strings do not
    /// survive contact with real mods: authors number the mod page and the manifest differently -
    /// one mod here is "V1.0.2" in its manifest and "3" on its page - so comparing them declared a
    /// permanent update that installing could never satisfy. The id is Nexus's own identity for a
    /// file and has no such problem.
    /// </summary>
    public static bool IsNewer(
        NexusInstallRecord record, IMod mod, NexusFileInfo latest, NexusFileInfo? installed)
    {
        // The version AIM recorded when it took the file, which is Nexus's own numbering and is
        // therefore directly comparable to the page's. The manifest is a last resort: authors
        // number it separately, and comparing "1.0.2" from a manifest against "3" from a page is
        // what produced permanent phantom updates.
        var version = string.IsNullOrWhiteSpace(record.Version) ? mod.GetVersion() : record.Version;

        // The same file cannot be an update to itself.
        if (record.FileId > 0 && latest.FileId == record.FileId) return false;

        // A different file at the same version is a re-upload, not a release: the author replaced
        // the archive without moving the version. There is nothing for the user to gain from it,
        // and "2.0 → 2.0" in an update list is indistinguishable from a bug.
        //
        // This also covers the case where the installed file has been deleted from the page
        // altogether, which is common - authors tidy up old files. Assuming an update there was
        // wrong: the version says plainly that nothing has moved.
        if (!string.IsNullOrWhiteSpace(version) && !string.IsNullOrWhiteSpace(latest.Version))
            return IsVersionNewer(version, latest.Version);

        // No usable versions on either side, so the file identity is all that is left. A candidate
        // that is not newer than what is installed is a withdrawn release, not an update.
        if (record.FileId <= 0) return false;

        return installed is null ||
               installed.UploadedAt == DateTimeOffset.MinValue ||
               latest.UploadedAt > installed.UploadedAt;
    }

    /// <summary>
    /// Compares dotted version numbers, falling back to "different means newer" for versions that
    /// are not numeric. Mod versions are written by hand and are not always well formed, so the
    /// fallback errs towards telling the user rather than staying silent.
    /// </summary>
    public static bool IsVersionNewer(string? installed, string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return false;
        if (string.IsNullOrWhiteSpace(installed)) return true;

        var left = Numbers(installed);
        var right = Numbers(candidate);

        if (left.Count > 0 && right.Count > 0)
        {
            for (var i = 0; i < Math.Max(left.Count, right.Count); i++)
            {
                var a = i < left.Count ? left[i] : 0;
                var b = i < right.Count ? right[i] : 0;
                if (a != b) return b > a;
            }

            return false;
        }

        return !installed.Trim().Equals(candidate.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Orders two mod versions newest first, as a proper total order.
    ///
    /// Not "ask <see cref="IsVersionNewer"/> twice": for versions with no digits in them that
    /// answers "newer" in *both* directions, which makes an inconsistent comparison - and
    /// <see cref="List{T}.Sort(Comparison{T})"/> is entitled to throw when it notices. Sorting a
    /// changelog is not worth an exception, so non-numeric versions fall back to comparing the text.
    /// </summary>
    public static int CompareVersionsNewestFirst(string? left, string? right)
    {
        var a = Numbers(left ?? "");
        var b = Numbers(right ?? "");

        if (a.Count > 0 && b.Count > 0)
        {
            for (var i = 0; i < Math.Max(a.Count, b.Count); i++)
            {
                var x = i < a.Count ? a[i] : 0;
                var y = i < b.Count ? b[i] : 0;
                if (x != y) return y.CompareTo(x);
            }

            return 0;
        }

        // Reversed so that, like the numeric branch, "greater" sorts first.
        return string.Compare(right ?? "", left ?? "", StringComparison.OrdinalIgnoreCase);
    }

    private static List<int> Numbers(string version) =>
        version.TrimStart('v', 'V')
            .Split('.', '-', '+', '_')
            .Select(part => int.TryParse(new string(part.TakeWhile(char.IsDigit).ToArray()), out var value)
                ? value
                : -1)
            .TakeWhile(value => value >= 0)
            .ToList();
}
