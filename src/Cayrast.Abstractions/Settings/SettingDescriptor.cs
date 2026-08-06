namespace Cayrast.Abstractions.Settings;

/// <summary>The control type used to edit a setting.</summary>
public enum SettingKind
{
    /// <summary>On/off. Rendered as a switch.</summary>
    Boolean = 0,

    /// <summary>Free text.</summary>
    Text = 1,

    /// <summary>Whole number, constrained by the descriptor's bounds.</summary>
    Integer = 2,

    /// <summary>Continuous value between bounds. Rendered as a slider.</summary>
    Slider = 3,

    /// <summary>One of a fixed set of options. Rendered as a dropdown.</summary>
    Choice = 4,

    /// <summary>A colour, rendered with a picker and hex entry.</summary>
    Color = 5,

    /// <summary>A key combination, captured by pressing it.</summary>
    Hotkey = 6,

    /// <summary>A filesystem path, rendered with a browse button.</summary>
    Path = 7,
}

/// <summary>
/// Declares one setting: what it is, how to edit it, and how to find it.
/// </summary>
/// <remarks>
/// <para>
/// The specification requires settings to be searchable. That is only tractable if
/// settings are <em>data</em> rather than hand-built UI, so every setting — core and
/// module alike — registers a descriptor, and one registry then drives three things
/// at once: the settings screen is generated from descriptors, settings search is a
/// query over the same descriptors, and modules get settings pages that look and
/// behave identically to the built-in ones for free.
/// </para>
/// <para>
/// The alternative — hand-writing each settings page and separately maintaining a
/// search index — guarantees the two drift apart. Across fourteen modules that drift
/// is not hypothetical.
/// </para>
/// </remarks>
public sealed record SettingDescriptor
{
    /// <summary>
    /// Stable dotted identifier, e.g. <c>appearance.blurStrength</c>. Used as the
    /// storage key, so renaming one orphans the stored value.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>Settings category this appears under, e.g. <c>Appearance</c>.</summary>
    public required string Category { get; init; }

    /// <summary>Label shown beside the control.</summary>
    public required string Label { get; init; }

    /// <summary>Explanatory text shown beneath the label.</summary>
    public string? Description { get; init; }

    /// <summary>Control type.</summary>
    public required SettingKind Kind { get; init; }

    /// <summary>Value applied when the setting has never been set, and restored by "reset".</summary>
    public required object? DefaultValue { get; init; }

    /// <summary>
    /// Extra terms that should match this setting in search.
    /// </summary>
    /// <remarks>
    /// This is what lets someone find the transparency slider by typing "glass",
    /// "acrylic", or "see-through". Users search for the concept they have in mind,
    /// not the label a developer happened to choose, so populating this generously is
    /// the difference between settings search working and merely existing.
    /// </remarks>
    public IReadOnlyList<string> Keywords { get; init; } = [];

    /// <summary>Options for <see cref="SettingKind.Choice"/>, as (value, label) pairs.</summary>
    public IReadOnlyList<(string Value, string Label)> Choices { get; init; } = [];

    /// <summary>Lower bound for <see cref="SettingKind.Integer"/> and <see cref="SettingKind.Slider"/>.</summary>
    public double? Minimum { get; init; }

    /// <summary>Upper bound for <see cref="SettingKind.Integer"/> and <see cref="SettingKind.Slider"/>.</summary>
    public double? Maximum { get; init; }

    /// <summary>Module that registered this setting, or <see langword="null"/> for core settings.</summary>
    public string? OwnerModuleId { get; init; }

    /// <summary>
    /// Whether changing this requires a restart to take effect.
    /// </summary>
    /// <remarks>
    /// Should be rare. A setting that needs a restart is a setting the user will not
    /// experiment with, and most of Cayrast's appearance settings are only worth having
    /// if their effect is immediate.
    /// </remarks>
    public bool RequiresRestart { get; init; }
}
