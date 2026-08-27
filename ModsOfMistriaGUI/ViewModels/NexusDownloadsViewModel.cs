using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Garethp.ModsOfMistriaGUI.Models;
using Garethp.ModsOfMistriaGUI.Views;
using Garethp.ModsOfMistriaInstallerLib;
using Garethp.ModsOfMistriaInstallerLib.ModTypes;
using Garethp.ModsOfMistriaInstallerLib.Nexus;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

namespace Garethp.ModsOfMistriaGUI.ViewModels;

/// <summary>
/// Everything behind the "Vortex" button on the Nexus website: the OAuth account session, the
/// protocol registration that makes the button reach AIM at all, and the downloads themselves.
/// </summary>
public partial class NexusDownloadsViewModel : ViewModelBase
{
    private readonly Settings _settings;
    private readonly NexusSettings _nexusSettings;
    private readonly NexusOAuthService _oauth;
    private readonly NxmDownloadService _service;
    private bool _handlerIsActive;

    /// <summary>Action shown in the Nexus submenu; it reflects the current handler state.</summary>
    public string HandlerActionText =>
        _handlerIsActive
            ? Localization["GUINexusHandlerDisableMenuItem"]
            : Localization["GUINexusHandlerEnableMenuItem"];

    /// <summary>Raised after a download installs, so the mod list can pick the new folder up.</summary>
    public event EventHandler? ModsChanged;

    public NexusDownloadsViewModel(Settings settings, NexusSettings? nexusSettings = null)
    {
        _settings = settings;
        _nexusSettings = nexusSettings ?? new NexusSettings();
        _oauth = new NexusOAuthService(_nexusSettings, NexusOAuthRegistration.Pending);
        _service = new NxmDownloadService(_oauth.GetAccessTokenAsync);

        Localization.LanguageChanged += (_, _) => RefreshHandlerStatus();
        RefreshHandlerStatus();
    }

    /// <summary>
    /// Start-up work that touches the machine: restoring the protocol registration. Kept out of the
    /// constructor so that constructing the view model - as the headless UI tests do - cannot
    /// change the user's nxm:// handler.
    /// </summary>
    public void Initialise()
    {
        RestoreHandlerRegistration();
        RefreshHandlerStatus();
        _ = OfferToHandleLinksAsync();
    }

    /// <summary>
    /// Asks once whether AIM should take over "Mod Manager Download" links, the way Vortex and
    /// Stardrop do on first run. Declining is remembered: a mod manager that asks every launch is
    /// a mod manager people stop reading dialogs from.
    /// </summary>
    private async Task OfferToHandleLinksAsync()
    {
        if (!NxmProtocolHandler.IsSupported()) return;
        if (_nexusSettings.HandlerPromptAnswered) return;

        var status = NxmProtocolHandler.GetStatus();
        if (status is { IsRegistered: true, IsThisExecutable: true }) return;

        // When another manager holds the protocol the offer names it, so the choice is informed
        // rather than a blind "yes" that quietly takes downloads away from Vortex.
        var message = status.IsClaimedByAnother
            ? string.Format(Localization["GUINexusHandlerOfferTakeOver"], status.HandlerName ?? status.CurrentHandler)
            : Localization["GUINexusHandlerOffer"];

        var answer = await ShowBoxAsync(Localization["GUINexusHandlerTitle"], message, ButtonEnum.YesNo);

        _nexusSettings.HandlerPromptAnswered = true;
        if (answer != ButtonResult.Yes) return;

        if (NxmProtocolHandler.Register(out var error))
        {
            _nexusSettings.HandlerRegistered = true;
            await ShowMessage(Localization["GUINexusHandlerTitle"], Localization["GUINexusHandlerRegistered"]);
        }
        else
        {
            await ShowMessage(Localization["GUINexusHandlerTitle"],
                string.Format(Localization["GUINexusHandlerFailed"], error ?? ""));
        }

        RefreshHandlerStatus();
    }

    /// <summary>An update service bound to one mods folder. The caller owns the lifetime.</summary>
    public NexusUpdateService CreateUpdateService(string modsLocation) => new(_oauth.GetAccessTokenAsync, modsLocation);

    /// <summary>
    /// Makes sure an OAuth account session exists. The public client registration is deliberately
    /// pending until Nexus approves AIM, so this never falls back to a personal API key.
    /// </summary>
    public async Task<bool> EnsureNexusAccountAsync()
    {
        if (_oauth.HasSession && await _oauth.GetAccessTokenAsync() is not null) return true;

        await ShowMessage(Localization["GUINexusDownloadFailedTitle"],
            _oauth.IsRegistered
                ? "Connect your Nexus account to continue."
                : "Nexus account connection is awaiting Nexus OAuth registration.");
        return false;
    }

    /// <summary>
    /// Runs an update as a normal download, so it shows in the same downloads strip with the same
    /// progress and cancel button. Returns true when the mod was replaced.
    /// </summary>
    public async Task<bool> RunUpdateAsync(
        NexusUpdateService service, IMod mod, NexusUpdateStatus status, string modsLocation)
    {
        if (!await EnsureNexusAccountAsync()) return false;

        var download = new NexusDownloadModel(mod.GetName());
        Downloads.Add(download);
        HasDownloads = true;

        var progress = new Progress<NxmDownloadProgress>(update =>
            Dispatcher.UIThread.Post(() => download.Apply(update)));

        NxmDownloadResult result;
        try
        {
            result = await Task.Run(() =>
                service.UpdateAsync(mod, status, modsLocation, progress, download.Token));
        }
        catch (Exception e)
        {
            download.Apply(new NxmDownloadProgress(NxmDownloadStage.Failed, e.Message));
            await ShowMessage(Localization["GUINexusDownloadFailedTitle"], e.Message);
            return false;
        }

        if (result.Success)
        {
            ModsChanged?.Invoke(this, EventArgs.Empty);
            _ = DismissWhenReadAsync(download);
            return true;
        }

        if (result.Cancelled)
        {
            _ = DismissWhenReadAsync(download);
            return false;
        }

        // The usual reason: a free account cannot be given a download link without the token that
        // only the website's button carries. Sending them to the page is the whole remedy.
        var openPage = await ShowBoxAsync(
            Localization["GUINexusDownloadFailedTitle"],
            string.Format(Localization["GUINexusUpdateNeedsPage"], result.Error ?? ""),
            ButtonEnum.YesNo);

        if (openPage == ButtonResult.Yes && status.Record is not null)
        {
            var url = status.LatestFileId is > 0
                ? status.Record.FilePageUrl(status.LatestFileId.Value)
                : status.Record.FilesPageUrl;
            OpenUrl(url);
        }

        return false;
    }

    /// <summary>Opens a page in the user's browser, refusing anything that is not plain https.</summary>
    public static void OpenUrl(string? url)
    {
        if (!ExternalUrl.IsAllowed(url)) return;

        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception e)
        {
            Logger.Log($"Could not open {url}: {e.Message}");
        }
    }

    public ObservableCollection<NexusDownloadModel> Downloads { get; } = [];

    [ObservableProperty] private bool _hasDownloads;

    [ObservableProperty] private string _handlerStatus = "";

    [ObservableProperty] private bool _handlerNeedsAttention;

    public bool IsNexusAccountConnected => _oauth.HasSession;

    // ── Incoming links ───────────────────────────────────────────────────────────

    /// <summary>
    /// Entry point for a link, whether it arrived on the command line, through the pipe from a
    /// second instance, or from the clipboard.
    /// </summary>
    public async Task HandleLinkAsync(string rawLink)
    {
        if (!NxmLink.TryParse(rawLink, out var link, out var error) || link is null)
        {
            await ShowMessage(Localization["GUINexusDownloadFailedTitle"], error ?? "");
            return;
        }

        await DownloadLinkAsync(link, confirmOverwrite: true);
    }

    /// <summary>
    /// Handles an NXM link attached to an already listed mod. The file metadata is checked before
    /// the download starts, so replacing the same version never shows a progress row first.
    /// </summary>
    public async Task<bool> HandleAssociatedLinkAsync(
        string rawLink, string existingSourcePath, string existingVersion)
    {
        if (!NxmLink.TryParse(rawLink, out var link, out var error) || link is null)
        {
            await ShowMessage(Localization["GUINexusDownloadFailedTitle"], error ?? "");
            return false;
        }

        if (!link.IsForMistria())
        {
            await ShowMessage(Localization["GUINexusDownloadFailedTitle"],
                string.Format(Localization["GUINexusWrongGame"], link.Game));
            return false;
        }

        if (!await EnsureNexusAccountAsync()) return false;

        NexusFileInfo fileInfo;
        try
        {
            var accessToken = await _oauth.GetAccessTokenAsync();
            if (string.IsNullOrEmpty(accessToken)) return false;
            var client = new NexusApiClient(accessToken);
            fileInfo = await client.GetFileInfoAsync(link);
        }
        catch (NexusApiException e)
        {
            await ShowMessage(Localization["GUINexusDownloadFailedTitle"], e.Message);
            return false;
        }

        var sameVersion = !string.IsNullOrWhiteSpace(existingVersion) &&
                          !string.IsNullOrWhiteSpace(fileInfo.Version) &&
                          !NexusUpdateService.IsVersionNewer(existingVersion, fileInfo.Version) &&
                          !NexusUpdateService.IsVersionNewer(fileInfo.Version, existingVersion);

        if (sameVersion)
        {
            // Association must not download a copy that is already present. The API lookup gives
            // us the exact file identity even when the user originally installed it manually.
            new NexusInstallIndex(_settings.ModsLocation).Record(existingSourcePath,
                new NexusInstallRecord(link.Game, link.ModId, fileInfo.FileId, fileInfo.FileName,
                    existingVersion, DateTimeOffset.UtcNow));
            return true;
        }

        // Association is initiated from a mod that already exists in the list. Never let a
        // missing/non-standard Nexus version silently turn this into a replacement download.
        // The downloader's overwrite callback is skipped below because this confirmation is the
        // single confirmation for the whole association action.
        var version = string.IsNullOrWhiteSpace(fileInfo.Version) ? "?" : fileInfo.Version;
        var answer = await ShowBoxAsync(
            Localization["GUINexusAlreadyInstalledTitle"],
            string.Format(Localization["GUINexusAlreadyInstalledMessage"],
                $"{fileInfo.FileName} (v{version})"),
            ButtonEnum.YesNo);

        if (answer != ButtonResult.Yes) return false;

        await DownloadLinkAsync(link, confirmOverwrite: false);
        return true;
    }

    private async Task DownloadLinkAsync(NxmLink link, bool confirmOverwrite)
    {

        if (!link.IsForMistria())
        {
            await ShowMessage(Localization["GUINexusDownloadFailedTitle"],
                string.Format(Localization["GUINexusWrongGame"], link.Game));
            return;
        }

        if (!await EnsureNexusAccountAsync()) return;

        if (!_settings.ValidModsLocation())
        {
            await ShowMessage(Localization["GUINexusDownloadFailedTitle"],
                Localization["GUINexusNoModsFolder"]);
            return;
        }

        var download = new NexusDownloadModel(string.Format(Localization["GUINexusDownloadTitle"], link.ModId));
        Downloads.Add(download);
        HasDownloads = true;

        var progress = new Progress<NxmDownloadProgress>(update =>
            Dispatcher.UIThread.Post(() => download.Apply(update)));

        // The download and unpack run off the UI thread; progress comes back through the
        // Progress<T> above and the one question it can ask is marshalled explicitly.
        var result = await Task.Run(() => _service.DownloadAndInstallAsync(
            link,
            _settings.ModsLocation,
            progress,
            confirmOverwrite
                ? folders => ConfirmOverwriteAsync(folders, download.Token)
                : _ => Task.FromResult(true),
            download.Token));

        download.Title = result.FileName;

        if (result.Success)
        {
            ModsChanged?.Invoke(this, EventArgs.Empty);
            _ = DismissWhenReadAsync(download);
        }
        else if (result.Cancelled)
        {
            _ = DismissWhenReadAsync(download);
        }
        else if (result.Error is not null)
        {
            await ShowMessage(Localization["GUINexusDownloadFailedTitle"], result.Error);
        }
    }

    private async Task<bool> ConfirmOverwriteAsync(List<string> folders, CancellationToken ct)
    {
        var result = await ShowBoxAsync(
            Localization["GUINexusAlreadyInstalledTitle"],
            string.Format(Localization["GUINexusAlreadyInstalledMessage"], string.Join(", ", folders)),
            ButtonEnum.YesNo,
            ct);

        return result == ButtonResult.Yes;
    }

    // ── Commands ─────────────────────────────────────────────────────────────────

    public async Task ManageNexusAccountAsync()
    {
        await EnsureNexusAccountAsync();
    }

    [RelayCommand]
    private Task ManageNexusAccount() => ManageNexusAccountAsync();

    /// <summary>
    /// Claims (or gives up) the nxm:// protocol. Registering is what makes the website's
    /// "Mod Manager Download" button hand its link to AIM.
    /// </summary>
    [RelayCommand]
    private async Task ToggleHandler()
    {
        var status = NxmProtocolHandler.GetStatus();

        if (status is { IsRegistered: true, IsThisExecutable: true })
        {
            if (NxmProtocolHandler.Unregister(out var unregisterError))
            {
                _nexusSettings.HandlerRegistered = false;
                await ShowMessage(Localization["GUINexusHandlerTitle"],
                    Localization["GUINexusHandlerUnregistered"]);
            }
            else
            {
                await ShowMessage(Localization["GUINexusHandlerTitle"],
                    string.Format(Localization["GUINexusHandlerUnregisterFailed"], unregisterError ?? ""));
            }

            RefreshHandlerStatus();
            return;
        }

        if (status.IsClaimedByAnother)
        {
            var confirm = await MessageBoxManager.GetMessageBoxStandard(
                Localization["GUINexusHandlerTitle"],
                string.Format(Localization["GUINexusHandlerTakeOver"], status.HandlerName ?? status.CurrentHandler),
                ButtonEnum.YesNo).ShowAsync();

            if (confirm != ButtonResult.Yes) return;
        }

        if (NxmProtocolHandler.Register(out var error))
        {
            _nexusSettings.HandlerRegistered = true;
            await ShowMessage(Localization["GUINexusHandlerTitle"], Localization["GUINexusHandlerRegistered"]);
        }
        else
        {
            await ShowMessage(Localization["GUINexusHandlerTitle"],
                string.Format(Localization["GUINexusHandlerFailed"], error ?? ""));
        }

        RefreshHandlerStatus();
    }

    /// <summary>
    /// A manual way in for anyone whose browser will not hand over nxm links - copy the link from
    /// the browser's download prompt and paste it here.
    /// </summary>
    [RelayCommand]
    private async Task PasteLink()
    {
        var clipboard = App.TopLevel?.Clipboard;
        if (clipboard is null) return;

        var text = (await clipboard.GetTextAsync())?.Trim();

        if (!NxmLink.IsNxmUri(text))
        {
            await ShowMessage(Localization["GUINexusPasteLinkTitle"], Localization["GUINexusPasteLinkEmpty"]);
            return;
        }

        await HandleLinkAsync(text!);
    }

    /// <summary>
    /// Clears a finished row after long enough to read it. Only downloads that worked, or that the
    /// user cancelled themselves, disappear on their own: a failure is the one case where the row
    /// is the only record of what went wrong, so it waits to be dismissed by hand.
    /// </summary>
    private async Task DismissWhenReadAsync(NexusDownloadModel download)
    {
        await Task.Delay(TimeSpan.FromSeconds(6));

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Downloads.Remove(download);
            HasDownloads = Downloads.Count > 0;
        });
    }

    [RelayCommand]
    private void DismissDownload(NexusDownloadModel download)
    {
        Downloads.Remove(download);
        HasDownloads = Downloads.Count > 0;
    }

    [RelayCommand]
    private void ClearFinishedDownloads()
    {
        foreach (var finished in Downloads.Where(d => d.IsFinished).ToList())
            Downloads.Remove(finished);

        HasDownloads = Downloads.Count > 0;
    }

    // ── Handler status ───────────────────────────────────────────────────────────

    /// <summary>
    /// Re-registers silently when the user has opted in but the registration has gone missing -
    /// which happens whenever a portable copy of AIM is moved or replaced. Another manager
    /// deliberately holding the protocol is left alone and reported instead.
    /// </summary>
    private void RestoreHandlerRegistration()
    {
        if (!_nexusSettings.HandlerRegistered || !NxmProtocolHandler.IsSupported()) return;

        var status = NxmProtocolHandler.GetStatus();
        if (status.IsThisExecutable) return;

        // A new local/released AIM build has a different executable path. Keep the user's
        // previous opt-in and silently move the registration to this build, but never take over
        // Vortex, ModDrop, or another unrelated manager without asking first.
        var isOlderAim = status.IsClaimedByAnother &&
                         status.HandlerName?.Contains("aim", StringComparison.OrdinalIgnoreCase) == true;
        if (status.IsClaimedByAnother && !isOlderAim) return;

        if (!NxmProtocolHandler.Register(out var error))
            Logger.Log($"Could not restore the nxm:// registration: {error}");
        else
            Logger.Log($"Updated the nxm:// registration from {status.CurrentHandler ?? "an older handler"} to {NxmProtocolHandler.GetExecutablePath()}");
    }

    private void RefreshHandlerStatus()
    {
        if (!NxmProtocolHandler.IsSupported())
        {
            _handlerIsActive = false;
            HandlerStatus = Localization["GUINexusHandlerUnsupported"];
            HandlerNeedsAttention = false;
            OnPropertyChanged(nameof(HandlerActionText));
            return;
        }

        var status = NxmProtocolHandler.GetStatus();

        if (status is { IsRegistered: true, IsThisExecutable: true })
        {
            _handlerIsActive = true;
            HandlerStatus = Localization["GUINexusHandlerActive"];
            HandlerNeedsAttention = false;
        }
        else if (status.IsClaimedByAnother)
        {
            _handlerIsActive = false;
            HandlerStatus = string.Format(Localization["GUINexusHandlerOtherApp"], status.HandlerName ?? status.CurrentHandler);
            HandlerNeedsAttention = true;
        }
        else
        {
            _handlerIsActive = false;
            HandlerStatus = Localization["GUINexusHandlerInactive"];
            HandlerNeedsAttention = true;
        }

        OnPropertyChanged(nameof(HandlerActionText));
    }

    private static Task ShowMessage(string title, string message) =>
        ShowBoxAsync(title, message, ButtonEnum.Ok);

    /// <summary>
    /// Shows a message box from any thread. The download pipeline runs on the thread pool, and a
    /// dialog opened from there would throw, so the call is posted to the UI thread and the answer
    /// comes back through a completion source.
    /// </summary>
    private static async Task<ButtonResult> ShowBoxAsync(
        string title, string message, ButtonEnum buttons, CancellationToken ct = default)
    {
        // RunContinuationsAsynchronously keeps the awaiting download from resuming inline on the
        // UI thread once the dialog closes.
        var completion = new TaskCompletionSource<ButtonResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                completion.TrySetResult(
                    await MessageBoxManager.GetMessageBoxStandard(title, message, buttons).ShowAsync());
            }
            catch (Exception e)
            {
                completion.TrySetException(e);
            }
        });

        // Cancelling the download also releases anything waiting on this dialog, so a question the
        // user never answers cannot pin the download in place.
        return await completion.Task.WaitAsync(ct);
    }
}
