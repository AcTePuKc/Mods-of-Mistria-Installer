namespace Garethp.ModsOfMistriaInstallerLib;

/// <summary>
/// Version values used when validating existing MOMI-format mods.
///
/// AIM has its own public release line (0.1.x), which is deliberately not what a mod's
/// <c>minInstallerVersion</c> is compared against: mod authors write that against upstream MOMI's
/// numbering, which is already past 0.15. Comparing it to AIM's own version would make every
/// current mod look like it needed a newer installer, because 1 sorts below 15.
/// </summary>
public static class InstallerVersion
{
    /// <summary>
    /// The highest upstream MOMI manifest level AIM implements.
    ///
    /// Raise this when AIM gains whatever a newer MOMI release added. It is a claim about what the
    /// installer supports, so it should follow real capability rather than be bumped to silence a
    /// warning - which is why a mod asking for more than this is now a warning the user can
    /// override rather than an error that hides the mod entirely.
    /// </summary>
    public static readonly Version ModCompatibilityVersion = new(0, 15, 10);
}
