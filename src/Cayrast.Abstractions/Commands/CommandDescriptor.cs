namespace Cayrast.Abstractions.Commands;

/// <summary>
/// Everything the host needs to know about a command without executing it.
/// </summary>
/// <remarks>
/// Descriptors are data, not behaviour, and that is what makes the built-in
/// <c>help</c> command possible: it enumerates the registered descriptors rather
/// than maintaining a hand-written list that drifts out of date the moment a module
/// adds a command. The same data drives search matching and inline usage hints.
/// </remarks>
public sealed record CommandDescriptor
{
    /// <summary>
    /// The word the user types, e.g. <c>calc</c>. Lowercase, no whitespace.
    /// </summary>
    public required string Verb { get; init; }

    /// <summary>Alternative spellings, e.g. <c>["="]</c> for <c>calc</c>.</summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];

    /// <summary>One-line description shown in results and in <c>help</c>.</summary>
    public required string Summary { get; init; }

    /// <summary>Argument shape, e.g. <c>calc &lt;expression&gt;</c>.</summary>
    public string? Usage { get; init; }

    /// <summary>
    /// Concrete examples, e.g. <c>["calc 20*50", "calc 15% of 340"]</c>.
    /// </summary>
    /// <remarks>
    /// Examples teach a command far better than a usage string. <c>help</c> shows them,
    /// and they are indexed for search — so typing "base64" surfaces the command even
    /// when the user does not recall its exact verb.
    /// </remarks>
    public IReadOnlyList<string> Examples { get; init; } = [];

    /// <summary>Module that contributed this command, or <see langword="null"/> for built-ins.</summary>
    public string? OwnerModuleId { get; init; }

    /// <summary>
    /// Whether the command produces a live preview as the user types.
    /// </summary>
    /// <remarks>
    /// True for pure, fast, side-effect-free commands like <c>calc</c> or <c>base64</c>,
    /// where showing the answer inline before Enter is most of the value. Must be false
    /// for anything that acts on the system — a command that previews must be safe to
    /// run on every keystroke.
    /// </remarks>
    public bool SupportsLivePreview { get; init; }
}
