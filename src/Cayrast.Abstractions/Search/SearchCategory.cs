namespace Cayrast.Abstractions.Search;

/// <summary>
/// A user-facing grouping of results, such as "Applications" or "Clipboard".
/// </summary>
/// <remarks>
/// Modelled as a record rather than an enum because modules define their own
/// categories. The well-known values below are the ones the shell ships with and
/// the ones the user can toggle in settings; a module is free to introduce others.
/// </remarks>
/// <param name="Id">Stable identifier, used in settings and persisted state.</param>
/// <param name="DisplayName">Localised name shown as a result group header.</param>
/// <param name="SortOrder">
/// Tie-breaker when results score equally. Lower sorts first — this is what makes
/// an exact application match outrank an equally-scored file match.
/// </param>
public sealed record SearchCategory(string Id, string DisplayName, int SortOrder = 100)
{
    /// <summary>Installed Win32 and UWP applications.</summary>
    public static readonly SearchCategory Applications = new("applications", "Applications", 10);

    /// <summary>Built-in, module, and user-defined commands.</summary>
    public static readonly SearchCategory Commands = new("commands", "Commands", 20);

    /// <summary>Files and folders from the indexed locations.</summary>
    public static readonly SearchCategory Files = new("files", "Files", 30);

    /// <summary>Clipboard history entries.</summary>
    public static readonly SearchCategory Clipboard = new("clipboard", "Clipboard", 40);

    /// <summary>Cayrast's own settings, findable by keyword.</summary>
    public static readonly SearchCategory Settings = new("settings", "Settings", 50);

    /// <summary>Utilities contributed by modules.</summary>
    public static readonly SearchCategory Tools = new("tools", "Tools", 60);

    /// <summary>Running processes and open windows.</summary>
    public static readonly SearchCategory System = new("system", "System", 70);

    /// <summary>Every category the shell ships with, in display order.</summary>
    public static IReadOnlyList<SearchCategory> BuiltIn { get; } =
    [
        Applications, Commands, Files, Clipboard, Settings, Tools, System,
    ];
}
