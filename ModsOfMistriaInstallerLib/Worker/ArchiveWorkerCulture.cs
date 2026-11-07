using System.Globalization;
using Garethp.ModsOfMistriaInstallerLib.Lang;

namespace Garethp.ModsOfMistriaInstallerLib.Worker;

/// <summary>
/// Applies the GUI's chosen language in the short-lived archive worker.
/// A worker is a separate process, so its culture does not inherit the
/// in-memory resource selection from the UI process.
/// </summary>
public static class ArchiveWorkerCulture
{
    public static void Apply(string? cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName)) return;

        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            if (!string.Equals(culture.Name, cultureName, StringComparison.OrdinalIgnoreCase))
                return;
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            Resources.Culture = culture;
        }
        catch (CultureNotFoundException)
        {
            // Requests from an older or manually edited client remain usable;
            // the worker simply uses its normal system culture.
        }
    }
}
