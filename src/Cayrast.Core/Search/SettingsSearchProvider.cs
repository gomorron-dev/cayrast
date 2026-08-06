using System.Runtime.CompilerServices;
using Cayrast.Abstractions.Search;
using Cayrast.Core.Settings;

namespace Cayrast.Core.Search;

/// <summary>
/// Makes settings findable from the search box.
/// </summary>
/// <remarks>
/// <para>
/// Matches labels, descriptions, categories, and — most importantly — the keywords each
/// descriptor declares. Matching only labels would mean a user has to already know what
/// a setting is called, which defeats the purpose: someone looking for the transparency
/// slider is as likely to type "glass" or "acrylic".
/// </para>
/// <para>
/// This provider exists only because settings are declarative data. If the settings
/// screen were hand-built interface, there would be nothing here to query.
/// </para>
/// </remarks>
public sealed class SettingsSearchProvider(ISettingsRegistry registry) : ISearchProvider
{
    /// <inheritdoc />
    public string Id => "cayrast.settings";

    /// <inheritdoc />
    public SearchCategory Category => SearchCategory.Settings;

    /// <inheritdoc />
    public bool CanHandle(SearchQuery query) => !query.IsEmpty;

    /// <inheritdoc />
    public async IAsyncEnumerable<SearchResult> SearchAsync(
        SearchQuery query,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var produced = 0;

        foreach (var descriptor in registry.All)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var labelMatch = FuzzyMatcher.Match(descriptor.Label, query.Text);

            // The label is the strongest signal. Everything else is discounted so that
            // a setting whose name matches outranks one that merely mentions the word
            // somewhere in its description.
            var score = labelMatch.Matched ? labelMatch.Score : 0;

            score = Math.Max(score, BestKeywordScore(descriptor, query.Text) * 0.85);
            score = Math.Max(score, ScoreOf(descriptor.Description, query.Text) * 0.5);
            score = Math.Max(score, ScoreOf(descriptor.Category, query.Text) * 0.6);

            if (score <= 0)
            {
                continue;
            }

            yield return new SearchResult
            {
                Id = $"setting:{descriptor.Id}",
                Title = descriptor.Label,
                Subtitle = descriptor.Description is null
                    ? descriptor.Category
                    : $"{descriptor.Category} — {descriptor.Description}",
                Category = SearchCategory.Settings,
                Icon = IconReference.Glyph("settings"),
                Score = score,
                TitleMatchIndices = labelMatch.Matched ? labelMatch.MatchedIndices : [],
                Actions = [ResultAction.Default("Open setting")],
                Tag = descriptor,
            };

            if (++produced >= query.MaxResults)
            {
                yield break;
            }
        }

        await Task.CompletedTask;
    }

    private static double BestKeywordScore(Abstractions.Settings.SettingDescriptor descriptor, string query)
    {
        var best = 0.0;

        foreach (var keyword in descriptor.Keywords)
        {
            var match = FuzzyMatcher.Match(keyword, query);
            if (match.Matched && match.Score > best)
            {
                best = match.Score;
            }
        }

        return best;
    }

    private static double ScoreOf(string? text, string query)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var match = FuzzyMatcher.Match(text, query);
        return match.Matched ? match.Score : 0;
    }
}
