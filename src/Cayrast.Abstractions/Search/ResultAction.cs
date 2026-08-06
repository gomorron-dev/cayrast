namespace Cayrast.Abstractions.Search;

/// <summary>
/// Something the user can do with a <see cref="SearchResult"/> — open it, reveal it
/// in Explorer, copy its path, run it as administrator.
/// </summary>
/// <param name="Id">Provider-scoped identifier, passed back when the action is invoked.</param>
/// <param name="Title">Verb phrase shown in the action menu, e.g. "Open containing folder".</param>
/// <param name="Shortcut">
/// Optional accelerator shown beside the title, e.g. <c>Ctrl+Enter</c>. Display only —
/// the shell owns the actual key binding so users can rebind it.
/// </param>
/// <param name="IsDestructive">
/// Marks actions that delete or overwrite. The UI styles these distinctly and requires
/// a deliberate confirmation, so that muscle memory alone cannot destroy something.
/// </param>
public sealed record ResultAction(
    string Id,
    string Title,
    string? Shortcut = null,
    bool IsDestructive = false)
{
    /// <summary>Conventional id for the default action bound to Enter.</summary>
    public const string DefaultActionId = "default";

    /// <summary>Creates the primary action for a result.</summary>
    public static ResultAction Default(string title) => new(DefaultActionId, title, "Enter");
}
