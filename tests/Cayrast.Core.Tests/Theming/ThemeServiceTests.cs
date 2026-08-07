using Cayrast.Abstractions;
using Cayrast.Core.Storage;
using Cayrast.Core.Theming;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cayrast.Core.Tests.Theming;

public sealed class ThemeServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CayrastPaths _paths;

    public ThemeServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CayrastThemeTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _paths = new CayrastPaths(_tempDir, _tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task RefreshAsync_LoadsValidThemeFiles()
    {
        var ct = TestContext.Current.CancellationToken;

        var themeContent = """
        {
            "id": "theme.dark",
            "name": "Dark Theme",
            "base": "dark",
            "variables": {
                "--cy-accent": "#8d8473"
            }
        }
        """;

        var filePath = Path.Combine(_paths.Themes, $"dark{CayrastBrand.ThemePackageExtension}");
        Directory.CreateDirectory(_paths.Themes);
        await File.WriteAllTextAsync(filePath, themeContent, ct);

        var service = new ThemeService(_paths, NullLogger<ThemeService>.Instance);
        await service.RefreshAsync(ct);

        Assert.Single(service.Themes);
        var loaded = service.Themes[0];
        Assert.Equal("theme.dark", loaded.Theme.Id);
        Assert.Equal("Dark Theme", loaded.Theme.Name);
        Assert.Equal("dark", loaded.Theme.Base);
        Assert.Equal("#8d8473", loaded.SafeVariables["--cy-accent"]);
        Assert.Empty(loaded.RejectedVariables);

        var found = service.Find("THEME.DARK");
        Assert.NotNull(found);
        Assert.Equal("Dark Theme", found.Theme.Name);
    }

    [Fact]
    public async Task RefreshAsync_SkipsMalformedOrInvalidThemes()
    {
        var ct = TestContext.Current.CancellationToken;
        Directory.CreateDirectory(_paths.Themes);

        // Missing id/name
        var invalidJson = """{ "base": "dark" }""";
        await File.WriteAllTextAsync(Path.Combine(_paths.Themes, $"invalid{CayrastBrand.ThemePackageExtension}"), invalidJson, ct);

        // Corrupted JSON
        var brokenJson = """{ "id": "broken", "name": """;
        await File.WriteAllTextAsync(Path.Combine(_paths.Themes, $"broken{CayrastBrand.ThemePackageExtension}"), brokenJson, ct);

        var service = new ThemeService(_paths, NullLogger<ThemeService>.Instance);
        await service.RefreshAsync(ct);

        Assert.Empty(service.Themes);
        Assert.Null(service.Find("invalid"));
    }
}
