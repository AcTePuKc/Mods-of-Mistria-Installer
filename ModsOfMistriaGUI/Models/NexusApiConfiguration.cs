namespace Garethp.ModsOfMistriaGUI.Models;

/// <summary>
/// Placeholder for the future Nexus OAuth/API integration.
/// It intentionally contains no credentials and performs no network work.
/// </summary>
public sealed record NexusApiConfiguration
{
    public bool Enabled { get; init; }
    public string ApplicationSlug { get; init; } = "";

    public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(ApplicationSlug);

    public static NexusApiConfiguration Disabled { get; } = new();
}
