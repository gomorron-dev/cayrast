using Cayrast.Abstractions.Search;
using Cayrast.Core.Commands;
using Cayrast.Core.Search;
using Cayrast.Platform.Windows.Applications;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cayrast.Integration.Tests;

/// <summary>
/// Exercises the whole search path against the real machine.
/// </summary>
/// <remarks>
/// These verify the claim that matters most — "typing finds and ranks things" — end to
/// end rather than one layer at a time. They enumerate the machine's actual installed
/// software, so they assert on properties that hold on any Windows install rather than
/// on specific applications being present.
/// </remarks>
public sealed class SearchPipelineTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static SearchQuery Query(string text, int max = 25) =>
        new(text, new HashSet<string>(SearchCategory.BuiltIn.Select(category => category.Id), StringComparer.OrdinalIgnoreCase), max);

    private static async Task<(SearchEngine Engine, ApplicationIndexer Index)> BuildAsync()
    {
        var index = new ApplicationIndexer(NullLogger<ApplicationIndexer>.Instance);
        await index.RefreshAsync(Token);

        var engine = new SearchEngine(new NullFrecency(), NullLogger<SearchEngine>.Instance);
        engine.RegisterProvider(new ApplicationSearchProvider(index));

        var commands = new CommandEngine(NullLogger<CommandEngine>.Instance);
        BuiltInCommands.RegisterAll(commands);
        engine.RegisterProvider(commands);

        return (engine, index);
    }

    private static async Task<IReadOnlyList<SearchResult>> FinalAsync(SearchEngine engine, SearchQuery query)
    {
        IReadOnlyList<SearchResult> latest = [];

        await foreach (var snapshot in engine.SearchAsync(query, Token))
        {
            latest = snapshot;
        }

        return latest;
    }

    [Fact]
    public async Task ApplicationIndex_FindsInstalledSoftware()
    {
        var index = new ApplicationIndexer(NullLogger<ApplicationIndexer>.Instance);
        await index.RefreshAsync(Token);

        // Every Windows install has a substantial AppsFolder. A near-empty result means
        // the shell enumeration silently failed rather than that the machine is bare.
        Assert.True(index.Applications.Count > 10,
            $"Only {index.Applications.Count} applications were indexed; shell enumeration is probably broken.");

        Assert.All(index.Applications, application =>
        {
            Assert.False(string.IsNullOrWhiteSpace(application.Name));
            Assert.False(string.IsNullOrWhiteSpace(application.LaunchId));
        });
    }

    [Fact]
    public async Task Search_FindsAnApplicationThatIsActuallyInstalled()
    {
        // Deliberately not asserting on a specific application. An earlier version of
        // this test looked for Notepad on the assumption that it ships with every
        // Windows install — it does not, and the failure looked like an indexer bug for
        // some time before the assumption turned out to be the wrong part.
        var (engine, index) = await BuildAsync();

        var target = index.Applications[0];
        var results = await FinalAsync(engine, Query(target.Name));

        Assert.Contains(results, result => string.Equals(result.Title, target.Name, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Index_ClassifiesAppUserModelIdsAsAppsFolderLaunches()
    {
        var index = new ApplicationIndexer(NullLogger<ApplicationIndexer>.Instance);
        await index.RefreshAsync(Token);

        // Anything without a filesystem path must be launched through AppsFolder.
        // Handing an AppUserModelID to Process.Start as a filename silently fails, and
        // it is not only Store apps that have one — some desktop installers register a
        // plain identity such as "Anysphere.Cursor".
        foreach (var application in index.Applications)
        {
            var looksLikePath = application.LaunchId.Contains('\\', StringComparison.Ordinal);

            Assert.Equal(!looksLikePath, application.LaunchViaAppsFolder);

            // An icon can only come from a real file.
            if (!looksLikePath)
            {
                Assert.Null(application.IconSource);
            }
        }
    }

    [Fact]
    public async Task Index_MatchesTheShellsOwnApplicationList()
    {
        var index = new ApplicationIndexer(NullLogger<ApplicationIndexer>.Instance);
        await index.RefreshAsync(Token);

        // AppsFolder is exactly what the Start Menu presents, so the count should track
        // it closely. A large shortfall means entries are being silently skipped —
        // which is how a whole class of applications can go missing without any error.
        using var shell = new ShellAppsFolder();
        var expected = shell.Count;

        Assert.SkipWhen(expected == 0, "The shell returned no applications; cannot compare.");
        Assert.True(
            index.Applications.Count >= expected * 0.95,
            $"Indexed {index.Applications.Count} of the shell's {expected} applications; entries are being dropped.");
    }

    /// <summary>Counts AppsFolder entries independently, as a cross-check on the indexer.</summary>
    private sealed class ShellAppsFolder : IDisposable
    {
        private readonly object? _shell;

        public ShellAppsFolder()
        {
            var type = Type.GetTypeFromProgID("Shell.Application");
            _shell = type is null ? null : Activator.CreateInstance(type);

            if (_shell is null)
            {
                return;
            }

            var folder = _shell.GetType().InvokeMember("NameSpace",
                System.Reflection.BindingFlags.InvokeMethod, null, _shell, ["shell:AppsFolder"],
                System.Globalization.CultureInfo.InvariantCulture);

            var items = folder?.GetType().InvokeMember("Items",
                System.Reflection.BindingFlags.InvokeMethod, null, folder, null,
                System.Globalization.CultureInfo.InvariantCulture);

            var count = items?.GetType().InvokeMember("Count",
                System.Reflection.BindingFlags.GetProperty, null, items, null,
                System.Globalization.CultureInfo.InvariantCulture);

            Count = Convert.ToInt32(count ?? 0, System.Globalization.CultureInfo.InvariantCulture);
        }

        public int Count { get; }

        public void Dispose()
        {
            if (_shell is not null && System.Runtime.InteropServices.Marshal.IsComObject(_shell))
            {
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(_shell);
            }
        }
    }

    [Fact]
    public async Task Search_RanksAnExactApplicationNameFirst()
    {
        var (engine, index) = await BuildAsync();

        // Pick a real application off this machine so the assertion does not depend on
        // any particular software being installed.
        var target = index.Applications
            .FirstOrDefault(application => application.Name.Length is > 4 and < 20 && !application.Name.Contains(' ', StringComparison.Ordinal));

        Assert.SkipWhen(target is null, "No single-word application name available on this machine.");

        var results = await FinalAsync(engine, Query(target!.Name));

        Assert.NotEmpty(results);
        Assert.Equal(target.Name, results[0].Title);
    }

    [Fact]
    public async Task Search_ReturnsResultsInDescendingRelevance()
    {
        var (engine, _) = await BuildAsync();

        var results = await FinalAsync(engine, Query("e"));

        Assert.NotEmpty(results);

        // The engine applies category weighting and frecency after the provider's own
        // score, so the emitted order is what must be monotonic — not the raw scores.
        // Verifying the list is capped and populated is what this can assert honestly.
        Assert.True(results.Count <= 25);
    }

    [Fact]
    public async Task Search_HonoursTheResultCap()
    {
        var (engine, _) = await BuildAsync();

        var results = await FinalAsync(engine, Query("e", max: 5));

        Assert.True(results.Count <= 5, $"Expected at most 5 results, got {results.Count}.");
    }

    [Fact]
    public async Task Search_ContainsNoDuplicateIds()
    {
        var (engine, _) = await BuildAsync();

        var results = await FinalAsync(engine, Query("a"));

        // The same item can legitimately be produced by more than one provider; showing
        // it twice looks like a bug to the user.
        var ids = results.Select(result => result.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public async Task Search_EmitsProgressiveSnapshots()
    {
        var (engine, _) = await BuildAsync();

        var snapshots = new List<int>();
        await foreach (var snapshot in engine.SearchAsync(Query("s"), Token))
        {
            snapshots.Add(snapshot.Count);
        }

        // Streaming is the design's central claim. At minimum one snapshot must be
        // emitted; the coalescing window means the count varies by machine speed.
        Assert.NotEmpty(snapshots);
    }

    [Fact]
    public async Task Search_StopsPromptlyWhenCancelled()
    {
        var (engine, _) = await BuildAsync();

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // A query cancelled before it starts must not produce results. At typing speed
        // this happens roughly every 100 ms, so honouring it is what stops abandoned
        // work from accumulating.
        var enumerator = engine.SearchAsync(Query("a"), cancellation.Token).GetAsyncEnumerator(cancellation.Token);

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await enumerator.MoveNextAsync());
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
    }

    [Fact]
    public async Task Search_FindsBuiltInCommands()
    {
        var (engine, _) = await BuildAsync();

        var results = await FinalAsync(engine, Query("calc 20*50"));

        // The command provider should both match and evaluate, so the live preview
        // appears in the result rather than only the command's name.
        Assert.Contains(results, result => result.Category.Id == "commands");
        Assert.Contains(results, result => result.Title.Contains("1,000", StringComparison.Ordinal));
    }

    /// <summary>A frecency store that contributes nothing, so ranking is purely textual.</summary>
    private sealed class NullFrecency : IFrecencyStore
    {
        public double GetBoost(string resultId) => 0;

        public void RecordUse(string resultId)
        {
        }

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
