using Cayrast.Abstractions.Search;
using Cayrast.Core.Search;
using Cayrast.Core.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cayrast.Core.Tests.Search;

public sealed class FileSearchProviderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly TestSettingsService _settings;
    private static readonly HashSet<string> DefaultCategories = [SearchCategory.Files.Id];

    public FileSearchProviderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CayrastFileSearchTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _settings = new TestSettingsService();
        _settings.Current = _settings.Current with
        {
            Search = _settings.Current.Search with
            {
                IndexedFolders = [_tempDir]
            }
        };
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    [Fact]
    public void CanHandle_RequiresMinimumQueryLength()
    {
        var provider = new FileSearchProvider(_settings, NullLogger<FileSearchProvider>.Instance);

        Assert.False(provider.CanHandle(new SearchQuery("a", DefaultCategories)));
        Assert.False(provider.CanHandle(new SearchQuery("ab", DefaultCategories)));
        Assert.True(provider.CanHandle(new SearchQuery("abc", DefaultCategories)));
        Assert.True(provider.CanHandle(new SearchQuery("  document  ", DefaultCategories)));
    }

    [Fact]
    public async Task SearchAsync_FindsMatchingFilesAndAppliesTypedTag()
    {
        var ct = TestContext.Current.CancellationToken;
        var targetFile = Path.Combine(_tempDir, "document_report.pdf");
        await File.WriteAllTextAsync(targetFile, "dummy content", ct);

        var provider = new FileSearchProvider(_settings, NullLogger<FileSearchProvider>.Instance);
        var query = new SearchQuery("doc", DefaultCategories, MaxResults: 10);

        var results = new List<SearchResult>();
        await foreach (var item in provider.SearchAsync(query, ct))
        {
            results.Add(item);
        }

        Assert.Single(results);
        var result = results[0];
        Assert.Equal("document_report.pdf", result.Title);
        Assert.Equal(SearchCategory.Files, result.Category);
        Assert.IsType<ResultTargets.FileTarget>(result.Tag);

        var tag = (ResultTargets.FileTarget)result.Tag;
        Assert.Equal(targetFile, tag.Path);
        Assert.False(tag.IsDirectory);
    }

    [Fact]
    public async Task SearchAsync_SkipsIgnoredDirectories()
    {
        var ct = TestContext.Current.CancellationToken;
        var nodeModulesDir = Path.Combine(_tempDir, "node_modules");
        Directory.CreateDirectory(nodeModulesDir);
        var nestedFile = Path.Combine(nodeModulesDir, "package_secret.json");
        await File.WriteAllTextAsync(nestedFile, "{}", ct);

        var provider = new FileSearchProvider(_settings, NullLogger<FileSearchProvider>.Instance);
        var query = new SearchQuery("package", DefaultCategories, MaxResults: 10);

        var results = new List<SearchResult>();
        await foreach (var item in provider.SearchAsync(query, ct))
        {
            results.Add(item);
        }

        Assert.Empty(results);
    }

    private sealed class TestSettingsService : ISettingsService
    {
        public CayrastSettings Current { get; set; } = new();

        public event EventHandler<CayrastSettings>? Changed;

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpdateAsync(Func<CayrastSettings, CayrastSettings> update, CancellationToken cancellationToken = default)
        {
            Current = update(Current);
            Changed?.Invoke(this, Current);
            return Task.CompletedTask;
        }

        public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
