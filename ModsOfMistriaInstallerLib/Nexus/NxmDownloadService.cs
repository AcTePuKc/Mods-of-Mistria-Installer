using System.Net.Http;

namespace Garethp.ModsOfMistriaInstallerLib.Nexus;

public enum NxmDownloadStage
{
    Queued,
    Resolving,
    Downloading,
    Installing,
    Completed,
    Failed,
    Cancelled
}

public record NxmDownloadProgress(NxmDownloadStage Stage, string Message, long BytesReceived = 0, long? TotalBytes = null)
{
    /// <summary>Fraction downloaded, or null when the server did not say how big the file is.</summary>
    public double? Fraction => TotalBytes is > 0 ? Math.Clamp((double)BytesReceived / TotalBytes.Value, 0, 1) : null;
}

public record NxmDownloadResult(
    bool Success,
    string FileName,
    List<InstalledModFolder> Installed,
    string? Error = null,
    bool Cancelled = false);

/// <summary>
/// Turns a clicked "Mod Manager Download" link into an installed mod folder.
///
/// The sequence mirrors what Vortex does: resolve the file through the Nexus API, download it from
/// the CDN URL that comes back, then unpack it into the mods folder. Everything the user might have
/// to answer - a missing API key, a mod that is already installed - is asked through a callback so
/// that this class stays free of UI.
/// </summary>
public class NxmDownloadService(NexusSettings settings, HttpClient? downloadClient = null, HttpClient? apiClient = null)
{
    // Mod archives are large and CDN throughput varies wildly, so the download client has no
    // per-request timeout and relies on cancellation instead. The API client keeps a short one:
    // a metadata call that hangs should fail quickly rather than stall the whole download.
    private readonly HttpClient _http = downloadClient ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

    private readonly HttpClient _api = apiClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

    /// <param name="link">The parsed nxm:// link.</param>
    /// <param name="modsLocation">Where mods are installed.</param>
    /// <param name="progress">Receives stage and byte-level updates.</param>
    /// <param name="confirmOverwrite">
    /// Asked when the download would replace folders that already exist. Returning false aborts the
    /// install and leaves what is on disk alone. When null, an existing folder is replaced silently,
    /// which is the right default for an update the user has just asked for.
    /// </param>
    public async Task<NxmDownloadResult> DownloadAndInstallAsync(
        NxmLink link,
        string modsLocation,
        IProgress<NxmDownloadProgress>? progress = null,
        Func<List<string>, Task<bool>>? confirmOverwrite = null,
        CancellationToken ct = default)
    {
        var fileName = $"{link.ModId}-{link.FileId}";
        string? temporaryFile = null;

        try
        {
            var apiKey = settings.GetApiKey();
            if (string.IsNullOrEmpty(apiKey))
                return Failure(fileName, "No Nexus API key has been set up yet.", progress);

            if (!link.IsForMistria())
                return Failure(fileName,
                    $"That download is for another game ({link.Game}), so this installer cannot handle it.", progress);

            if (link.IsExpired)
                return Failure(fileName,
                    "That download link has expired. Click \"Mod Manager Download\" on the mod page again.", progress);

            if (!Directory.Exists(modsLocation))
                return Failure(fileName, "The mods folder could not be found.", progress);

            progress?.Report(new NxmDownloadProgress(NxmDownloadStage.Resolving, "Asking Nexus about the file..."));

            var client = new NexusApiClient(apiKey, _api);
            var fileInfo = await client.GetFileInfoAsync(link, ct);
            fileName = fileInfo.FileName;

            var urls = await client.GetDownloadUrlsAsync(link, ct);

            progress?.Report(new NxmDownloadProgress(
                NxmDownloadStage.Downloading, $"Downloading {fileInfo.Name}", 0, fileInfo.SizeInBytes));

            temporaryFile = await DownloadAsync(urls, fileName, fileInfo.Name, progress, ct);

            progress?.Report(new NxmDownloadProgress(NxmDownloadStage.Installing, $"Unpacking {fileInfo.Name}"));

            var (installed, abandoned) = await InstallAsync(temporaryFile, modsLocation, fileName, confirmOverwrite, ct);
            if (abandoned)
            {
                progress?.Report(new NxmDownloadProgress(NxmDownloadStage.Cancelled, "Install cancelled"));
                return new NxmDownloadResult(false, fileName, [], null, true);
            }

            var summary = installed.Count == 1
                ? $"Installed {installed[0].Name}"
                : $"Installed {installed.Count} mods";

            progress?.Report(new NxmDownloadProgress(NxmDownloadStage.Completed, summary));
            Logger.Log($"{summary} from Nexus ({fileName})");

            return new NxmDownloadResult(true, fileName, installed);
        }
        catch (OperationCanceledException)
        {
            progress?.Report(new NxmDownloadProgress(NxmDownloadStage.Cancelled, "Download cancelled"));
            return new NxmDownloadResult(false, fileName, [], null, true);
        }
        catch (Exception e) when (e is NexusApiException or ModArchiveException)
        {
            return Failure(fileName, e.Message, progress);
        }
        catch (Exception e)
        {
            Logger.Log($"Unexpected failure downloading {fileName}: {e}");
            return Failure(fileName, e.Message, progress);
        }
        finally
        {
            TryDelete(temporaryFile);
        }
    }

    // ── Steps ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Downloads to a temporary file, trying each mirror in turn. Nexus returns several CDN hosts
    /// and the preferred one is not always healthy.
    /// </summary>
    private async Task<string> DownloadAsync(
        List<string> urls,
        string fileName,
        string displayName,
        IProgress<NxmDownloadProgress>? progress,
        CancellationToken ct)
    {
        var directory = Path.Combine(Path.GetTempPath(), "aim-nexus-downloads");
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, $"{Guid.NewGuid():N}-{SafeFileName(fileName)}");

        Exception? lastFailure = null;

        foreach (var url in urls)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                var total = response.Content.Headers.ContentLength;

                await using var input = await response.Content.ReadAsStreamAsync(ct);
                await using (var output = File.Create(destination))
                {
                    var buffer = new byte[81920];
                    long received = 0;
                    var lastReport = 0L;

                    int read;
                    while ((read = await input.ReadAsync(buffer, ct)) > 0)
                    {
                        await output.WriteAsync(buffer.AsMemory(0, read), ct);
                        received += read;

                        // Reporting every chunk would flood the UI thread on a fast connection.
                        if (received - lastReport < 256 * 1024 && received != total) continue;

                        lastReport = received;
                        progress?.Report(new NxmDownloadProgress(
                            NxmDownloadStage.Downloading, $"Downloading {displayName}", received, total));
                    }
                }

                return destination;
            }
            catch (OperationCanceledException)
            {
                TryDelete(destination);
                throw;
            }
            catch (Exception e)
            {
                lastFailure = e;
                Logger.Log($"Download mirror failed ({url}): {e.Message}");
                TryDelete(destination);
            }
        }

        throw new NexusApiException(
            $"Could not download the file from Nexus: {lastFailure?.Message ?? "no download server responded"}");
    }

    /// <summary>
    /// Unpacks the archive, pausing to ask about folders that already exist. Extraction itself is
    /// blocking work and is pushed off the calling thread; the confirmation is awaited rather than
    /// blocked on, because the answer usually comes from the UI thread.
    /// </summary>
    private static async Task<(List<InstalledModFolder> Installed, bool Abandoned)> InstallAsync(
        string archivePath,
        string modsLocation,
        string fileName,
        Func<List<string>, Task<bool>>? confirmOverwrite,
        CancellationToken ct)
    {
        try
        {
            var installed = await Task.Run(
                () => ModArchiveInstaller.Install(archivePath, modsLocation, fileName), ct);
            return (installed, false);
        }
        catch (ModArchiveConflictException conflict)
        {
            ct.ThrowIfCancellationRequested();

            var replace = confirmOverwrite is null || await confirmOverwrite(conflict.Folders);
            if (!replace) return ([], true);

            var installed = await Task.Run(() => ModArchiveInstaller.Install(
                archivePath, modsLocation, fileName, ArchiveConflictBehaviour.Replace), ct);
            return (installed, false);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static NxmDownloadResult Failure(string fileName, string error, IProgress<NxmDownloadProgress>? progress)
    {
        progress?.Report(new NxmDownloadProgress(NxmDownloadStage.Failed, error));
        return new NxmDownloadResult(false, fileName, [], error);
    }

    private static string SafeFileName(string name) =>
        new(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());

    private static void TryDelete(string? path)
    {
        if (path is null || !File.Exists(path)) return;

        try
        {
            File.Delete(path);
        }
        catch (Exception e)
        {
            Logger.Log($"Could not remove the temporary download {Path.GetFileName(path)}: {e.Message}");
        }
    }
}
