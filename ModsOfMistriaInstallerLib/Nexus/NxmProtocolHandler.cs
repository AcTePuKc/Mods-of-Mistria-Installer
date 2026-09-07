using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32;

namespace Garethp.ModsOfMistriaInstallerLib.Nexus;

public record NxmHandlerStatus(bool IsRegistered, bool IsThisExecutable, string? CurrentHandler)
{
    /// <summary>Whether this AIM installation is listed as an NXM-capable application.</summary>
    public bool IsThisApplicationRegistered { get; init; }

    /// <summary>Another program (Vortex, MO2, an older copy of AIM) currently owns nxm://.</summary>
    public bool IsClaimedByAnother => IsRegistered && !IsThisExecutable;

    /// <summary>
    /// The owning program as a person would name it - "Vortex" rather than the whole registered
    /// command line, which is long enough to be cut off wherever it is shown.
    /// </summary>
    public string? HandlerName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(CurrentHandler)) return null;

            var command = CurrentHandler.Trim();
            string executable;

            // Windows records a command line: "C:\path\Vortex.exe" -d "%1"
            if (command.StartsWith('"'))
            {
                var closing = command.IndexOf('"', 1);
                executable = closing > 1 ? command[1..closing] : command[1..];
            }
            else
            {
                executable = command.Split(' ')[0];
            }

            // Linux records a .desktop file name instead.
            // Windows registrations can be inspected on Linux/macOS in tests or in diagnostic
            // data. Normalize the separator before using the host OS path APIs.
            var name = Path.GetFileNameWithoutExtension(executable.Replace('\\', '/'));
            return string.IsNullOrWhiteSpace(name) ? command : name;
        }
    }
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
    private const string WindowsProtocolKeyPath = @"Software\Classes\nxm";
    private const string WindowsProgId = "AIM.nxm";
    private const string WindowsCapabilitiesPath = @"Software\AIM\Capabilities";
    private const string WindowsRegisteredApplicationsPath = @"Software\RegisteredApplications";
    private const string WindowsRegisteredApplicationName = "AIM";
    private const string WindowsUserChoicePath =
        @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\nxm\UserChoice";
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

            if (string.IsNullOrEmpty(current))
            {
                return new NxmHandlerStatus(false, false, null)
                {
                    IsThisApplicationRegistered = OperatingSystem.IsWindows()
                        ? IsWindowsApplicationRegistered()
                        : false
                };
            }

            var isThisExecutable = PointsAtUs(current);
            return new NxmHandlerStatus(true, isThisExecutable, current)
            {
                IsThisApplicationRegistered = OperatingSystem.IsWindows()
                    ? IsWindowsApplicationRegistered()
                    : isThisExecutable
            };
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

        // On Windows every local/release build has the same file name (AIM.exe). Matching only
        // that name makes an older copy look like the current executable and prevents the
        // automatic re-registration after a portable build is replaced or moved.
        if (OperatingSystem.IsWindows())
        {
            var registeredExecutable = current.Trim();
            if (registeredExecutable.StartsWith('"'))
            {
                var closingQuote = registeredExecutable.IndexOf('"', 1);
                registeredExecutable = closingQuote > 1
                    ? registeredExecutable[1..closingQuote]
                    : registeredExecutable[1..];
            }
            else
            {
                registeredExecutable = registeredExecutable.Split(' ', 2)[0];
            }

            try
            {
                return string.Equals(
                    Path.GetFullPath(registeredExecutable),
                    Path.GetFullPath(us),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        return current.Contains(us, StringComparison.OrdinalIgnoreCase);
    }

    // ── Register / unregister ────────────────────────────────────────────────────

    /// <summary>
    /// Claims nxm:// for this executable. Returns false with a message rather than throwing:
    /// a locked-down machine refusing the write is a situation to explain, not a crash.
    /// The registry is read back after writing so callers never report success when another
    /// manager has immediately restored its own association.
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

            var status = GetStatus();
            if (!status.IsThisExecutable)
            {
                error = status.IsClaimedByAnother
                    ? $"AIM is registered, but Windows is still using {status.HandlerName ?? status.CurrentHandler} for nxm:// links.\n\nChoose AIM as the nxm:// default in Windows Default apps."
                    : "The nxm:// registration could not be verified after writing it.";
                Logger.Log($"Registration was not retained: {error}");
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

    /// <summary>
    /// Opens the Windows picker where the user can choose which registered application handles
    /// nxm:// links. Windows owns the final default-app selection; AIM must not edit UserChoice.
    /// </summary>
    public static bool OpenWindowsDefaultApps()
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:defaultapps")
            {
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception e)
        {
            Logger.Log($"Could not open Windows Default apps: {e.Message}");
            return false;
        }
    }

    // ── Windows ──────────────────────────────────────────────────────────────────

    [SupportedOSPlatform("windows")]
    private static void RegisterWindows(string executable)
    {
        var commandLine = $"\"{executable}\" \"%1\"";
        var iconPath = $"\"{executable}\",0";
        var foreignManagerHasNxmCapability = HasForeignNxmCapability();

        // Register a dedicated ProgID as well as the bare protocol fallback. The ProgID is what
        // Windows exposes in Default apps and what lets it distinguish AIM from Stardrop, ModDrop,
        // or another manager that also supports nxm://.
        using (var progId = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{WindowsProgId}"))
        {
            progId.SetValue("", "URL:Nexus Mods Protocol");
            progId.SetValue("URL Protocol", "");

            using var icon = progId.CreateSubKey("DefaultIcon");
            icon.SetValue("", iconPath);

            using var command = progId.CreateSubKey(@"shell\open\command");
            command.SetValue("", commandLine);
        }

        // If another manager has a structured registration, do not overwrite its bare fallback.
        // Managers such as Stardrop use that mismatch as a signal to repair their registration,
        // which would otherwise create a registry tug-of-war. The user can choose AIM explicitly
        // from Windows' Default apps picker using the AIM.nxm capability registered above.
        if (!foreignManagerHasNxmCapability)
        {
            using var protocol = Registry.CurrentUser.CreateSubKey(WindowsProtocolKeyPath);
            protocol.SetValue("", "URL:Nexus Mods Protocol");
            protocol.SetValue("URL Protocol", "");

            using var icon = protocol.CreateSubKey("DefaultIcon");
            icon.SetValue("", iconPath);

            using var command = protocol.CreateSubKey(@"shell\open\command");
            command.SetValue("", commandLine);
        }

        using (var capabilities = Registry.CurrentUser.CreateSubKey(WindowsCapabilitiesPath))
        {
            capabilities.SetValue("ApplicationName", "AIM - Mods of Mistria Installer");
            capabilities.SetValue("ApplicationDescription", "Handles Nexus Mods mod-manager links.");
            capabilities.SetValue("ApplicationIcon", iconPath);

            using var associations = capabilities.CreateSubKey("UrlAssociations");
            associations.SetValue(Scheme, WindowsProgId);
        }

        using var registeredApplications = Registry.CurrentUser.CreateSubKey(WindowsRegisteredApplicationsPath);
        registeredApplications.SetValue(WindowsRegisteredApplicationName, WindowsCapabilitiesPath);
    }

    [SupportedOSPlatform("windows")]
    private static void UnregisterWindows()
    {
        // Only stand down if we are the handler - blowing away another manager's
        // registration on our way out would be rude and hard to diagnose.
        var current = GetWindowsHandler();
        var protocol = GetWindowsProtocolHandler();
        if ((current is null || !PointsAtUs(current)) && (protocol is null || !PointsAtUs(protocol))) return;

        if (protocol is not null && PointsAtUs(protocol))
            Registry.CurrentUser.DeleteSubKeyTree(WindowsProtocolKeyPath, throwOnMissingSubKey: false);

        Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{WindowsProgId}", throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(WindowsCapabilitiesPath, throwOnMissingSubKey: false);

        using var registeredApplications = Registry.CurrentUser.OpenSubKey(WindowsRegisteredApplicationsPath, writable: true);
        registeredApplications?.DeleteValue(WindowsRegisteredApplicationName, throwOnMissingValue: false);
    }

    [SupportedOSPlatform("windows")]
    private static string? GetWindowsHandler()
    {
        // A UserChoice is the actual Windows selection and takes precedence over the bare
        // protocol key. Reading only Classes\nxm is what made AIM report itself as active while
        // Windows still launched Stardrop.
        using (var userChoice = Registry.CurrentUser.OpenSubKey(WindowsUserChoicePath))
        {
            var progId = userChoice?.GetValue("ProgId") as string;
            var chosenCommand = GetWindowsCommandForProgId(progId);
            if (!string.IsNullOrEmpty(chosenCommand)) return chosenCommand;
        }

        return GetWindowsProtocolHandler();
    }

    [SupportedOSPlatform("windows")]
    private static string? GetWindowsProtocolHandler()
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"{WindowsProtocolKeyPath}\shell\open\command");
        var command = key?.GetValue("") as string;
        if (!string.IsNullOrEmpty(command)) return command;

        // A machine-wide registration (an installer that ran as administrator) wins over
        // ours only if HKCU is empty, so it is worth reporting.
        using var machineKey = Registry.LocalMachine.OpenSubKey($@"{WindowsProtocolKeyPath}\shell\open\command");
        return machineKey?.GetValue("") as string;
    }

    [SupportedOSPlatform("windows")]
    private static string? GetWindowsCommandForProgId(string? progId)
    {
        if (string.IsNullOrWhiteSpace(progId)) return null;

        using var userKey = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{progId}\shell\open\command");
        var command = userKey?.GetValue("") as string;
        if (!string.IsNullOrEmpty(command)) return command;

        using var machineKey = Registry.LocalMachine.OpenSubKey($@"Software\Classes\{progId}\shell\open\command");
        return machineKey?.GetValue("") as string;
    }

    [SupportedOSPlatform("windows")]
    private static bool IsWindowsApplicationRegistered()
    {
        var command = GetWindowsCommandForProgId(WindowsProgId);
        if (string.IsNullOrWhiteSpace(command)) return false;

        using var capabilities = Registry.CurrentUser.OpenSubKey(WindowsCapabilitiesPath);
        using var associations = capabilities?.OpenSubKey("UrlAssociations");
        if (!WindowsProgId.Equals(associations?.GetValue(Scheme) as string, StringComparison.OrdinalIgnoreCase))
            return false;

        using var registeredApplications = Registry.CurrentUser.OpenSubKey(WindowsRegisteredApplicationsPath);
        return WindowsCapabilitiesPath.Equals(
            registeredApplications?.GetValue(WindowsRegisteredApplicationName) as string,
            StringComparison.OrdinalIgnoreCase);
    }

    [SupportedOSPlatform("windows")]
    private static bool HasForeignNxmCapability()
    {
        using var registeredApplications = Registry.CurrentUser.OpenSubKey(WindowsRegisteredApplicationsPath);
        if (registeredApplications is null) return false;

        foreach (var applicationName in registeredApplications.GetValueNames())
        {
            if (applicationName.Equals(WindowsRegisteredApplicationName, StringComparison.OrdinalIgnoreCase))
                continue;

            var capabilitiesPath = registeredApplications.GetValue(applicationName) as string;
            if (string.IsNullOrWhiteSpace(capabilitiesPath)) continue;

            using var associations = Registry.CurrentUser.OpenSubKey($@"{capabilitiesPath}\UrlAssociations");
            if (!string.IsNullOrWhiteSpace(associations?.GetValue(Scheme) as string)) return true;
        }

        return false;
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
