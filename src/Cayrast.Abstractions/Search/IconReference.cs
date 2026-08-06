namespace Cayrast.Abstractions.Search;

/// <summary>How an icon should be resolved for display.</summary>
public enum IconKind
{
    /// <summary>No icon; the UI renders a category-appropriate placeholder.</summary>
    None = 0,

    /// <summary>A built-in glyph from the shell's icon set, referenced by name.</summary>
    Glyph = 1,

    /// <summary>Extract the associated icon from a file or executable at the given path.</summary>
    ExtractedFromFile = 2,

    /// <summary>A file inside the owning module's <c>assets/</c> directory.</summary>
    ModuleAsset = 3,

    /// <summary>A <c>data:</c> URI supplied inline by the provider.</summary>
    DataUri = 4,
}

/// <summary>
/// A late-bound icon reference.
/// </summary>
/// <remarks>
/// Deliberately a reference and not pixels. Extracting an icon from an executable
/// costs milliseconds and touches the disk; doing that for every result of every
/// keystroke would dominate search time. Providers describe where the icon lives, and
/// the UI resolves it lazily for rows that actually become visible, through a cache
/// keyed on <see cref="Value"/>.
/// </remarks>
/// <param name="Kind">How to interpret <see cref="Value"/>.</param>
/// <param name="Value">Glyph name, file path, asset-relative path, or data URI.</param>
public readonly record struct IconReference(IconKind Kind, string? Value = null)
{
    /// <summary>No icon.</summary>
    public static readonly IconReference None = new(IconKind.None);

    /// <summary>References a built-in glyph by name.</summary>
    public static IconReference Glyph(string name) => new(IconKind.Glyph, name);

    /// <summary>Extracts the icon associated with a file or executable.</summary>
    public static IconReference FromFile(string path) => new(IconKind.ExtractedFromFile, path);

    /// <summary>References an image shipped inside the module package.</summary>
    public static IconReference Asset(string relativePath) => new(IconKind.ModuleAsset, relativePath);
}
