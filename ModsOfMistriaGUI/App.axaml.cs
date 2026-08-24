using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System.Diagnostics;
using Garethp.ModsOfMistriaGUI.Models;
using Garethp.ModsOfMistriaGUI.Services;
using Garethp.ModsOfMistriaGUI.ViewModels;
using Garethp.ModsOfMistriaGUI.Views;
using Garethp.ModsOfMistriaInstallerLib;
using Garethp.ModsOfMistriaInstallerLib.Nexus;
using MsBox.Avalonia;
using Newtonsoft.Json.Linq;

namespace Garethp.ModsOfMistriaGUI;

public class App : Application
{
    public static TopLevel? TopLevel { get; private set; }

    /// <summary>
    /// An nxm:// link this process was started with, set by <see cref="Program"/> before the UI
    /// exists. It is handled once the main window is up.
    /// </summary>
    public static string? StartupNxmLink { get; set; }

    private readonly MainWindowViewModel _mainViewModel;
    private CancellationTokenSource? _updateCheckCancellation;
    private NxmLinkListener? _nxmListener;

    public App()
    {
        var stopwatch = Stopwatch.StartNew();
        LocalizationService.Instance.SetLanguage(Settings.LoadSavedUiLanguage());
        _mainViewModel = new MainWindowViewModel();
        PerformanceDiagnostics.Log($"Startup: App + MainWindowViewModel construction={stopwatch.ElapsedMilliseconds} ms");
    }

    public override void Initialize()
    {
        var stopwatch = Stopwatch.StartNew();
        AvaloniaXamlLoader.Load(this);
        PerformanceDiagnostics.Log($"Startup: Avalonia resources={stopwatch.ElapsedMilliseconds} ms");
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var stopwatch = Stopwatch.StartNew();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = new MainWindow { DataContext = _mainViewModel };
            desktop.MainWindow = mainWindow;
            TopLevel = TopLevel.GetTopLevel(mainWindow);

            _updateCheckCancellation = new CancellationTokenSource();

            // Deliberately not done in the view model constructor: it touches the registry and the
            // user's config directory, which the headless UI tests must not do.
            _mainViewModel.Nexus.Initialise();

            // Links clicked while this window is open arrive here from the short-lived process the
            // browser started. Failing to listen is not fatal: those processes then handle their
            // own link in a second window.
            _nxmListener = NxmLinkListener.TryStart(link =>
                Dispatcher.UIThread.Post(() => HandleNxmLink(mainWindow, link)));

            mainWindow.Closed += (_, _) =>
            {
                _mainViewModel.SaveCurrentState();
                _updateCheckCancellation.Cancel();
                // Disposal waits on a background accept loop, so it must not run on the UI thread.
                var listener = _nxmListener;
                _nxmListener = null;
                Task.Run(() => listener?.Dispose());
                ArchiveWorkerClient.StopAll();
            };

            if (StartupNxmLink is not null)
            {
                var startupLink = StartupNxmLink;
                StartupNxmLink = null;
                Dispatcher.UIThread.Post(() => HandleNxmLink(mainWindow, startupLink));
            }

            if (Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess)
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    MessageBoxManager.GetMessageBoxStandard(
                        ModsOfMistriaInstallerLib.Lang.Resources.GUIWarning32BitTitle,
                        ModsOfMistriaInstallerLib.Lang.Resources.GUIWarning32Bit
                    ).ShowAsync();
                });
            }

            // Disabled in this isolated Nexus/sandbox test build. The normal
            // AIM build keeps the GitHub Releases update check enabled.
        }

        PerformanceDiagnostics.Log($"Startup: framework initialization={stopwatch.ElapsedMilliseconds} ms");

        base.OnFrameworkInitializationCompleted();
    }

    private void HandleNxmLink(Window mainWindow, string link)
    {
        // The click happened in the browser, so the window is behind it and the user would
        // otherwise have no sign that anything is downloading.
        mainWindow.Activate();
        _ = _mainViewModel.HandleNxmLinkAsync(link);
    }

    private async Task CheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var currentVersion = Version.Parse(AppInfo.Version);
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "AIM");
            using var response = await client.GetAsync(AppInfo.ReleaseApiUrl, cancellationToken);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var releases = JArray.Parse(json);
            var aimRelease = releases.FirstOrDefault(release =>
            {
                var name = release["name"]?.ToString() ?? "";
                var tag = release["tag_name"]?.ToString() ?? "";
                return name.StartsWith("AIM ", StringComparison.OrdinalIgnoreCase)
                       || tag.StartsWith("aim-", StringComparison.OrdinalIgnoreCase);
            });
            var tagName = aimRelease?["tag_name"]?.ToString();
            if (tagName is null) return;

            var latestVersion = Version.Parse(tagName.TrimStart('v'));
            if (latestVersion <= currentVersion || cancellationToken.IsCancellationRequested) return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!cancellationToken.IsCancellationRequested)
                    _mainViewModel.ShowUpdateAvailable(latestVersion.ToString(3));
            });
        }
        catch (OperationCanceledException)
        {
            // Expected when the main window closes during the request.
        }
        catch (Exception)
        {
            // Update checks are advisory and must never prevent startup.
        }
    }
}
