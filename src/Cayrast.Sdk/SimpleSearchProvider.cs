using System.Runtime.CompilerServices;
using Cayrast.Abstractions.Search;

namespace Cayrast.Sdk;

/// <summary>
/// A search provider for modules whose results are a plain list.
/// </summary>
/// <remarks>
/// <para>
/// Most module providers do not need streaming: they hold a handful of items in memory
/// and filter them. This adapts that shape onto <see cref="ISearchProvider"/> and — more
/// importantly — gets the cancellation right, which is the part module authors most
/// often miss.
/// </para>
/// <para>
/// <b>Why cancellation matters.</b> Your token is cancelled on the user's next
/// keystroke, roughly every 100 ms while typing. A provider that ignores it accumulates
/// abandoned work and starves the thread pool within seconds — and because the symptom
/// is the whole launcher feeling slow, it gets blamed on Cayrast rather than on the
/// module. This base class checks the token between every item.
/// </para>
/// <para>
/// If you genuinely need to stream — a network search, or a filesystem walk — implement
/// <see cref="ISearchProvider"/> directly and yield as results arrive.
/// </para>
/// </remarks>
public abstract class SimpleSearchProvider : ISearchProvider
{
    /// <inheritdoc />
    public abstract string Id { get; }

    /// <inheritdoc />
    public abstract SearchCategory Category { get; }

    /// <inheritdoc />
    /// <remarks>
    /// The default runs for any non-empty query. Override to return
    /// <see langword="false"/> quickly when the query cannot possibly concern you —
    /// a provider that only answers prefixed queries costs nothing on every other
    /// keystroke, and that is the cheapest way to stay fast.
    /// </remarks>
    public virtual bool CanHandle(SearchQuery query) => !query.IsEmpty;

    /// <summary>Returns the results for a query. Called off the UI thread.</summary>
    protected abstract IEnumerable<SearchResult> GetResults(SearchQuery query);

    /// <inheritdoc />
    public async IAsyncEnumerable<SearchResult> SearchAsync(
        SearchQuery query,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var result in GetResults(query))
        {
            // Checked between every item rather than at the start, so a provider with a
            // long list stops promptly when the user types another character.
            cancellationToken.ThrowIfCancellationRequested();
            yield return result;
        }

        await Task.CompletedTask;
    }
}
