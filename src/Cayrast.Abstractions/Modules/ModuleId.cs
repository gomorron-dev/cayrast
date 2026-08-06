using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Cayrast.Abstractions.Modules;

/// <summary>
/// A validated, case-insensitive module identifier such as <c>cayrast.clipboard</c>.
/// </summary>
/// <remarks>
/// Module ids end up in filesystem paths, registry keys, database primary keys, and
/// DNS-style WebView2 origins. A raw <see cref="string"/> would let a malformed or
/// hostile id (<c>../../evil</c>) escape any of those. Parsing once at the boundary
/// and passing this type everywhere afterwards makes that class of bug unreachable.
/// </remarks>
public readonly partial record struct ModuleId
{
    /// <summary>Reverse-DNS style: lowercase segments separated by dots.</summary>
    [GeneratedRegex(@"^[a-z0-9]+(?:[-_][a-z0-9]+)*(?:\.[a-z0-9]+(?:[-_][a-z0-9]+)*)+$")]
    private static partial Regex ValidPattern { get; }

    private ModuleId(string value) => Value = value;

    /// <summary>The normalised (lowercase) identifier.</summary>
    public string Value { get; }

    /// <summary>
    /// A DNS-label-safe form of this id, used to build the module's WebView2 origin.
    /// </summary>
    public string Slug => Value.Replace('.', '-');

    /// <summary>Parses and validates a module id, throwing on malformed input.</summary>
    /// <exception cref="FormatException">The value is not a valid module id.</exception>
    public static ModuleId Parse(string value) =>
        TryParse(value, out var id)
            ? id
            : throw new FormatException(
                $"'{value}' is not a valid module id. Expected reverse-DNS form such as 'author.module'.");

    /// <summary>Attempts to parse a module id. Returns <see langword="false"/> on malformed input.</summary>
    public static bool TryParse([NotNullWhen(true)] string? value, out ModuleId id)
    {
        id = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalised = value.Trim().ToLowerInvariant();
        if (!ValidPattern.IsMatch(normalised))
        {
            return false;
        }

        id = new ModuleId(normalised);
        return true;
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
