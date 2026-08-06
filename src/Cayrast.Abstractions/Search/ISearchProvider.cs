namespace Cayrast.Abstractions.Search;

/// <summary>
/// A source of search results. Implemented by the shell's built-in providers and by
/// modules, through the identical contract.
/// </summary>
/// <remarks>
/// <para>
/// <b>Results stream.</b> <see cref="SearchAsync"/> returns
/// <see cref="IAsyncEnumerable{T}"/> rather than a completed collection, and this is
/// the single most important performance decision in the search subsystem. The host
/// fans out to all providers concurrently and renders results as they arrive, so the
/// command provider (in-memory, microseconds) paints immediately while the file
/// provider is still walking the disk. Were this <c>Task&lt;IReadOnlyList&lt;T&gt;&gt;</c>,
/// every query would feel as slow as the slowest provider.
/// </para>
/// <para>
/// <b>Cancellation is not optional.</b> The token is cancelled on the very next
/// keystroke — which, at typing speed, is roughly every 100 ms. A provider that
/// ignores it will pile up abandoned work and starve the thread pool within seconds.
/// Check it inside loops, and pass it to every call that accepts one.
/// </para>
/// </remarks>
public interface ISearchProvider
{
    /// <summary>Stable identifier, unique across the host. Used for diagnostics and settings.</summary>
    string Id { get; }

    /// <summary>Category the results are grouped under.</summary>
    SearchCategory Category { get; }

    /// <summary>
    /// Whether this provider should run for the given query, checked before any async
    /// work is scheduled.
    /// </summary>
    /// <remarks>
    /// The cheap way to stay fast. A provider that only answers prefixed queries (say,
    /// <c>gh </c>) returns <see langword="false"/> here and costs nothing on every other
    /// keystroke. Must be side-effect free and must not block.
    /// </remarks>
    bool CanHandle(SearchQuery query);

    /// <summary>
    /// Produces results for the query, yielding each as soon as it is known.
    /// </summary>
    /// <param name="query">The user's current input and active filters.</param>
    /// <param name="cancellationToken">Cancelled on the next keystroke. Honour it promptly.</param>
    /// <returns>An async stream of results; may be empty.</returns>
    IAsyncEnumerable<SearchResult> SearchAsync(SearchQuery query, CancellationToken cancellationToken);
}
