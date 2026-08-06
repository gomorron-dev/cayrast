namespace Cayrast.Abstractions.Search;

/// <summary>
/// One dispatch of the user's current input to every enabled provider.
/// </summary>
/// <param name="Text">
/// The raw text as typed, already trimmed. Providers should treat this as untrusted
/// display input, never as a path or shell fragment without validation.
/// </param>
/// <param name="EnabledCategories">
/// Categories the user has switched on. A provider whose category is absent is not
/// invoked at all, so this is a filter the provider does not need to re-check.
/// </param>
/// <param name="MaxResults">
/// Soft cap per provider. Producing more than this wastes work, because the merge
/// stage will discard the tail anyway.
/// </param>
public sealed record SearchQuery(
    string Text,
    IReadOnlySet<string> EnabledCategories,
    int MaxResults = 25)
{
    /// <summary>True when the user has typed nothing — the moment the launcher opens.</summary>
    /// <remarks>
    /// Providers should answer an empty query cheaply or not at all. This is the most
    /// latency-visible instant in the whole application: the user pressed Alt+Space and
    /// is looking at the window right now. Only frecency-ranked recents belong here.
    /// </remarks>
    public bool IsEmpty => string.IsNullOrWhiteSpace(Text);
}
