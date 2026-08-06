using Cayrast.Abstractions.Search;

namespace Cayrast.Core.Search;

/// <summary>Dispatches a query across every registered provider and ranks the results.</summary>
public interface ISearchEngine
{
    /// <summary>Adds a provider. Safe to call while the engine is idle.</summary>
    void RegisterProvider(ISearchProvider provider);

    /// <summary>Removes a provider, e.g. when its module is disabled.</summary>
    void UnregisterProvider(string providerId);

    /// <summary>
    /// Runs a query, yielding a ranked snapshot each time new results arrive.
    /// </summary>
    /// <param name="query">The user's current input and active filters.</param>
    /// <param name="cancellationToken">Cancel on the next keystroke.</param>
    /// <returns>
    /// A stream of increasingly complete ranked lists. Callers should render each
    /// snapshot as it arrives and stop when the stream completes.
    /// </returns>
    /// <remarks>
    /// Snapshots rather than individual results, because the interface has to re-rank
    /// as new providers report in: a result that arrives late may belong at the top.
    /// Emitting whole lists keeps that decision here instead of duplicating merge
    /// logic in the frontend.
    /// </remarks>
    IAsyncEnumerable<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken cancellationToken);
}

/// <summary>Records and scores how often and how recently a result is chosen.</summary>
/// <remarks>
/// "Frecency" combines frequency and recency. Frequency alone entrenches whatever the
/// user opened most last year; recency alone forgets their daily tools after one
/// unusual afternoon. Together they make the empty-query list — the first thing shown
/// on every launch — genuinely useful rather than arbitrary.
/// </remarks>
public interface IFrecencyStore
{
    /// <summary>Returns a 0.0-1.0 boost for a result, where 0 means never used.</summary>
    double GetBoost(string resultId);

    /// <summary>Records that the user chose a result.</summary>
    void RecordUse(string resultId);

    /// <summary>Loads persisted usage data.</summary>
    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Persists usage data.</summary>
    Task SaveAsync(CancellationToken cancellationToken = default);
}
