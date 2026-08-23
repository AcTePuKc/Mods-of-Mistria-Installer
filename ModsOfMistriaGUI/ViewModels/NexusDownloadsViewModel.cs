using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Garethp.ModsOfMistriaGUI.Models;
using Garethp.ModsOfMistriaGUI.Views;
using Garethp.ModsOfMistriaInstallerLib;
using Garethp.ModsOfMistriaInstallerLib.Nexus;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

namespace Garethp.ModsOfMistriaGUI.ViewModels;

/// <summary>
/// Everything behind the "Mod Manager Download" button on the Nexus website: the API key, the
/// protocol registration that makes the button reach AIM at all, and the downloads themselves.
/// </summary>
public partial class NexusDownloadsViewModel : ViewModelBase
{
    private readonly Settings _settings;
    private readonly NexusSettings _nexusSettings;
    private readonly NxmDownloadService _service;

    /// <summary>Raised after a download installs, so the mod list can pick the new folder up.</summary>
    public event EventHandler? ModsChanged;

    public NexusDownloadsViewModel(Settings settings, NexusSettings? nexusSettings = null)
    {
        _settings = settings;
        _nexusSettings = nexusSettings ?? new NexusSettings();
        _service = new NxmDownloadService(_nexusSettings);

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
    }

    public ObservableCollection<NexusDownloadModel> Downloads { get; } = [];

    [ObservableProperty] private bool _hasDownloads;

    [ObservableProperty] private string _handlerStatus = "";

    [ObservableProperty] private bool _handlerNeedsAttention;

    public bool HasApiKey => _nexusSettings.HasApiKey();

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

        if (!link.IsForMistria())
        {
            await ShowMessage(Localization["GUINexusDownloadFailedTitle"],
                string.Format(Localization["GUINexusWrongGame"], link.Game));
            return;
        }

        if (!_nexusSettings.HasApiKey())
        {
            var owner = App.TopLevel as Window;
            if (owner is null) return;

            var saved = await NexusApiKeyWindow.ShowAsync(owner, _nexusSettings);
            OnPropertyChanged(nameof(HasApiKey));
            if (!saved) return;
        }

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
            folders => ConfirmOverwriteAsync(folders, download.Token),
            download.Token));

        download.Title = result.FileName;

        if (result.Success)
        {
            ModsChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (!result.Cancelled && result.Error is not null)
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

    [RelayCommand]
    private async Task SetApiKey()
    {
        if (App.TopLevel is not Window owner) return;

        await NexusApiKeyWindow.ShowAsync(owner, _nexusSettings);
        OnPropertyChanged(nameof(HasApiKey));
    }

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
            NxmProtocolHandler.Unregister(out _);
            _nexusSettings.HandlerRegistered = false;
            RefreshHandlerStatus();
            return;
        }

        if (status.IsClaimedByAnother)
        {
            var confirm = await MessageBoxManager.GetMessageBoxStandard(
                Localization["GUINexusHandlerTitle"],
                string.Format(Localization["GUINexusHandlerTakeOver"], status.CurrentHandler),
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
        if (status.IsRegistered) return;

        if (!NxmProtocolHandler.Register(out var error))
            Logger.Log($"Could not restore the nxm:// registration: {error}");
    }

    private void RefreshHandlerStatus()
    {
        if (!NxmProtocolHandler.IsSupported())
        {
            HandlerStatus = Localization["GUINexusHandlerUnsupported"];
            HandlerNeedsAttention = false;
            return;
        }

        var status = NxmProtocolHandler.GetStatus();

        if (status is { IsRegistered: true, IsThisExecutable: true })
        {
            HandlerStatus = Localization["GUINexusHandlerActive"];
            HandlerNeedsAttention = false;
        }
        else if (status.IsClaimedByAnother)
        {
            HandlerStatus = string.Format(Localization["GUINexusHandlerOtherApp"], status.CurrentHandler);
            HandlerNeedsAttention = true;
        }
        else
        {
            HandlerStatus = Localization["GUINexusHandlerInactive"];
            HandlerNeedsAttention = true;
        }
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
