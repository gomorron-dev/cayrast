using System.Runtime.CompilerServices;
using Cayrast.Abstractions.Applications;
using Cayrast.Abstractions.Search;

namespace Cayrast.Core.Search;

/// <summary>
/// Surfaces installed applications as search results.
/// </summary>
/// <remarks>
/// Runs entirely against an in-memory index, so it answers in microseconds and is one
/// of the providers whose results paint before anything slower has started. The index
/// itself is maintained by the platform layer; this class only matches and ranks.
/// </remarks>
public sealed class ApplicationSearchProvider(IApplicationIndex index) : ISearchProvider
{
    /// <inheritdoc />
    public string Id => "cayrast.applications";

    /// <inheritdoc />
    public SearchCategory Category => SearchCategory.Applications;

    /// <inheritdoc />
    public bool CanHandle(SearchQuery query) => true;

    /// <inheritdoc />
    public async IAsyncEnumerable<SearchResult> SearchAsync(
        SearchQuery query,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Snapshotted once: the index can be replaced by a background refresh partway
        // through, and iterating a collection that changes underneath would throw.
        var applications = index.Applications;

        // On an empty query the launcher has just opened. Everything is returned
        // unranked and the engine orders it by frecency, which is what makes the first
        // screen show the user's actual tools instead of an alphabetical list.
        if (query.IsEmpty)
        {
            foreach (var application in applications.Take(query.MaxResults))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return Build(application, 0.5, []);
            }

            yield break;
        }

        var produced = 0;

        foreach (var application in applications)
        {
            // Checked every iteration rather than every batch: the token is cancelled
            // on the next keystroke, roughly every 100 ms while typing, and an index of
            // several hundred applications would otherwise keep scoring after the
            // result was already irrelevant.
            cancellationToken.ThrowIfCancellationRequested();

            var match = FuzzyMatcher.Match(application.Name, query.Text);
            if (!match.Matched)
            {
                continue;
            }

            yield return Build(application, match.Score, match.MatchedIndices);

            if (++produced >= query.MaxResults)
            {
                yield break;
            }
        }

        await Task.CompletedTask;
    }

    private static SearchResult Build(InstalledApplication application, double score, IReadOnlyList<int> matchIndices) =>
        new()
        {
            // Derived from the launch identifier so it is stable across queries, which
            // is what lets frecency and deduplication key on it.
            Id = $"app:{application.LaunchId}",
            Title = application.Name,
            Subtitle = application.LaunchViaAppsFolder ? "Application" : application.IconSource,
            Category = SearchCategory.Applications,
            Icon = application.IconSource is null
                ? IconReference.Glyph("application")
                : IconReference.FromFile(application.IconSource),
            Score = score,
            TitleMatchIndices = matchIndices,
            Actions = BuildActions(application),
            Tag = application,
        };

    private static ResultAction[] BuildActions(InstalledApplication application)
    {
        // Without a filesystem path there is nothing to reveal or copy, so those
        // actions are omitted rather than offered and then failing.
        if (application.IconSource is null)
        {
            return [ResultAction.Default("Open")];
        }

        return
        [
            ResultAction.Default("Open"),
            new ResultAction("reveal", "Open containing folder", "Ctrl+Enter"),
            new ResultAction("copy-path", "Copy path", "Ctrl+C"),
        ];
    }
}
