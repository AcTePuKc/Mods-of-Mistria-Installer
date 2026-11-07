using System.Reflection;

namespace Garethp.ModsOfMistriaInstallerLib.Utils;

/// <summary>
/// The exact AIM release which is building an archive. This is intentionally
/// separate from <see cref="InstallerVersion.ModCompatibilityVersion"/>, which
/// is the upstream MOMI manifest level AIM implements, not AIM's own release.
/// </summary>
public static class InstallerBuildVersion
{
    public static string? Current()
    {
        var assembly = Assembly.GetEntryAssembly();
        var version = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? assembly?.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
        if (string.IsNullOrWhiteSpace(version)) return null;

        var buildMetadata = version.IndexOf('+');
        return buildMetadata < 0 ? version : version[..buildMetadata];
    }
}
