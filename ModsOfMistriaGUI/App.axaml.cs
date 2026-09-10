using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
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
        SetTheme(Settings.LoadSavedUiTheme());
        SetFontSize(Settings.LoadSavedUiFontSize());
        PerformanceDiagnostics.Log($"Startup: Avalonia resources={stopwatch.ElapsedMilliseconds} ms");
    }

    public static void SetTheme(string? preference)
    {
        if (Current is null) return;

        var theme = Settings.NormalizeTheme(preference);
        Current.RequestedThemeVariant = theme switch
        {
            "light" => ThemeVariant.Light,
            "harvest" => ThemeVariant.Light,
            "meadow-green" => ThemeVariant.Light,
            "dark" => ThemeVariant.Dark,
            "night" => ThemeVariant.Dark,
            "rose" => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };

        // Fluent supplies the stable Light/Dark control templates. AIM's custom palettes are
        // applied through dynamic application resources so they can change live.
        const string accentKey = "SystemAccentColor";
        switch (theme)
        {
            case "harvest":
                Current.Resources[accentKey] = Color.Parse("#4F8058");
                SetAIMPalette("#F7F1E2", "#FDF9EF", "#E6DAC3", "#D8C7A9", "#B09A72", "#2B3029", "#687066", "#EFE4D1", "#E9DCC4", "#FDF9EF",
                    "#F8F3E8", "#2D352D", "#FAE7E8", "#721F2C", "#12000000");
                break;
            case "meadow-green":
                Current.Resources[accentKey] = Color.Parse("#4F8058");
                SetAIMPalette("#E7F1E2", "#F6FBF3", "#AA8255", "#C09769", "#9BAF8B", "#243523", "#5B7058", "#DCEAD5", "#D4E5CB", "#F6FBF3",
                    "#EAF4E5", "#243523", "#FBE4E7", "#742238", "#10000000");
                break;
            case "night":
                Current.Resources[accentKey] = Color.Parse("#9A8BE5");
                SetAIMPalette("#181622", "#252039", "#362F4C", "#474063", "#645A85", "#F6F1FF", "#C1B9D3", "#211C32", "#201A31", "#252039",
                    "#29253C", "#F6F1FF", "#5B2637", "#FFF4F7", "#16FFFFFF");
                break;
            case "rose":
                Current.Resources[accentKey] = Color.Parse("#E69AB5");
                SetAIMPalette("#21111D", "#321A2B", "#4A2940", "#623754", "#8A5A72", "#FFF2F7", "#DDBCCA", "#2C1725", "#2D1827", "#321A2B",
                    "#321B2B", "#FFF2F7", "#66253D", "#FFF5F8", "#16FFFFFF");
                break;
            default:
                // Return to the operating system accent rather than retaining an old preset.
                Current.Resources.Remove(accentKey);
                RestoreAIMBrushes();
                break;
        }

        ApplyPaletteClass(theme);
    }

    private static readonly string[] AIMPaletteBrushKeys =
    [
        "ModStatusBackgroundBrush", "ModStatusForegroundBrush", "ModFailureBackgroundBrush",
        "ModFailureForegroundBrush", "ModAlternateRowBrush"
    ];

    private static void SetAIMPalette(string windowBackground, string surface, string button,
        string buttonHover, string border, string foreground, string mutedForeground, string menu,
        string settingsNavigation, string settingsContent,
        string statusBackground, string statusForeground, string failureBackground,
        string failureForeground, string alternateRow)
    {
        if (Current is null) return;
        Current.Resources["AIMWindowBackgroundBrush"] = new SolidColorBrush(Color.Parse(windowBackground));
        Current.Resources["AIMSurfaceBrush"] = new SolidColorBrush(Color.Parse(surface));
        Current.Resources["AIMButtonBrush"] = new SolidColorBrush(Color.Parse(button));
        Current.Resources["AIMButtonHoverBrush"] = new SolidColorBrush(Color.Parse(buttonHover));
        Current.Resources["AIMBorderBrush"] = new SolidColorBrush(Color.Parse(border));
        Current.Resources["AIMForegroundBrush"] = new SolidColorBrush(Color.Parse(foreground));
        Current.Resources["AIMMutedForegroundBrush"] = new SolidColorBrush(Color.Parse(mutedForeground));
        Current.Resources["AIMMenuBrush"] = new SolidColorBrush(Color.Parse(menu));
        Current.Resources["AIMSettingsNavigationBrush"] = new SolidColorBrush(Color.Parse(settingsNavigation));
        Current.Resources["AIMSettingsContentBrush"] = new SolidColorBrush(Color.Parse(settingsContent));
        Current.Resources["ModStatusBackgroundBrush"] = new SolidColorBrush(Color.Parse(statusBackground));
        Current.Resources["ModStatusForegroundBrush"] = new SolidColorBrush(Color.Parse(statusForeground));
        Current.Resources["ModFailureBackgroundBrush"] = new SolidColorBrush(Color.Parse(failureBackground));
        Current.Resources["ModFailureForegroundBrush"] = new SolidColorBrush(Color.Parse(failureForeground));
        Current.Resources["ModAlternateRowBrush"] = new SolidColorBrush(Color.Parse(alternateRow));
    }

    private static void RestoreAIMBrushes()
    {
        if (Current is null) return;
        foreach (var key in AIMPaletteBrushKeys)
            Current.Resources.Remove(key);

        foreach (var key in new[]
                 {
                     "AIMWindowBackgroundBrush", "AIMSurfaceBrush", "AIMButtonBrush", "AIMButtonHoverBrush",
                     "AIMBorderBrush", "AIMForegroundBrush", "AIMMutedForegroundBrush", "AIMMenuBrush",
                     "AIMSettingsNavigationBrush", "AIMSettingsContentBrush"
                 })
            Current.Resources.Remove(key);
    }

    public static void ApplyThemeClass(Window window)
    {
        window.FontSize = Settings.LoadSavedUiFontSize();
        var theme = Settings.LoadSavedUiTheme();
        window.Classes.Remove("aim-palette");
        if (theme is "harvest" or "meadow-green" or "night" or "rose")
            window.Classes.Add("aim-palette");
    }

    private static void ApplyPaletteClass(string theme)
    {
        if (Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        foreach (var window in desktop.Windows)
        {
            window.Classes.Remove("aim-palette");
            if (theme is "harvest" or "meadow-green" or "night" or "rose")
                window.Classes.Add("aim-palette");
        }
    }

    public static void SetFontSize(double preference)
    {
        if (Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        var fontSize = Settings.NormalizeUiFontSize(preference);
        foreach (var window in desktop.Windows)
            window.FontSize = fontSize;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var stopwatch = Stopwatch.StartNew();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = new MainWindow { DataContext = _mainViewModel };
            desktop.MainWindow = mainWindow;
            TopLevel = TopLevel.GetTopLevel(mainWindow);
            ApplyThemeClass(mainWindow);

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
                    _ = AIMMessageDialog.GetMessageBoxStandard(
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
