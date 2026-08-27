using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Garethp.ModsOfMistriaInstallerLib.Nexus;

/// <summary>
/// The small amount of state AIM has to keep between runs for Nexus downloads: the user's
/// OAuth session, and whether they have asked AIM to be the nxm:// handler.
///
/// It lives outside the mods folder (unlike profiles) because it is per-user, not per-install,
/// and because a mods folder is something people zip up and share - an OAuth session must not ride
/// along with it.
///
/// On Windows the session is encrypted with DPAPI, so a copied settings file is useless on another
/// machine or account. Elsewhere it is stored plainly in a file with owner-only permissions,
/// which is the same posture as other mod managers on Linux.
/// </summary>
public class NexusSettings
{
    private const string LegacyApiKeyField = "nexusApiKey";
    private const string LegacyProtectedApiKeyField = "nexusApiKeyProtected";
    private const string ProtectedOAuthTokensField = "nexusOAuthTokensProtected";
    private const string OAuthTokensField = "nexusOAuthTokens";
    private const string HandlerRegisteredField = "nxmHandlerRegistered";
    private const string HandlerPromptField = "nxmHandlerPromptAnswered";

    private readonly string _path;
    private JObject _data;

    public NexusSettings(string? configDirectory = null)
    {
        var directory = configDirectory ?? GetDefaultConfigDirectory();
        Directory.CreateDirectory(directory);

        // Kept apart from the AIM settings.json that holds launch and language preferences:
        // that file is written on every preference change and is a reasonable thing to copy
        // between machines, which an OAuth session is not.
        _path = Path.Combine(directory, "nexus.json");
        _data = Load();
        RemoveLegacyApiKeyData();
    }

    public static string GetDefaultConfigDirectory()
    {
        var overrideDir = Environment.GetEnvironmentVariable("AIM_CONFIG_DIR");
        if (!string.IsNullOrEmpty(overrideDir)) return overrideDir;

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        // A sandboxed or otherwise odd environment can hand back an empty path; the home
        // directory is a workable stand-in and beats writing next to the executable.
        if (string.IsNullOrEmpty(appData))
            appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");

        return Path.Combine(appData, "AIM");
    }

    // ── OAuth session ────────────────────────────────────────────────────────────

    public NexusOAuthTokens? GetOAuthTokens()
    {
        var protectedValue = _data.Value<string>(ProtectedOAuthTokensField);
        var serialized = !string.IsNullOrEmpty(protectedValue)
            ? TryUnprotect(protectedValue)
            : _data.Value<string>(OAuthTokensField);

        if (string.IsNullOrWhiteSpace(serialized)) return null;

        try
        {
            return JObject.Parse(serialized).ToObject<NexusOAuthTokens>();
        }
        catch
        {
            Logger.Log("Could not read the stored Nexus OAuth session; signing in again will be required.");
            return null;
        }
    }

    public bool HasOAuthTokens() => GetOAuthTokens() is not null;

    /// <summary>Stores an OAuth token response. Personal API keys are never accepted here.</summary>
    public void SetOAuthTokens(NexusOAuthTokens? tokens)
    {
        _data.Remove(ProtectedOAuthTokensField);
        _data.Remove(OAuthTokensField);

        if (tokens is not null)
        {
            var serialized = JObject.FromObject(tokens).ToString(Newtonsoft.Json.Formatting.None);
            var protectedValue = TryProtect(serialized);

            if (protectedValue is not null) _data[ProtectedOAuthTokensField] = protectedValue;
            else _data[OAuthTokensField] = serialized;
        }

        Save();
    }

    // ── Handler registration ─────────────────────────────────────────────────────

    /// <summary>
    /// Whether the user has opted in to AIM handling nxm:// links. This is our own record of
    /// intent; <see cref="NxmProtocolHandler.IsRegistered"/> reports what the OS actually thinks,
    /// which can differ if another manager has since claimed the protocol.
    /// </summary>
    public bool HandlerRegistered
    {
        get => _data.Value<bool?>(HandlerRegisteredField) ?? false;
        set
        {
            _data[HandlerRegisteredField] = value;
            Save();
        }
    }

    /// <summary>
    /// Whether the user has already been asked to let AIM handle nxm:// links. Recorded whichever
    /// way they answered, so the offer is made once rather than at every launch.
    /// </summary>
    public bool HandlerPromptAnswered
    {
        get => _data.Value<bool?>(HandlerPromptField) ?? false;
        set
        {
            _data[HandlerPromptField] = value;
            Save();
        }
    }

    // ── Storage ──────────────────────────────────────────────────────────────────

    private JObject Load()
    {
        try
        {
            if (File.Exists(_path)) return JObject.Parse(File.ReadAllText(_path));
        }
        catch (Exception e)
        {
            Logger.Log($"Could not read the Nexus settings, starting fresh: {e.Message}");
        }

        return new JObject();
    }

    private void RemoveLegacyApiKeyData()
    {
        // The API-key feature is intentionally retired. Remove both forms immediately so an
        // upgraded AIM cannot accidentally use or retain a credential the user entered before
        // the OAuth migration.
        var removed = _data.Remove(LegacyApiKeyField);
        removed |= _data.Remove(LegacyProtectedApiKeyField);
        if (removed) Save();
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(_path, _data.ToString());
            RestrictPermissions(_path);
        }
        catch (Exception e)
        {
            Logger.Log($"Could not save the Nexus settings: {e.Message}");
        }
    }

    private static void RestrictPermissions(string path)
    {
        if (OperatingSystem.IsWindows()) return;

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch
        {
            // Best effort: a filesystem that cannot express permissions is not a reason to fail.
        }
    }

    // ── DPAPI (Windows only) ─────────────────────────────────────────────────────

    private static string? TryProtect(string value)
    {
        if (!OperatingSystem.IsWindows()) return null;

        try
        {
            return Convert.ToBase64String(Protect(Encoding.UTF8.GetBytes(value)));
        }
        catch
        {
            return null;
        }
    }

    private static string? TryUnprotect(string value)
    {
        if (!OperatingSystem.IsWindows()) return null;

        try
        {
            return Encoding.UTF8.GetString(Unprotect(Convert.FromBase64String(value)));
        }
        catch
        {
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static byte[] Protect(byte[] data) =>
        ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);

    [SupportedOSPlatform("windows")]
    private static byte[] Unprotect(byte[] data) =>
        ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
}
