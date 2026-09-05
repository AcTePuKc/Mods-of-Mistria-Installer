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

    /// <summary>
    /// Opens a folder in the user's file manager.
    ///
    /// Kept apart from <see cref="Open"/> rather than folded into it, because the https check is
    /// the whole point of that method and a path would fail it. A folder that does not exist is
    /// created first: the crash archive's folder is only made when the first crash is captured, and
    /// "there is nowhere to look yet" is better shown as an empty folder than as nothing happening.
    /// </summary>
    public static void OpenFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception exception)
        {
            Logger.Log($"Could not open {path}: {exception.Message}");
        }
    }
}
