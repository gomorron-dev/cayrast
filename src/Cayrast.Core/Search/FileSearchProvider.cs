using System.Runtime.CompilerServices;
using Cayrast.Abstractions.Search;
using Cayrast.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Cayrast.Core.Search;

/// <summary>
/// Finds files and folders by walking the user's indexed locations.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a live walk rather than a maintained index.</b> Building and maintaining a
/// filesystem index means a background service, a database, change notifications, and a
/// re-index story after every sleep or unclean shutdown — a large amount of machinery
/// that is wrong in exactly the moments users notice. A bounded live walk has no state
/// to go stale, and because results stream, useful matches appear long before the walk
/// finishes.
/// </para>
/// <para>
/// The tradeoff is that it cannot search the whole disk. It searches the places people
/// actually keep things — Desktop, Documents, Downloads, and any folders they add —
/// which covers the overwhelming majority of "where did I put that file".
/// </para>
/// <para>
/// <b>This provider is the reason cancellation matters.</b> It is the slow one. Every
/// loop checks the token, because at typing speed the query it is serving is obsolete
/// roughly every 100 ms.
/// </para>
/// </remarks>
public sealed class FileSearchProvider(ISettingsService settings, ILogger<FileSearchProvider> logger) : ISearchProvider
{
    /// <summary>Shortest query worth walking the disk for.</summary>
    /// <remarks>
    /// A single character matches nearly everything, so the walk would cost real I/O to
    /// produce results the user cannot use. Applications and commands still answer.
    /// </remarks>
    private const int MinimumQueryLength = 3;

    /// <summary>How deep to descend from each indexed root.</summary>
    /// <remarks>
    /// Deep enough for a realistic project or document tree, shallow enough that one
    /// unlucky root — a node_modules tree, a source checkout — cannot dominate the walk.
    /// </remarks>
    private const int MaxDepth = 6;

    /// <summary>Directories never descended into.</summary>
    /// <remarks>
    /// These hold enormous numbers of files that no one searches for by name. Skipping
    /// them is the single largest speed win available to this provider.
    /// </remarks>
    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".git", ".svn", "bin", "obj", ".vs", ".vscode", ".idea",
        "AppData", "$RECYCLE.BIN", "System Volume Information", "__pycache__",
        ".next", ".nuxt", "dist", "target", "Packages", ".gradle", ".venv", "venv",
    };

    /// <inheritdoc />
    public string Id => "cayrast.files";

    /// <inheritdoc />
    public SearchCategory Category => SearchCategory.Files;

    /// <inheritdoc />
    public bool CanHandle(SearchQuery query) => query.Text.Trim().Length >= MinimumQueryLength;

    /// <inheritdoc />
    public async IAsyncEnumerable<SearchResult> SearchAsync(
        SearchQuery query,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var needle = query.Text.Trim();
        var produced = 0;

        foreach (var root in GetSearchRoots())
        {
            if (produced >= query.MaxResults || cancellationToken.IsCancellationRequested)
            {
                yield break;
            }

            foreach (var entry in Walk(root, 0, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Matched on the file name rather than the full path. A path match would
                // score every file in a matching folder equally, burying the one the
                // user actually named.
                var name = Path.GetFileName(entry.Path);
                var match = FuzzyMatcher.Match(name, needle);

                if (!match.Matched)
                {
                    continue;
                }

                yield return Build(entry, name, match);

                if (++produced >= query.MaxResults)
                {
                    yield break;
                }
            }
        }

        await Task.CompletedTask;
    }

    /// <summary>The directories to search, from settings or sensible defaults.</summary>
    private IEnumerable<string> GetSearchRoots()
    {
        var configured = settings.Current.Search.IndexedFolders;

        if (configured.Count > 0)
        {
            return configured.Where(Directory.Exists);
        }

        // Resolved at runtime rather than stored, so a redirected or roamed profile is
        // followed correctly instead of pointing at a path that no longer exists.
        return new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads",
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        }.Where(path => !string.IsNullOrEmpty(path) && Directory.Exists(path));
    }

    /// <summary>An entry discovered during the walk.</summary>
    private readonly record struct Entry(string Path, bool IsDirectory);

    /// <summary>
    /// Walks a directory tree breadth-first, yielding entries as it goes.
    /// </summary>
    /// <remarks>
    /// Breadth-first deliberately: files near the top of a tree are far likelier to be
    /// what someone is looking for than files buried six levels down, and because
    /// results stream, the good ones should arrive first.
    /// </remarks>
    private IEnumerable<Entry> Walk(string root, int depth, CancellationToken cancellationToken)
    {
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((root, depth));

        while (queue.Count > 0)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                yield break;
            }

            var (current, currentDepth) = queue.Dequeue();

            string[] entries;
            try
            {
                entries = Directory.GetFileSystemEntries(current);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
            {
                // Permission denied and vanished directories are completely routine when
                // walking a live filesystem. Skipping quietly is correct; logging each
                // one would flood the log on every keystroke.
                continue;
            }

            foreach (var entry in entries)
            {
                var isDirectory = Directory.Exists(entry);
                var name = Path.GetFileName(entry);

                if (isDirectory)
                {
                    if (currentDepth + 1 <= MaxDepth && !SkippedDirectories.Contains(name) && !IsHidden(entry))
                    {
                        queue.Enqueue((entry, currentDepth + 1));
                    }

                    yield return new Entry(entry, true);
                }
                else if (!IsHidden(entry))
                {
                    yield return new Entry(entry, false);
                }
            }
        }
    }

    private bool IsHidden(string path)
    {
        try
        {
            // Hidden and system files are infrastructure the user did not create and is
            // not looking for.
            var attributes = File.GetAttributes(path);
            return attributes.HasFlag(FileAttributes.Hidden) || attributes.HasFlag(FileAttributes.System);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogTrace(ex, "Could not read attributes for {Path}.", path);
            return true;
        }
    }

    private static SearchResult Build(Entry entry, string name, FuzzyMatch match) => new()
    {
        Id = $"file:{entry.Path}",
        Title = name,
        Subtitle = Path.GetDirectoryName(entry.Path),
        Category = SearchCategory.Files,
        Icon = entry.IsDirectory
            ? IconReference.Glyph("folder")
            : IconReference.FromFile(entry.Path),

        // Discounted against applications and commands. Someone typing "chrome" means
        // the browser, not a file that happens to be called chrome.txt — and a file
        // search can return many more candidates, so it must not crowd the list.
        Score = match.Score * 0.8,
        TitleMatchIndices = match.MatchedIndices,
        Actions =
        [
            ResultAction.Default(entry.IsDirectory ? "Open folder" : "Open"),
            new ResultAction("reveal", "Open containing folder", "Ctrl+Enter"),
            new ResultAction("copy-path", "Copy path", "Ctrl+C"),
        ],
        Tag = new ResultTargets.FileTarget(entry.Path, entry.IsDirectory),
    };
}
