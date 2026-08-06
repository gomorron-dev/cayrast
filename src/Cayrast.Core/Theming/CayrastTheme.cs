using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Cayrast.Core.Theming;

/// <summary>An installable theme.</summary>
/// <remarks>
/// A theme is a set of CSS custom-property overrides applied at runtime. Because every
/// visual value in the stylesheet reads from a custom property, a theme needs no
/// rebuild, no restart, and no code — which is what makes theming approachable to
/// people who are not developers.
/// </remarks>
public sealed record CayrastTheme
{
    /// <summary>Display name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Stable identifier, reverse-DNS style.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>Theme version.</summary>
    [JsonPropertyName("version")]
    public string Version { get; init; } = "1.0.0";

    /// <summary>Who made it.</summary>
    [JsonPropertyName("author")]
    public string Author { get; init; } = string.Empty;

    /// <summary>
    /// Which built-in palette to start from, <c>light</c> or <c>dark</c>.
    /// </summary>
    /// <remarks>
    /// Themes override a subset of tokens; the base supplies the rest. Without it a
    /// theme author would have to redefine every token to avoid unreadable
    /// combinations, and most would get one wrong.
    /// </remarks>
    [JsonPropertyName("base")]
    public string Base { get; init; } = "dark";

    /// <summary>CSS custom properties to override, e.g. <c>--cy-accent</c>.</summary>
    [JsonPropertyName("variables")]
    public IReadOnlyDictionary<string, string> Variables { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <summary>Validates themes before their values reach the stylesheet.</summary>
/// <remarks>
/// <para>
/// <b>Theme values become CSS.</b> A theme file is downloaded from anywhere and its
/// values are injected as custom properties, so an unvalidated value is a CSS injection
/// vector: a value containing <c>;</c> or <c>}</c> can close the declaration and start
/// writing arbitrary rules, and <c>url(...)</c> can make the interface fetch a remote
/// resource, which would leak the fact that a user is running Cayrast to a third party.
/// </para>
/// <para>
/// Validation is an allow-list of shapes rather than a deny-list of characters. A
/// deny-list invites exactly the escaping games this is trying to avoid.
/// </para>
/// </remarks>
public static partial class ThemeValidator
{
    /// <summary>Custom property names Cayrast recognises: <c>--cy-</c> followed by kebab-case.</summary>
    [GeneratedRegex(@"^--cy-[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex ValidVariableName { get; }

    /// <summary>
    /// Values that are safe to inject.
    /// </summary>
    /// <remarks>
    /// Covers what a theme legitimately needs: hex colours, rgb/rgba/hsl/hsla, colour
    /// keywords, numbers with CSS units, unitless numbers, simple durations, font
    /// stacks, and cubic-bezier easings. Anything containing a semicolon, a brace, a
    /// url(), or an @-rule fails to match and is dropped.
    /// </remarks>
    [GeneratedRegex(
        @"^(?:#[0-9a-fA-F]{3,8}"
        + @"|(?:rgb|rgba|hsl|hsla|color-mix|cubic-bezier|calc|var)\([^;{}()]*(?:\([^;{}()]*\))?[^;{}()]*\)"
        + @"|-?[0-9]*\.?[0-9]+(?:px|em|rem|%|s|ms|deg|vh|vw|fr)?"
        + @"|[a-zA-Z][a-zA-Z0-9 _'-]*(?:,\s*[a-zA-Z'][a-zA-Z0-9 _'-]*)*"
        + @")$")]
    private static partial Regex ValidVariableValue { get; }

    /// <summary>
    /// Returns only the variables that are safe to apply.
    /// </summary>
    /// <param name="theme">The theme to filter.</param>
    /// <param name="rejected">Names that failed validation, for reporting to the author.</param>
    /// <remarks>
    /// Invalid entries are dropped rather than failing the whole theme. One bad value
    /// should cost the author that one token, not make their theme refuse to load with
    /// no indication of which line was wrong.
    /// </remarks>
    public static IReadOnlyDictionary<string, string> Sanitise(CayrastTheme theme, out IReadOnlyList<string> rejected)
    {
        ArgumentNullException.ThrowIfNull(theme);

        var safe = new Dictionary<string, string>(StringComparer.Ordinal);
        var bad = new List<string>();

        foreach (var (name, value) in theme.Variables)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var trimmedName = name.Trim();
            var trimmedValue = value.Trim();

            if (!ValidVariableName.IsMatch(trimmedName) || !ValidVariableValue.IsMatch(trimmedValue))
            {
                bad.Add(trimmedName);
                continue;
            }

            safe[trimmedName] = trimmedValue;
        }

        rejected = bad;
        return safe;
    }

    /// <summary>Normalises the base palette to <c>light</c> or <c>dark</c>.</summary>
    public static string NormaliseBase(string? value) =>
        string.Equals(value?.Trim(), "light", StringComparison.OrdinalIgnoreCase) ? "light" : "dark";
}
