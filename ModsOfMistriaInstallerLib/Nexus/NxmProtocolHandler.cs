using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32;

namespace Garethp.ModsOfMistriaInstallerLib.Nexus;

public record NxmHandlerStatus(bool IsRegistered, bool IsThisExecutable, string? CurrentHandler)
{
    /// <summary>Another program (Vortex, MO2, an older copy of AIM) currently owns nxm://.</summary>
    public bool IsClaimedByAnother => IsRegistered && !IsThisExecutable;
}

/// <summary>
/// Registers AIM as the operating system's handler for <c>nxm://</c> links, which is what makes
/// the "Mod Manager Download" button on the Nexus website reach us at all. This is the same
/// mechanism Vortex and Mod Organizer 2 use.
///
/// Everything is written per-user (HKCU on Windows, ~/.local/share on Linux) so that registering
/// never needs administrator rights and never affects other accounts on the machine.
/// </summary>
public static class NxmProtocolHandler
{
    private const string Scheme = "nxm";
    private const string WindowsKeyPath = @"Software\Classes\nxm";
    private const string LinuxDesktopFileName = "aim-nxm-handler.desktop";

    /// <summary>
    /// The executable to hand nxm links to. Under a single-file publish this is the real
    /// launcher; inside an AppImage it is the AppImage itself, which is the thing the
    /// desktop entry must point at.
    /// </summary>
    public static string GetExecutablePath()
    {
        var appImage = Environment.GetEnvironmentVariable("APPIMAGE");
        if (!string.IsNullOrEmpty(appImage) && File.Exists(appImage)) return appImage;

        return Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";
    }

    public static bool IsSupported() => OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

    // ── Status ───────────────────────────────────────────────────────────────────

    public static NxmHandlerStatus GetStatus()
    {
        try
        {
            var current = OperatingSystem.IsWindows() ? GetWindowsHandler()
                : OperatingSystem.IsLinux() ? GetLinuxHandler()
                : null;

            if (string.IsNullOrEmpty(current)) return new NxmHandlerStatus(false, false, null);

            return new NxmHandlerStatus(true, PointsAtUs(current), current);
        }
        catch (Exception e)
        {
            Logger.Log($"Could not read the nxm:// handler registration: {e.Message}");
            return new NxmHandlerStatus(false, false, null);
        }
    }

    private static bool PointsAtUs(string current)
    {
        var us = GetExecutablePath();
        if (string.IsNullOrEmpty(us)) return false;

        // On Linux the recorded handler is a .desktop file name rather than a path, so the
        // match is on ours specifically; on Windows it is the command line, which contains
        // the executable path.
        if (current.Equals(LinuxDesktopFileName, StringComparison.OrdinalIgnoreCase)) return true;

        return current.Contains(us, StringComparison.OrdinalIgnoreCase) ||
               current.Contains(Path.GetFileName(us), StringComparison.OrdinalIgnoreCase);
    }

    // ── Register / unregister ────────────────────────────────────────────────────

    /// <summary>
    /// Claims nxm:// for this executable. Returns false with a message rather than throwing:
    /// a locked-down machine refusing the write is a situation to explain, not a crash.
    /// </summary>
    public static bool Register(out string? error)
    {
        error = null;
        var executable = GetExecutablePath();

        if (string.IsNullOrEmpty(executable) || !File.Exists(executable))
        {
            error = "Could not work out where the installer is running from.";
            return false;
        }

        try
        {
            if (OperatingSystem.IsWindows()) RegisterWindows(executable);
            else if (OperatingSystem.IsLinux()) RegisterLinux(executable);
            else
            {
                error = "Registering nxm:// links is only supported on Windows and Linux.";
                return false;
            }

            Logger.Log($"Registered {Scheme}:// links to {executable}");
            return true;
        }
        catch (Exception e)
        {
            error = e.Message;
            Logger.Log($"Failed to register {Scheme}:// links: {e.Message}");
            return false;
        }
    }

    public static bool Unregister(out string? error)
    {
        error = null;

        try
        {
            if (OperatingSystem.IsWindows()) UnregisterWindows();
            else if (OperatingSystem.IsLinux()) UnregisterLinux();
            return true;
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }
    }

    // ── Windows ──────────────────────────────────────────────────────────────────

    [SupportedOSPlatform("windows")]
    private static void RegisterWindows(string executable)
    {
        using var key = Registry.CurrentUser.CreateSubKey(WindowsKeyPath);
        key.SetValue("", "URL:NXM Protocol");
        key.SetValue("URL Protocol", "");

        using (var icon = key.CreateSubKey("DefaultIcon"))
            icon.SetValue("", $"\"{executable}\",0");

        using var command = key.CreateSubKey(@"shell\open\command");
        command.SetValue("", $"\"{executable}\" \"%1\"");
    }

    [SupportedOSPlatform("windows")]
    private static void UnregisterWindows()
    {
        // Only stand down if we are the handler - blowing away another manager's
        // registration on our way out would be rude and hard to diagnose.
        var current = GetWindowsHandler();
        if (current is null || !PointsAtUs(current)) return;

        Registry.CurrentUser.DeleteSubKeyTree(WindowsKeyPath, throwOnMissingSubKey: false);
    }

    [SupportedOSPlatform("windows")]
    private static string? GetWindowsHandler()
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"{WindowsKeyPath}\shell\open\command");
        var command = key?.GetValue("") as string;
        if (!string.IsNullOrEmpty(command)) return command;

        // A machine-wide registration (an installer that ran as administrator) wins over
        // ours only if HKCU is empty, so it is worth reporting.
        using var machineKey = Registry.LocalMachine.OpenSubKey($@"{WindowsKeyPath}\shell\open\command");
        return machineKey?.GetValue("") as string;
    }

    // ── Linux ────────────────────────────────────────────────────────────────────

    private static string LinuxApplicationsDirectory =>
        Path.Combine(GetXdgDirectory("XDG_DATA_HOME", ".local/share"), "applications");

    private static string LinuxMimeAppsPath =>
        Path.Combine(GetXdgDirectory("XDG_CONFIG_HOME", ".config"), "mimeapps.list");

    private static string GetXdgDirectory(string variable, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        if (!string.IsNullOrEmpty(value)) return value;

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), fallback);
    }

    [UnsupportedOSPlatform("windows")]
    private static void RegisterLinux(string executable)
    {
        Directory.CreateDirectory(LinuxApplicationsDirectory);
        var desktopPath = Path.Combine(LinuxApplicationsDirectory, LinuxDesktopFileName);

        var desktopEntry = new StringBuilder()
            .AppendLine("[Desktop Entry]")
            .AppendLine("Type=Application")
            .AppendLine("Name=AIM - Mods of Mistria Installer")
            .AppendLine("Comment=Handles Nexus Mods \"Mod Manager Download\" links")
            .AppendLine($"Exec=\"{executable}\" %u")
            .AppendLine("Terminal=false")
            .AppendLine("NoDisplay=true")
            .AppendLine("Categories=Game;")
            .AppendLine($"MimeType=x-scheme-handler/{Scheme};")
            .ToString();

        File.WriteAllText(desktopPath, desktopEntry);

        try
        {
            File.SetUnixFileMode(desktopPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        }
        catch
        {
            // Not fatal: the entry works without the execute bit on most desktops.
        }

        SetLinuxDefaultApplication(LinuxDesktopFileName);

        // These keep the desktop's caches honest. Both are optional - a Steam Deck in game
        // mode may have neither - so failure is ignored.
        RunQuietly("update-desktop-database", LinuxApplicationsDirectory);
        RunQuietly("xdg-mime", $"default {LinuxDesktopFileName} x-scheme-handler/{Scheme}");
    }

    private static void UnregisterLinux()
    {
        var desktopPath = Path.Combine(LinuxApplicationsDirectory, LinuxDesktopFileName);
        if (File.Exists(desktopPath)) File.Delete(desktopPath);

        if (GetLinuxHandler() == LinuxDesktopFileName) SetLinuxDefaultApplication(null);

        RunQuietly("update-desktop-database", LinuxApplicationsDirectory);
    }

    private static string? GetLinuxHandler()
    {
        var path = LinuxMimeAppsPath;
        if (!File.Exists(path)) return null;

        var inDefaults = false;
        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.StartsWith('['))
            {
                inDefaults = line.Equals("[Default Applications]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inDefaults || !line.StartsWith($"x-scheme-handler/{Scheme}=")) continue;

            var value = line.Split('=', 2)[1].Split(';', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        return null;
    }

    /// <summary>
    /// Rewrites the <c>x-scheme-handler/nxm</c> line in mimeapps.list, adding the
    /// [Default Applications] section if the file does not have one. Passing null removes
    /// the line. Editing the file directly means registration still works where xdg-mime
    /// is missing, which is the normal state of affairs on a Steam Deck.
    /// </summary>
    private static void SetLinuxDefaultApplication(string? desktopFileName)
    {
        var path = LinuxMimeAppsPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : [];
        var entry = $"x-scheme-handler/{Scheme}={desktopFileName}";

        var sectionIndex = lines.FindIndex(line =>
            line.Trim().Equals("[Default Applications]", StringComparison.OrdinalIgnoreCase));

        if (sectionIndex < 0)
        {
            if (desktopFileName is null) return;

            if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1])) lines.Add("");
            lines.Add("[Default Applications]");
            lines.Add(entry);
            File.WriteAllLines(path, lines);
            return;
        }

        var sectionEnd = lines.FindIndex(sectionIndex + 1, line => line.TrimStart().StartsWith('['));
        if (sectionEnd < 0) sectionEnd = lines.Count;

        var existing = lines.FindIndex(sectionIndex + 1, sectionEnd - sectionIndex - 1,
            line => line.TrimStart().StartsWith($"x-scheme-handler/{Scheme}=", StringComparison.OrdinalIgnoreCase));

        if (existing >= 0)
        {
            if (desktopFileName is null) lines.RemoveAt(existing);
            else lines[existing] = entry;
        }
        else if (desktopFileName is not null)
        {
            lines.Insert(sectionIndex + 1, entry);
        }

        File.WriteAllLines(path, lines);
    }

    private static void RunQuietly(string fileName, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            process?.WaitForExit(5000);
        }
        catch
        {
            // The tool is not installed. The direct file edits above already did the work.
        }
    }
}
