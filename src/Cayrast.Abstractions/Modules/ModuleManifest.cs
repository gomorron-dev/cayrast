using System.Text.Json.Serialization;

namespace Cayrast.Abstractions.Modules;

/// <summary>
/// The deserialised form of a module package's <c>manifest.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// This type is read from untrusted input — a <c>.cayrast</c> file the user
/// downloaded from anywhere. Nothing here may be assumed well-formed; the loader
/// validates every field before the module is allowed to load, and validation
/// failures are surfaced to the user rather than swallowed.
/// </para>
/// <para>
/// Adding an optional property is a compatible change. Adding a required one is
/// not, because it invalidates every published module.
/// </para>
/// </remarks>
public sealed record ModuleManifest
{
    /// <summary>Human-readable name shown in the plugin manager, e.g. "Spotify".</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Reverse-DNS unique identifier, e.g. <c>cayrast.spotify</c>.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>Semantic version of the module itself.</summary>
    [JsonPropertyName("version")]
    public required string Version { get; init; }

    /// <summary>Module author, shown in the consent prompt.</summary>
    [JsonPropertyName("author")]
    public required string Author { get; init; }

    /// <summary>One-line description shown in search results and the plugin manager.</summary>
    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Managed assembly inside <c>backend/</c> that contains the
    /// <see cref="ICayrastModule"/> implementation, e.g. <c>main.dll</c>.
    /// </summary>
    /// <remarks>
    /// Optional: a module may be frontend-only (UI and commands with no native
    /// backend), in which case this is <see langword="null"/> and no assembly is
    /// loaded at all — the cheapest and safest kind of module.
    /// </remarks>
    [JsonPropertyName("entry")]
    public string? Entry { get; init; }

    /// <summary>
    /// Entry HTML inside <c>frontend/</c>, served on the module's own origin.
    /// </summary>
    [JsonPropertyName("ui")]
    public string? Ui { get; init; }

    /// <summary>
    /// Capabilities this module requires, as lowercase strings matching
    /// <see cref="ModulePermission"/> members, e.g. <c>["filesystem", "network"]</c>.
    /// </summary>
    /// <remarks>
    /// Declared here, consented to by the user at install time, and enforced by the
    /// host broker at call time. A module requesting a permission it was not granted
    /// receives a failure, not a silent no-op — silent failure teaches module authors
    /// to ignore the permission system.
    /// </remarks>
    [JsonPropertyName("permissions")]
    public IReadOnlyList<string> Permissions { get; init; } = [];

    /// <summary>
    /// Minimum Cayrast version this module supports, e.g. <c>"0.4.0"</c>.
    /// </summary>
    /// <remarks>
    /// Loading a module built against a newer SDK than the running host produces a
    /// clear "update Cayrast to use this module" message instead of a
    /// <see cref="MissingMethodException"/> at some random later moment.
    /// </remarks>
    [JsonPropertyName("minHostVersion")]
    public string? MinHostVersion { get; init; }

    /// <summary>Relative path to the module icon inside <c>assets/</c>.</summary>
    [JsonPropertyName("icon")]
    public string? Icon { get; init; }

    /// <summary>Project or documentation URL shown in the plugin manager.</summary>
    [JsonPropertyName("homepage")]
    public string? Homepage { get; init; }
}
