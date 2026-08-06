namespace Cayrast.Abstractions.Search;

/// <summary>
/// A single row in the results list.
/// </summary>
/// <remarks>
/// Immutable by design. Results cross a thread boundary (produced on the pool,
/// rendered on the UI thread) and may be cached and re-ranked, so a mutable result
/// would be a data race waiting to happen.
/// </remarks>
public sealed record SearchResult
{
    /// <summary>
    /// Identifier stable across queries for the same underlying thing.
    /// </summary>
    /// <remarks>
    /// Used to deduplicate — an application found by both the app indexer and the file
    /// provider should appear once — and to key frecency. An id that changes between
    /// queries silently breaks both, so derive it from the target (a full path, a URI)
    /// rather than from the query or a counter.
    /// </remarks>
    public required string Id { get; init; }

    /// <summary>Primary line, e.g. "Discord".</summary>
    public required string Title { get; init; }

    /// <summary>Secondary line, e.g. the file path or a description.</summary>
    public string? Subtitle { get; init; }

    /// <summary>Grouping this result belongs to.</summary>
    public required SearchCategory Category { get; init; }

    /// <summary>How to render this result's icon.</summary>
    public IconReference Icon { get; init; } = IconReference.None;

    /// <summary>
    /// Relevance from 0.0 to 1.0 as judged by the producing provider.
    /// </summary>
    /// <remarks>
    /// Providers score only <em>within</em> their own results. The host applies
    /// category weighting and frecency before merging, because no provider can know how
    /// its results compare to another's.
    /// </remarks>
    public required double Score { get; init; }

    /// <summary>
    /// Character indices in <see cref="Title"/> that matched the query, for highlighting.
    /// </summary>
    /// <remarks>
    /// Supplied by the provider because only it knows how the match was made. Showing
    /// users why a result matched is most of what makes fuzzy search feel trustworthy
    /// rather than arbitrary.
    /// </remarks>
    public IReadOnlyList<int> TitleMatchIndices { get; init; } = [];

    /// <summary>
    /// What can be done with this result. The first entry is the default action, run on Enter.
    /// </summary>
    public required IReadOnlyList<ResultAction> Actions { get; init; }

    /// <summary>Provider-private payload carried back to the action handler.</summary>
    /// <remarks>
    /// Saves re-resolving the target when the user acts. Never rendered, never
    /// serialised across the sandbox boundary — sandboxed providers must encode
    /// everything they need into <see cref="Id"/> instead.
    /// </remarks>
    public object? Tag { get; init; }
}
