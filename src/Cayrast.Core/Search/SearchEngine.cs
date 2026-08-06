using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Cayrast.Abstractions.Search;
using Microsoft.Extensions.Logging;

namespace Cayrast.Core.Search;

/// <summary>
/// Fans a query out to every provider concurrently and merges the results as they arrive.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why streaming.</b> Providers differ in speed by orders of magnitude: the command
/// provider answers from memory in microseconds, while a filesystem provider may walk
/// a disk for hundreds of milliseconds. Waiting for all of them would make every query
/// feel as slow as the slowest, so results are emitted as ranked snapshots the moment
/// anything new arrives and the interface repaints progressively.
/// </para>
/// <para>
/// <b>Isolation.</b> Each provider runs in its own task with its own timeout and its
/// own exception boundary. One provider hanging, throwing, or ignoring cancellation
/// must not be able to prevent the others' results from reaching the user — a plugin
/// platform where a single bad module breaks search is not a plugin platform.
/// </para>
/// </remarks>
public sealed class SearchEngine(IFrecencyStore frecency, ILogger<SearchEngine> logger) : ISearchEngine
{
    /// <summary>
    /// How long a single provider may take before it is abandoned.
    /// </summary>
    /// <remarks>
    /// Generous enough for a real filesystem scan, short enough that a wedged provider
    /// releases its slot rather than lingering for the session. Results produced before
    /// the timeout are kept — a slow provider is degraded, not discarded.
    /// </remarks>
    private static readonly TimeSpan ProviderTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Minimum interval between emitted snapshots.
    /// </summary>
    /// <remarks>
    /// Without coalescing, a provider yielding a thousand results would emit a thousand
    /// snapshots, each triggering a sort and a repaint. This is below the threshold of
    /// perception while collapsing those into a handful of updates.
    /// </remarks>
    private static readonly TimeSpan SnapshotInterval = TimeSpan.FromMilliseconds(16);

    private readonly ConcurrentDictionary<string, ISearchProvider> _providers = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public void RegisterProvider(ISearchProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        _providers[provider.Id] = provider;
        logger.LogInformation("Registered search provider '{Provider}' ({Category}).",
            provider.Id, provider.Category.DisplayName);
    }

    /// <inheritdoc />
    public void UnregisterProvider(string providerId)
    {
        if (_providers.TryRemove(providerId, out _))
        {
            logger.LogInformation("Unregistered search provider '{Provider}'.", providerId);
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<IReadOnlyList<SearchResult>> SearchAsync(
        SearchQuery query,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var applicable = _providers.Values
            .Where(provider => query.EnabledCategories.Contains(provider.Category.Id))
            .Where(provider => SafelyCanHandle(provider, query))
            .ToList();

        if (applicable.Count == 0)
        {
            yield break;
        }

        // Unbounded because a provider must never block on a slow consumer; the
        // per-provider result cap already bounds how much can accumulate.
        var channel = Channel.CreateUnbounded<SearchResult>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        var producers = Task.WhenAll(applicable.Select(provider => RunProviderAsync(provider, query, channel.Writer, cancellationToken)));

        // Completing the channel from a continuation keeps the consumer loop below
        // free of any knowledge of how many producers there were.
        _ = producers.ContinueWith(
            _ => channel.Writer.TryComplete(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        var collected = new List<SearchResult>(query.MaxResults * 2);
        var lastSnapshot = Environment.TickCount64;
        var dirty = false;

        await foreach (var result in channel.Reader.ReadAllAsync(cancellationToken))
        {
            collected.Add(result);
            dirty = true;

            if (Environment.TickCount64 - lastSnapshot < SnapshotInterval.TotalMilliseconds)
            {
                continue;
            }

            lastSnapshot = Environment.TickCount64;
            dirty = false;
            yield return Rank(collected, query.MaxResults);
        }

        // Always emit a final snapshot: without it, results that arrived inside the
        // last coalescing window would never be shown.
        if (dirty || collected.Count > 0)
        {
            yield return Rank(collected, query.MaxResults);
        }
    }

    private bool SafelyCanHandle(ISearchProvider provider, SearchQuery query)
    {
        try
        {
            return provider.CanHandle(query);
        }
        catch (Exception ex)
        {
            // CanHandle is documented as side-effect free and non-throwing. A provider
            // that breaks that contract is excluded rather than allowed to fail the query.
            logger.LogError(ex, "Search provider '{Provider}' threw from CanHandle and was skipped.", provider.Id);
            return false;
        }
    }

    private async Task RunProviderAsync(
        ISearchProvider provider,
        SearchQuery query,
        ChannelWriter<SearchResult> writer,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProviderTimeout);

        var produced = 0;

        try
        {
            await foreach (var result in provider.SearchAsync(query, timeout.Token).WithCancellation(timeout.Token))
            {
                // The cap is enforced here rather than trusted to the provider: the
                // merge stage would discard the tail anyway, and an unbounded provider
                // could otherwise exhaust memory.
                if (produced >= query.MaxResults)
                {
                    break;
                }

                writer.TryWrite(result);
                produced++;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Superseded by the next keystroke. Entirely routine — not worth logging.
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning(
                "Search provider '{Provider}' exceeded its {Timeout:g} budget; keeping the {Count} results it produced.",
                provider.Id, ProviderTimeout, produced);
        }
        catch (Exception ex)
        {
            // A provider may be third-party code. It does not get to break search.
            logger.LogError(ex, "Search provider '{Provider}' failed.", provider.Id);
        }
    }

    /// <summary>Merges, weights, and truncates the results collected so far.</summary>
    /// <remarks>
    /// Providers score only within their own results, because none of them can know
    /// how their output compares to another's. Cross-provider comparison happens here,
    /// where category weight and frecency are applied.
    /// </remarks>
    private SearchResult[] Rank(List<SearchResult> results, int maxResults)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ranked = new List<(SearchResult Result, double Score)>(results.Count);

        foreach (var result in results)
        {
            // The same thing can legitimately be found by more than one provider — an
            // application by both the app index and the file provider. Showing it twice
            // looks like a bug, so the first (higher-priority) sighting wins.
            if (!seen.Add(result.Id))
            {
                continue;
            }

            ranked.Add((result, ComputeFinalScore(result)));
        }

        ranked.Sort((left, right) =>
        {
            var byScore = right.Score.CompareTo(left.Score);
            if (byScore != 0)
            {
                return byScore;
            }

            // Equal scores fall back to category order, so an application outranks an
            // equally-scored file rather than the order depending on which task
            // happened to finish first — which would make results jump around.
            var byCategory = left.Result.Category.SortOrder.CompareTo(right.Result.Category.SortOrder);
            return byCategory != 0
                ? byCategory
                : string.Compare(left.Result.Title, right.Result.Title, StringComparison.OrdinalIgnoreCase);
        });

        var count = Math.Min(ranked.Count, maxResults);
        var output = new SearchResult[count];
        for (var i = 0; i < count; i++)
        {
            output[i] = ranked[i].Result;
        }

        return output;
    }

    private double ComputeFinalScore(SearchResult result)
    {
        // Category weight nudges rather than dominates: a strong text match in a
        // lower-priority category should still be able to beat a weak one in a higher
        // category, or the ranking stops responding to what the user actually typed.
        var categoryWeight = 1.0 - (Math.Clamp(result.Category.SortOrder, 0, 100) / 1000.0);

        // Frecency is additive and capped so a familiar item gets a real lift without
        // being able to outrank a much better textual match on habit alone.
        var frecencyBoost = frecency.GetBoost(result.Id) * 0.25;

        return (result.Score * categoryWeight) + frecencyBoost;
    }
}
