using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json;

namespace Garethp.ModsOfMistriaGUI.Models;

public partial class Settings : ObservableObject
{
    public const double DefaultUiFontSize = 12;
    public const double MinimumUiFontSize = 11;
    public const double MaximumUiFontSize = 16;

    public Settings()
    {
    }

    public Settings(string? mistriaLocation, string? modsLocation)
    {
        MistriaLocation = mistriaLocation ?? "";
        ModsLocation = modsLocation ?? "";
    }

    [ObservableProperty] private string _mistriaLocation = "";

    [ObservableProperty] private string _modsLocation = "";

    [ObservableProperty] private bool _launchGameDirectly;

    [ObservableProperty] private string _uiLanguage = "system";

    // "system" deliberately remains the default: a first launch follows the operating system
    // exactly as AIM always has. Explicit choices are remembered independently of language.
    [ObservableProperty] private string _uiTheme = "system";

    // This is text size in Avalonia's device-independent pixels, not display/DPI scaling. The
    // window value is inherited by its controls, so it remains a readable preference on every OS.
    [ObservableProperty] private double _uiFontSize = DefaultUiFontSize;

    // A dismissed update is remembered only for that exact version. A later
    // release remains visible instead of being hidden permanently.
    [ObservableProperty] private string? _dismissedUpdateVersion;

    /// <summary>
    /// Folders AIM watches for mods downloaded by hand - normally the browser's downloads folder.
    ///
    /// A plain list rather than an observable property: it is edited through
    /// <see cref="SetDropFolders"/>, which is also where it is normalised and written out, so
    /// there is one place that can put a duplicate or a blank into it.
    /// </summary>
    public List<string> DropFolders { get; private set; } = [];

    public void SetDropFolders(IEnumerable<string> folders)
    {
        DropFolders = folders
            .Where(folder => !string.IsNullOrWhiteSpace(folder))
            .Select(folder => folder.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        OnPropertyChanged(nameof(DropFolders));
        SavePreferences();
    }

    private static string PreferencesPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AIM",
        "settings.json");

    private static string LegacyPreferencesPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MOMI",
        "settings.json");

    partial void OnLaunchGameDirectlyChanged(bool value)
        => SavePreferences();

    partial void OnUiLanguageChanged(string value)
        => SavePreferences();

    partial void OnUiThemeChanged(string value)
        => SavePreferences();

    partial void OnUiFontSizeChanged(double value)
        => SavePreferences();

    partial void OnDismissedUpdateVersionChanged(string? value)
        => SavePreferences();

    private void SavePreferences()
    {
        try
        {
            var directory = Path.GetDirectoryName(PreferencesPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(PreferencesPath, JsonSerializer.Serialize(
                new LaunchPreferences(LaunchGameDirectly, UiLanguage, DismissedUpdateVersion, DropFolders, UiTheme, UiFontSize)));
        }
        catch
        {
            // A preference must never prevent MOMI from starting or launching the game.
        }
    }

    public void LoadPreferences()
    {
        try
        {
            var path = File.Exists(PreferencesPath) ? PreferencesPath : LegacyPreferencesPath;
            if (!File.Exists(path)) return;
            var preferences = JsonSerializer.Deserialize<LaunchPreferences>(File.ReadAllText(path));
            if (preferences is not null)
            {
                LaunchGameDirectly = preferences.LaunchGameDirectly;
                UiLanguage = string.IsNullOrWhiteSpace(preferences.UiLanguage) ? "system" : preferences.UiLanguage;
                UiTheme = NormalizeTheme(preferences.UiTheme);
                UiFontSize = NormalizeUiFontSize(preferences.UiFontSize);
                DismissedUpdateVersion = preferences.DismissedUpdateVersion;
                DropFolders = preferences.DropFolders ?? [];
            }
        }
        catch
        {
            LaunchGameDirectly = false;
        }
    }

    public static string LoadSavedUiLanguage()
    {
        try
        {
            var path = File.Exists(PreferencesPath) ? PreferencesPath : LegacyPreferencesPath;
            if (!File.Exists(path)) return "system";
            var preferences = JsonSerializer.Deserialize<LaunchPreferences>(File.ReadAllText(path));
            return string.IsNullOrWhiteSpace(preferences?.UiLanguage) ? "system" : preferences.UiLanguage;
        }
        catch { return "system"; }
    }

    public static string LoadSavedUiTheme()
    {
        try
        {
            var path = File.Exists(PreferencesPath) ? PreferencesPath : LegacyPreferencesPath;
            if (!File.Exists(path)) return "system";
            var preferences = JsonSerializer.Deserialize<LaunchPreferences>(File.ReadAllText(path));
            return NormalizeTheme(preferences?.UiTheme);
        }
        catch { return "system"; }
    }

    public static double LoadSavedUiFontSize()
    {
        try
        {
            var path = File.Exists(PreferencesPath) ? PreferencesPath : LegacyPreferencesPath;
            if (!File.Exists(path)) return DefaultUiFontSize;
            var preferences = JsonSerializer.Deserialize<LaunchPreferences>(File.ReadAllText(path));
            return NormalizeUiFontSize(preferences?.UiFontSize ?? DefaultUiFontSize);
        }
        catch { return DefaultUiFontSize; }
    }

    public static string NormalizeTheme(string? value) => value?.ToLowerInvariant() switch
    {
        "light" => "light",
        "dark" => "dark",
        // 0.2.0-preview called the warm parchment preset "meadow". Preserve that user's
        // choice when it is renamed to Harvest; the new green Meadow has its own stable value.
        "meadow" => "harvest",
        "harvest" => "harvest",
        "meadow-green" => "meadow-green",
        "night" => "night",
        "rose" => "rose",
        _ => "system"
    };

    public static double NormalizeUiFontSize(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return DefaultUiFontSize;
        return Math.Clamp(Math.Round(value), MinimumUiFontSize, MaximumUiFontSize);
    }

    private sealed record LaunchPreferences(
        bool LaunchGameDirectly,
        string UiLanguage = "system",
        string? DismissedUpdateVersion = null,
        List<string>? DropFolders = null,
        string UiTheme = "system",
        double UiFontSize = DefaultUiFontSize);

    public bool ValidMistriaLocation() => !string.IsNullOrEmpty(MistriaLocation) &&
                                          Directory.Exists(MistriaLocation) &&
                                          (File.Exists(Path.Combine(MistriaLocation, "assets.zip")) ||
                                           Directory.Exists(Path.Combine(MistriaLocation, "assets")));

    public bool ValidModsLocation() => !string.IsNullOrEmpty(ModsLocation) &&
                                       Directory.Exists(ModsLocation);

    public bool WrongMistriaVersion() => !string.IsNullOrEmpty(MistriaLocation) && Directory.Exists(MistriaLocation) &&
                                         (File.Exists(Path.Combine(MistriaLocation, "FieldsOfMistria.exe")) ||
                                          File.Exists(Path.Combine(MistriaLocation, "FieldsOfMistria"))) &&
                                         !File.Exists(Path.Combine(MistriaLocation, "assets.zip"));
}
