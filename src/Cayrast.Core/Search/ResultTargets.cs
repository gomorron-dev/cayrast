namespace Cayrast.Core.Search;

/// <summary>
/// Typed payloads carried on <see cref="Abstractions.Search.SearchResult.Tag"/>.
/// </summary>
/// <remarks>
/// <para>
/// These exist because <c>Tag</c> is <see cref="object"/>, and activation dispatches on
/// its runtime type. Using bare primitives there is a trap: the command provider
/// originally tagged results with a <see cref="string"/> verb, and when the file
/// provider was added tagging results with a <see cref="string"/> path, activating a
/// file would have tried to run it as a command. Nothing in the type system objected,
/// and the failure would only have appeared at run time as a baffling "not a known
/// command" message naming a file path.
/// </para>
/// <para>
/// A distinct type per target category makes that class of collision impossible, and
/// makes the switch in the activation handler exhaustive by construction.
/// </para>
/// </remarks>
public static class ResultTargets
{
    /// <summary>A file or folder on disk.</summary>
    /// <param name="Path">Full path to the entry.</param>
    /// <param name="IsDirectory">Whether it is a directory.</param>
    public sealed record FileTarget(string Path, bool IsDirectory);

    /// <summary>A command to run.</summary>
    /// <param name="Verb">The command's verb, as registered.</param>
    public sealed record CommandTarget(string Verb);
}
