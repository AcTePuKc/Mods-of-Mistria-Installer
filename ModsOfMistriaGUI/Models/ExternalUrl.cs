using System.Diagnostics;
using Garethp.ModsOfMistriaInstallerLib;

namespace Garethp.ModsOfMistriaGUI.Models;

internal static class ExternalUrl
{
    public static bool IsAllowed(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps;

    /// <summary>
    /// Opens a link in the user's browser, but only an https one.
    ///
    /// The check is not ceremony: several of these URLs are built from mod names and Nexus ids that
    /// came out of a mod's own manifest, and ShellExecute will happily run whatever scheme it is
    /// handed.
    /// </summary>
    public static void Open(string? url)
    {
        if (!IsAllowed(url)) return;

        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception exception)
        {
            Logger.Log($"Could not open {url}: {exception.Message}");
        }
    }
}
