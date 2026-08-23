using System.Net;
using System.Net.Http;
using Garethp.ModsOfMistriaInstallerLib.ModTypes;

namespace Garethp.ModsOfMistriaInstallerLib.Nexus;

public enum NexusUpdateState
{
    /// <summary>The installed file is the one the mod page offers.</summary>
    UpToDate,

    UpdateAvailable,

    /// <summary>The user asked AIM to leave this mod on the version it is on.</summary>
    Frozen,

    /// <summary>AIM has no way to tell which Nexus mod this is.</summary>
    NotFromNexus,

    /// <summary>Nexus could not be asked - no key, no network, rate limited.</summary>
    Unavailable
}

public record NexusUpdateStatus(
    NexusUpdateState State,
    NexusInstallRecord? Record = null,
    string? LatestVersion = null,
    int? LatestFileId = null,
    string? Message = null)
{
    public bool HasUpdate => State == NexusUpdateState.UpdateAvailable;
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

    private readonly NexusSettings _settings;
    private readonly NexusInstallIndex _index;
    private readonly HttpClient _api;

    public NexusUpdateService(NexusSettings settings, string modsLocation, HttpClient? apiClient = null)
    {
        _settings = settings;
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

        if (record.Frozen || _index.IsFrozen(mod.GetSourcePath()))
            return new NexusUpdateStatus(NexusUpdateState.Frozen, record);

        var apiKey = _settings.GetApiKey();
        if (string.IsNullOrEmpty(apiKey))
            return new NexusUpdateStatus(NexusUpdateState.Unavailable, record,
                Message: "No Nexus API key has been set up yet.");

        try
        {
            var client = new NexusApiClient(apiKey, _api);
            var latest = await client.GetLatestMainFileAsync(record.Game, record.ModId, ct);

            if (latest is null)
                return new NexusUpdateStatus(NexusUpdateState.Unavailable, record,
                    Message: "That mod page has no main file to compare against.");

            var newer = IsNewer(record, mod, latest);

            return new NexusUpdateStatus(
                newer ? NexusUpdateState.UpdateAvailable : NexusUpdateState.UpToDate,
                record, latest.Version, latest.FileId);
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

        var service = new NxmDownloadService(_settings, apiClient: _api);

        // Replacing without asking is the point here: the user chose to update this specific mod,
        // and the previous copy is kept as a backup rather than destroyed.
        return await service.DownloadAndInstallAsync(
            link, modsLocation, progress, _ => Task.FromResult(true), ct,
            previousVersion: mod.GetVersion());
    }

    public void SetFrozen(IMod mod, bool frozen) => _index.SetFrozen(mod.GetSourcePath(), frozen);

    public bool IsFrozen(IMod mod) => _index.IsFrozen(mod.GetSourcePath());

    // ── Version comparison ───────────────────────────────────────────────────────

    private static bool IsNewer(NexusInstallRecord record, IMod mod, NexusFileInfo latest)
    {
        // A file id is exact: it identifies the very file that was installed, so a different id on
        // the page means the author has published something since.
        if (record.FileId > 0) return latest.FileId != record.FileId;

        var installed = string.IsNullOrWhiteSpace(record.Version) ? mod.GetVersion() : record.Version;
        return IsVersionNewer(installed, latest.Version);
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

    private static List<int> Numbers(string version) =>
        version.TrimStart('v', 'V')
            .Split('.', '-', '+', '_')
            .Select(part => int.TryParse(new string(part.TakeWhile(char.IsDigit).ToArray()), out var value)
                ? value
                : -1)
            .TakeWhile(value => value >= 0)
            .ToList();
}
