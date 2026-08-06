using Cayrast.Core.Search;

namespace Cayrast.Core.Tests.Search;

/// <summary>
/// Tests for <see cref="FuzzyMatcher"/>.
/// </summary>
/// <remarks>
/// Most of these assert *ordering* rather than absolute scores. The exact numbers are
/// tuning and may change; what must not change is that the result a user meant ranks
/// above the one they did not. Those are the assertions worth defending.
/// </remarks>
public sealed class FuzzyMatcherTests
{
    [Theory]
    [InlineData("Visual Studio Code", "vsc")]
    [InlineData("Visual Studio Code", "code")]
    [InlineData("Visual Studio Code", "vscode")]
    [InlineData("Google Chrome", "chrome")]
    [InlineData("Google Chrome", "gc")]
    [InlineData("Notepad", "notepad")]
    [InlineData("my_config_file.json", "config")]
    public void Match_FindsSubsequences(string candidate, string query)
    {
        var result = FuzzyMatcher.Match(candidate, query);

        Assert.True(result.Matched);
        Assert.True(result.Score > 0);
    }

    [Theory]
    [InlineData("Notepad", "xyz")]
    [InlineData("Notepad", "notepadd")]
    [InlineData("Chrome", "chromium")]
    [InlineData("abc", "cba")]
    public void Match_RejectsNonSubsequences(string candidate, string query)
    {
        Assert.False(FuzzyMatcher.Match(candidate, query).Matched);
    }

    [Fact]
    public void Match_EmptyQueryMatchesEverything()
    {
        // The moment the launcher opens, before anything is typed. Ranking then falls
        // to frecency rather than to relevance.
        var result = FuzzyMatcher.Match("Anything", string.Empty);

        Assert.True(result.Matched);
        Assert.Empty(result.MatchedIndices);
    }

    [Fact]
    public void Match_PrefersAMatchThatStartsEarlier()
    {
        // Both candidates match "vsc" on three word initials, so bonuses alone cannot
        // separate them — the deciding factor has to be that one starts at position 0
        // and the other only at position 9. This is the case that forced the leading
        // gap penalty to exist.
        var intended = FuzzyMatcher.Match("Visual Studio Code", "vsc");
        var coincidental = FuzzyMatcher.Match("Advanced Vision Studio Codec", "vsc");

        Assert.True(intended.Matched);
        Assert.True(coincidental.Matched);
        Assert.True(
            intended.Score > coincidental.Score,
            $"Word initials ({intended.Score:F3}) should outrank scattered letters ({coincidental.Score:F3}).");
    }

    [Fact]
    public void Match_PrefersAPrefixOverAMidWordMatch()
    {
        var prefix = FuzzyMatcher.Match("Chrome", "chr");
        var midWord = FuzzyMatcher.Match("Launch Archiver", "chr");

        Assert.True(
            prefix.Score > midWord.Score,
            $"A prefix ({prefix.Score:F3}) should outrank a mid-word match ({midWord.Score:F3}).");
    }

    [Fact]
    public void Match_PrefersConsecutiveCharacters()
    {
        var consecutive = FuzzyMatcher.Match("Terminal", "term");
        var scattered = FuzzyMatcher.Match("The Element Remover", "term");

        Assert.True(
            consecutive.Score > scattered.Score,
            $"Consecutive ({consecutive.Score:F3}) should outrank scattered ({scattered.Score:F3}).");
    }

    [Fact]
    public void Match_RecognisesCamelCaseHumps()
    {
        // Users read camelCase as word boundaries even without separators.
        var camel = FuzzyMatcher.Match("getUserProfile", "gup");
        var scattered = FuzzyMatcher.Match("guardedupdatepolicy", "gup");

        Assert.True(camel.Matched);
        Assert.True(
            camel.Score > scattered.Score,
            $"camelCase humps ({camel.Score:F3}) should outrank a flat run ({scattered.Score:F3}).");
    }

    [Fact]
    public void Match_TreatsSeparatorsAsWordBoundaries()
    {
        var separated = FuzzyMatcher.Match("my_config_file", "mcf");
        var flat = FuzzyMatcher.Match("mysteriouscoffeefilter", "mcf");

        Assert.True(
            separated.Score > flat.Score,
            $"Separated words ({separated.Score:F3}) should outrank a flat run ({flat.Score:F3}).");
    }

    [Fact]
    public void Match_IsCaseInsensitive()
    {
        var lower = FuzzyMatcher.Match("Google Chrome", "chrome");
        var upper = FuzzyMatcher.Match("Google Chrome", "CHROME");

        Assert.True(lower.Matched);
        Assert.True(upper.Matched);
        Assert.Equal(lower.Score, upper.Score);
    }

    [Fact]
    public void Match_ScoreStaysWithinTheNormalisedRange()
    {
        // Providers are merged against one another, so a score outside 0-1 would let
        // one provider's results dominate purely by arithmetic.
        string[] candidates = ["a", "Visual Studio Code", "x".PadRight(200, 'y'), "Chrome"];
        string[] queries = ["a", "vsc", "x", "chrome", "c"];

        foreach (var candidate in candidates)
        {
            foreach (var query in queries)
            {
                var result = FuzzyMatcher.Match(candidate, query);
                Assert.InRange(result.Score, 0.0, 1.0);
            }
        }
    }

    [Fact]
    public void Match_ExactMatchScoresNearTheTop()
    {
        var exact = FuzzyMatcher.Match("Notepad", "Notepad");

        Assert.True(exact.Score > 0.8, $"An exact match scored only {exact.Score:F3}.");
    }

    [Fact]
    public void MatchedIndices_PointAtTheCharactersThatMatched()
    {
        var result = FuzzyMatcher.Match("Visual Studio Code", "vsc");

        Assert.True(result.Matched);
        Assert.Equal(3, result.MatchedIndices.Count);

        // Highlighting the wrong characters is worse than not highlighting at all, so
        // verify the indices actually spell the query.
        var matched = string.Concat(result.MatchedIndices.Select(i => "Visual Studio Code"[i]));
        Assert.Equal("VSC", matched);
    }

    [Fact]
    public void MatchedIndices_AreAscendingAndInBounds()
    {
        const string Candidate = "my_config_file.json";
        var result = FuzzyMatcher.Match(Candidate, "config");

        Assert.True(result.Matched);

        for (var i = 0; i < result.MatchedIndices.Count; i++)
        {
            Assert.InRange(result.MatchedIndices[i], 0, Candidate.Length - 1);

            if (i > 0)
            {
                Assert.True(result.MatchedIndices[i] > result.MatchedIndices[i - 1]);
            }
        }
    }

    [Fact]
    public void Match_HandlesVeryLongCandidatesWithoutFailing()
    {
        // Deeply nested paths are common and must not throw or hang; they fall back to
        // a cheap subsequence test rather than paying the quadratic cost.
        var longPath = string.Join('\\', Enumerable.Repeat("directory", 80)) + "\\target.txt";

        var result = FuzzyMatcher.Match(longPath, "target");

        Assert.True(result.Matched);
        Assert.InRange(result.Score, 0.0, 1.0);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Match_HandlesDegenerateCandidates(string candidate)
    {
        // Must not throw. An index entry with a blank name is malformed data, not a
        // reason to fail the whole query.
        var result = FuzzyMatcher.Match(candidate, "x");
        Assert.False(result.Matched);
    }
}
