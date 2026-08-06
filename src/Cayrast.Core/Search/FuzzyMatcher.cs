using System.Buffers;

namespace Cayrast.Core.Search;

/// <summary>The outcome of scoring one candidate against a query.</summary>
/// <param name="Matched">Whether the query is a subsequence of the candidate.</param>
/// <param name="Score">Relevance from 0.0 to 1.0. Meaningless when not matched.</param>
/// <param name="MatchedIndices">
/// Positions in the candidate that the query matched, ascending. Used to highlight.
/// </param>
public readonly record struct FuzzyMatch(bool Matched, double Score, IReadOnlyList<int> MatchedIndices)
{
    /// <summary>A non-match.</summary>
    public static readonly FuzzyMatch None = new(false, 0, []);
}

/// <summary>
/// Subsequence matching and scoring, in the style of fzf.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not a simpler matcher.</b> A naive "contains all characters in order" test
/// is a handful of lines, but it cannot distinguish <c>Visual Studio Code</c> from
/// <c>Advanced Vision Studio Codec</c> for the query <c>vsc</c> — and getting that
/// ordering right is most of what makes a launcher feel like it read your mind rather
/// than merely filtered a list. The dynamic-programming approach below scores *where*
/// a match happened, not just whether it did.
/// </para>
/// <para>
/// <b>The scoring model.</b> Characters that begin a word score far higher than
/// characters in the middle of one, and consecutive matches compound. So for
/// <c>vsc</c>, matching the initials of three words beats matching three scattered
/// letters, which is the intuition users actually have.
/// </para>
/// <para>
/// <b>Cost.</b> O(query × candidate) time and memory, with buffers rented from
/// <see cref="ArrayPool{T}"/> rather than allocated. This runs against every indexed
/// item on every keystroke, so a per-call allocation here would put the garbage
/// collector directly in the typing path.
/// </para>
/// </remarks>
public static class FuzzyMatcher
{
    // Weights follow fzf's, which are well-tuned by long practical use. The absolute
    // values do not matter; only their ratios do, since the result is normalised.

    /// <summary>Awarded for each matched character.</summary>
    private const int ScoreMatch = 16;

    /// <summary>Charged once when a run of matches breaks.</summary>
    private const int ScoreGapStart = -3;

    /// <summary>Charged for each further unmatched character in a gap.</summary>
    private const int ScoreGapExtension = -1;

    /// <summary>Awarded when a match begins a word, e.g. after a space, slash, or underscore.</summary>
    private const int BonusBoundary = ScoreMatch / 2;

    /// <summary>Awarded when a match lands on a camelCase hump or a digit run.</summary>
    private const int BonusCamel = BonusBoundary + ScoreGapExtension;

    /// <summary>Awarded when a match immediately follows another.</summary>
    private const int BonusConsecutive = -(ScoreGapStart + ScoreGapExtension);

    /// <summary>
    /// Multiplies the bonus on the query's first character.
    /// </summary>
    /// <remarks>
    /// Where a match *starts* is the strongest signal of intent. Someone typing
    /// <c>chr</c> means Chrome, not "Launch Archiver".
    /// </remarks>
    private const int BonusFirstCharMultiplier = 2;

    /// <summary>
    /// Floor on the penalty for a match that starts late in the candidate.
    /// </summary>
    /// <remarks>
    /// Earlier matches should win, but the penalty must not grow without bound. File
    /// paths are long and their meaningful part is the tail, so an uncapped penalty
    /// would drive every deep path match to zero and make files unfindable — trading
    /// one ranking problem for a much worse one.
    /// </remarks>
    private const int MaxLeadingGapPenalty = -20;

    /// <summary>Longest candidate scored with the full algorithm.</summary>
    /// <remarks>
    /// Beyond this the quadratic cost stops being worth it, and the candidate is
    /// almost certainly a long path whose meaningful part is its tail. Such candidates
    /// fall back to a cheap subsequence test rather than being dropped.
    /// </remarks>
    private const int MaxScoredLength = 512;

    /// <summary>Scores a candidate against a query.</summary>
    /// <param name="candidate">Text being searched, e.g. an application name.</param>
    /// <param name="query">What the user typed.</param>
    /// <returns>A match with score and highlight positions, or <see cref="FuzzyMatch.None"/>.</returns>
    public static FuzzyMatch Match(string candidate, string query)
    {
        if (string.IsNullOrEmpty(query))
        {
            // An empty query matches everything equally; ranking is then left to
            // frecency, which is what makes the initial list useful rather than random.
            return new FuzzyMatch(true, 0, []);
        }

        if (string.IsNullOrEmpty(candidate) || query.Length > candidate.Length)
        {
            return FuzzyMatch.None;
        }

        // Cheap rejection first. Most candidates fail, and failing them without
        // touching the pool or the matrix is what keeps a full-index scan viable.
        if (!IsSubsequence(candidate, query))
        {
            return FuzzyMatch.None;
        }

        if (candidate.Length > MaxScoredLength)
        {
            // Matched, but not worth scoring precisely. A low fixed score keeps it
            // below anything properly ranked while still letting it be found.
            return new FuzzyMatch(true, 0.1, []);
        }

        return Score(candidate, query);
    }

    /// <summary>Fast case-insensitive subsequence test.</summary>
    private static bool IsSubsequence(string candidate, string query)
    {
        var queryIndex = 0;

        for (var i = 0; i < candidate.Length && queryIndex < query.Length; i++)
        {
            if (char.ToLowerInvariant(candidate[i]) == char.ToLowerInvariant(query[queryIndex]))
            {
                queryIndex++;
            }
        }

        return queryIndex == query.Length;
    }

    private static FuzzyMatch Score(string candidate, string query)
    {
        var n = candidate.Length;
        var m = query.Length;

        // One rented block holds the score matrix, the consecutive-run matrix, and the
        // per-position bonuses. Renting once and slicing beats three separate rentals.
        var scores = ArrayPool<int>.Shared.Rent(n * m);
        var consecutive = ArrayPool<int>.Shared.Rent(n * m);
        var bonuses = ArrayPool<int>.Shared.Rent(n);

        try
        {
            ComputeBonuses(candidate, bonuses);

            var best = int.MinValue;
            var bestPosition = -1;

            for (var i = 0; i < m; i++)
            {
                var queryChar = char.ToLowerInvariant(query[i]);
                var rowOffset = i * n;
                var previousRowOffset = rowOffset - n;

                for (var j = 0; j < n; j++)
                {
                    // Option A: skip candidate character j and carry the score from
                    // the cell immediately to the left, paying a gap cost.
                    //
                    // This must read the adjacent cell, not a running maximum over the
                    // row. A running maximum charges for a gap only once no matter how
                    // wide it is, which makes a scattered match score identically to a
                    // consecutive one — precisely the distinction this matcher exists
                    // to draw.
                    var gapScore = int.MinValue / 2;

                    if (j > 0)
                    {
                        var leftCell = scores[rowOffset + j - 1];
                        if (leftCell > int.MinValue / 2)
                        {
                            // Breaking a run costs more than widening an existing gap.
                            var brokeARun = consecutive[rowOffset + j - 1] > 0;
                            gapScore = leftCell + (brokeARun ? ScoreGapStart : ScoreGapExtension);
                        }
                    }

                    // Option B: match query character i at candidate position j.
                    var matchScore = int.MinValue / 2;
                    var runLength = 0;

                    if (char.ToLowerInvariant(candidate[j]) == queryChar)
                    {
                        int diagonal;

                        if (i == 0)
                        {
                            // Penalise how far into the candidate the match begins.
                            // Without this, "Visual Studio Code" and "Advanced Vision
                            // Studio Codec" score identically for "vsc" — both are
                            // three word initials — and the user always means the one
                            // that starts at the beginning.
                            diagonal = j == 0
                                ? 0
                                : Math.Max(MaxLeadingGapPenalty, ScoreGapStart + ((j - 1) * ScoreGapExtension));
                        }
                        else if (j == 0)
                        {
                            // The previous query character has nowhere to sit.
                            diagonal = int.MinValue / 2;
                        }
                        else
                        {
                            diagonal = scores[previousRowOffset + j - 1];
                            runLength = consecutive[previousRowOffset + j - 1];
                        }

                        if (diagonal > int.MinValue / 2)
                        {
                            var bonus = bonuses[j];

                            if (i == 0)
                            {
                                // Where a match *starts* is the strongest signal of
                                // intent, so the opening character's bonus is doubled.
                                bonus *= BonusFirstCharMultiplier;
                            }
                            else if (runLength > 0)
                            {
                                // Inside a run, take whichever is stronger: this
                                // position's own bonus, the consecutive reward, or the
                                // bonus of the position the run started from.
                                bonus = Math.Max(bonus, Math.Max(BonusConsecutive, bonuses[j - runLength]));
                            }

                            matchScore = diagonal + ScoreMatch + bonus;
                        }
                    }

                    // Ties go to the match so that backtracking has a path to follow.
                    if (matchScore >= gapScore && matchScore > int.MinValue / 2)
                    {
                        scores[rowOffset + j] = matchScore;
                        consecutive[rowOffset + j] = runLength + 1;
                    }
                    else
                    {
                        scores[rowOffset + j] = gapScore;
                        consecutive[rowOffset + j] = 0;
                    }

                    if (i == m - 1 && scores[rowOffset + j] > best)
                    {
                        best = scores[rowOffset + j];
                        bestPosition = j;
                    }
                }
            }

            if (bestPosition < 0)
            {
                return FuzzyMatch.None;
            }

            var indices = Backtrack(scores, consecutive, n, m, bestPosition);
            return new FuzzyMatch(true, Normalise(best, m), indices);
        }
        finally
        {
            ArrayPool<int>.Shared.Return(scores);
            ArrayPool<int>.Shared.Return(consecutive);
            ArrayPool<int>.Shared.Return(bonuses);
        }
    }

    /// <summary>
    /// Precomputes the positional bonus for every character in the candidate.
    /// </summary>
    /// <remarks>
    /// Depends only on the candidate, not the query, so it is computed once per
    /// candidate rather than once per query character.
    /// </remarks>
    private static void ComputeBonuses(string candidate, int[] bonuses)
    {
        for (var i = 0; i < candidate.Length; i++)
        {
            var current = candidate[i];

            if (i == 0)
            {
                // The very first character is always a boundary. The first-character
                // multiplier is NOT applied here — the scoring loop applies it to
                // whichever position the query's first character lands on, which is
                // not necessarily position zero.
                bonuses[i] = BonusBoundary;
                continue;
            }

            var previous = candidate[i - 1];

            bonuses[i] = (char.IsLetterOrDigit(previous), char.IsLetterOrDigit(current)) switch
            {
                // A letter or digit after a separator starts a word: the "S" in
                // "Visual Studio", or the "c" in "my_config".
                (false, true) => BonusBoundary,

                // An upper-case letter after a lower-case one is a camelCase hump,
                // which users treat as a word start even without a separator.
                (true, true) when char.IsLower(previous) && char.IsUpper(current) => BonusCamel,

                // A digit beginning a run reads as its own token, as in "Photoshop2024".
                (true, true) when !char.IsDigit(previous) && char.IsDigit(current) => BonusCamel,

                _ => 0,
            };
        }
    }

    /// <summary>Walks the matrix backwards to recover which characters matched.</summary>
    /// <remarks>
    /// Only the winning path is needed, so this walks one cell per query character
    /// rather than re-searching. Highlighting is what shows the user *why* a result
    /// matched, which is most of what makes fuzzy search feel trustworthy.
    /// </remarks>
    private static int[] Backtrack(int[] scores, int[] consecutive, int n, int m, int endPosition)
    {
        var indices = new int[m];
        var i = m - 1;
        var j = endPosition;

        while (i >= 0 && j >= 0)
        {
            if (consecutive[(i * n) + j] > 0)
            {
                indices[i] = j;
                i--;
                j--;
                continue;
            }

            // This cell was reached by skipping a candidate character.
            j--;
        }

        // A truncated walk would leave zeroes at the front, which would highlight the
        // wrong characters. Returning what was recovered is safer than guessing.
        return i < 0 ? indices : indices[(i + 1)..];
    }

    /// <summary>
    /// Maps a raw score onto 0.0-1.0.
    /// </summary>
    /// <remarks>
    /// Providers must return comparable scores because the host merges results from
    /// several of them, and raw scores grow with query length — so a long query would
    /// otherwise outrank a short exact one purely by arithmetic.
    /// </remarks>
    private static double Normalise(int rawScore, int queryLength)
    {
        if (rawScore <= 0)
        {
            return 0;
        }

        // The ceiling is every character matching consecutively on a word boundary,
        // with the first character's multiplier applied.
        var maximum = (queryLength * (ScoreMatch + BonusBoundary))
                      + (BonusBoundary * (BonusFirstCharMultiplier - 1));

        return Math.Clamp((double)rawScore / maximum, 0.0, 1.0);
    }
}
