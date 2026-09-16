using System.Reflection;

namespace Garethp.ModsOfMistriaGUI;

public static class AppInfo
{
    public const bool IsNexusDistribution =
#if AIM_NEXUS_DISTRIBUTION
        true;
#else
        false;
#endif

    public const string GitHubUrl = "https://github.com/AcTePuKc/Mods-of-Mistria-Installer";
    public const string ReleasesUrl = "https://github.com/AcTePuKc/Mods-of-Mistria-Installer/releases";
    public const string ReleaseApiUrl = "https://api.github.com/repos/AcTePuKc/Mods-of-Mistria-Installer/releases?per_page=20";
    public const string SupportedGame = "Fields of Mistria 1.0.x";
    public const string GameLaunchUri = "steam://rungameid/2142790";
    public static string Version
    {
        get
        {
            // Use AIM's assembly rather than EntryAssembly. The latter is the
            // test host when this code runs under the headless UI test suite.
            var assembly = typeof(AppInfo).Assembly;
            // InformationalVersion may be supplied by CI/source-control tooling
            // (for example, a game/mod version). The project FileVersion is the
            // authoritative AIM application version shown to users.
            var value = assembly?.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version
                        ?? assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                        ?? assembly?.GetName().Version?.ToString();
            return value is null ? "unknown" : TrimBuildSuffix(value);
        }
    }

    public static string DisplayVersion => $"AIM {Version}";

    /// <summary>
    /// Distinguishes AIM releases from the historical MOMI releases that predate the fork in this
    /// repository. Version numbers alone are not enough: MOMI 0.15.6 is newer than AIM 0.2.0 to a
    /// semantic-version comparer, but is not an AIM update.
    /// </summary>
    public static bool IsAimRelease(string? releaseName) =>
        releaseName?.StartsWith("AIM ", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// GitHub pre-release tags such as v0.3.0-rc.1 are valid AIM releases, but
    /// <see cref="System.Version"/> does not parse their suffix. Compare their numeric core while
    /// retaining the complete tag for the message and exact release URL.
    /// </summary>
    public static bool TryParseReleaseVersion(string? tagName, out Version version)
    {
        var normalized = tagName?.Trim().TrimStart('v', 'V');
        var suffix = normalized?.IndexOfAny(['-', '+']) ?? -1;
        if (suffix >= 0) normalized = normalized![..suffix];
        return System.Version.TryParse(normalized, out version!) && version.Build >= 0;
    }

    public static string ReleaseUrlForTag(string tagName) =>
        $"{ReleasesUrl}/tag/{Uri.EscapeDataString(tagName)}";

    private static string TrimBuildSuffix(string value)
    {
        var plus = value.IndexOf('+');
        if (plus >= 0) value = value[..plus];
        if (System.Version.TryParse(value, out var version) && version.Revision == 0)
            return version.ToString(3);
        return value;
    }
}
