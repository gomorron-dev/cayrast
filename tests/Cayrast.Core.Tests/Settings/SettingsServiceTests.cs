using Cayrast.Core.Settings;
using Cayrast.Core.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cayrast.Core.Tests.Settings;

/// <summary>
/// Tests for <see cref="SettingsService"/>.
/// </summary>
/// <remarks>
/// The behaviour that matters most here is not "settings round-trip", it is what
/// happens when the file is damaged. Cayrast is the tool a user launches everything
/// else from, so refusing to start because of a malformed config would be the worst
/// possible failure mode — worse than losing the settings themselves.
/// </remarks>
public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cayrast-tests", Guid.NewGuid().ToString("N"));
    private readonly CayrastPaths _paths;

    public SettingsServiceTests()
    {
        _paths = new CayrastPaths(Path.Combine(_root, "roaming"), Path.Combine(_root, "local"));
        _paths.EnsureCreated();
    }

    /// <summary>Ties file and service operations to the test runner's cancellation.</summary>
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private SettingsService CreateService() => new(_paths, NullLogger<SettingsService>.Instance);

    private string SettingsFile => Path.Combine(_paths.Settings, "settings.json");

    [Fact]
    public async Task LoadAsync_WithNoFile_UsesDefaultsAndWritesThem()
    {
        var service = CreateService();

        await service.LoadAsync(Token);

        Assert.Equal(CayrastSettings.CurrentSchemaVersion, service.Current.SchemaVersion);
        Assert.Equal(AppearanceSettings.DefaultAccentColor, service.Current.Appearance.AccentColor);

        // Writing defaults on first run gives the user a file to edit and makes the
        // available options discoverable without documentation.
        Assert.True(File.Exists(SettingsFile));
    }

    [Fact]
    public async Task UpdateAsync_ThenFlush_PersistsAcrossInstances()
    {
        var service = CreateService();
        await service.LoadAsync(Token);

        await service.UpdateAsync(
            current => current with
            {
                Appearance = current.Appearance with { AccentColor = "#ff0000", PanelWidth = 900 },
            },
            Token);
        await service.FlushAsync(Token);

        var reloaded = CreateService();
        await reloaded.LoadAsync(Token);

        Assert.Equal("#ff0000", reloaded.Current.Appearance.AccentColor);
        Assert.Equal(900, reloaded.Current.Appearance.PanelWidth);
    }

    [Fact]
    public async Task LoadAsync_WithCorruptFile_FallsBackToDefaultsAndQuarantinesIt()
    {
        await File.WriteAllTextAsync(SettingsFile, "{ this is not valid json ", Token);

        var service = CreateService();
        await service.LoadAsync(Token);

        // Starting successfully is the whole point.
        Assert.Equal(AppearanceSettings.DefaultAccentColor, service.Current.Appearance.AccentColor);

        // The damaged file is preserved, not overwritten: it may represent real work,
        // and it is the only evidence of what went wrong.
        var quarantined = Directory.GetFiles(_paths.Settings, "settings.json.corrupt-*");
        Assert.Single(quarantined);
        Assert.Contains("not valid json", await File.ReadAllTextAsync(quarantined[0], Token), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_WithEmptyFile_FallsBackToDefaults()
    {
        // A zero-byte file is what a crash or a full disk during a write leaves behind.
        await File.WriteAllTextAsync(SettingsFile, string.Empty, Token);

        var service = CreateService();
        await service.LoadAsync(Token);

        Assert.Equal(CayrastSettings.CurrentSchemaVersion, service.Current.SchemaVersion);
    }

    [Fact]
    public async Task LoadAsync_WithNewerSchema_UsesDefaultsWithoutTouchingTheFile()
    {
        // Simulates a downgrade: the user rolled back after a bad update, and the file
        // on disk was written by a version that knew about fields this one does not.
        const string FutureSettings = """
            { "schemaVersion": 9999, "appearance": { "accentColor": "#123456" } }
            """;
        await File.WriteAllTextAsync(SettingsFile, FutureSettings, Token);

        var service = CreateService();
        await service.LoadAsync(Token);

        Assert.Equal(AppearanceSettings.DefaultAccentColor, service.Current.Appearance.AccentColor);

        // Critically, the newer file must survive intact so that re-upgrading restores
        // the user's configuration rather than silently discarding it.
        var onDisk = await File.ReadAllTextAsync(SettingsFile, Token);
        Assert.Contains("9999", onDisk, StringComparison.Ordinal);
        Assert.Contains("#123456", onDisk, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_WithPartialFile_FillsMissingSectionsWithDefaults()
    {
        // Hand-edited files are expected; a user who sets one value should not lose
        // every value they did not mention.
        const string PartialSettings = """
            { "schemaVersion": 1, "appearance": { "panelWidth": 640 } }
            """;
        await File.WriteAllTextAsync(SettingsFile, PartialSettings, Token);

        var service = CreateService();
        await service.LoadAsync(Token);

        Assert.Equal(640, service.Current.Appearance.PanelWidth);
        Assert.Equal(AppearanceSettings.DefaultAccentColor, service.Current.Appearance.AccentColor);
        Assert.NotNull(service.Current.Behavior);
        Assert.NotNull(service.Current.Privacy);
    }

    [Fact]
    public async Task UpdateAsync_RaisesChangedWithTheNewValue()
    {
        var service = CreateService();
        await service.LoadAsync(Token);

        CayrastSettings? observed = null;
        service.Changed += (_, updated) => observed = updated;

        await service.UpdateAsync(
            current => current with { Behavior = current.Behavior with { Hotkey = "Ctrl+Shift+P" } },
            Token);

        Assert.NotNull(observed);
        Assert.Equal("Ctrl+Shift+P", observed.Behavior.Hotkey);
    }

    [Fact]
    public async Task UpdateAsync_AlwaysStampsTheCurrentSchemaVersion()
    {
        var service = CreateService();
        await service.LoadAsync(Token);

        // A caller reconstructing settings from an old payload must not be able to
        // write a stale version back and trigger a spurious migration next launch.
        await service.UpdateAsync(current => current with { SchemaVersion = 0 }, Token);

        Assert.Equal(CayrastSettings.CurrentSchemaVersion, service.Current.SchemaVersion);
    }

    [Fact]
    public async Task DisposeAsync_FlushesAPendingDebouncedWrite()
    {
        var service = CreateService();
        await service.LoadAsync(Token);

        await service.UpdateAsync(
            current => current with { Appearance = current.Appearance with { PanelWidth = 1234 } },
            Token);

        // Disposed well inside the debounce window: without a flush on dispose the
        // change would never reach disk, which users experience as "it forgot".
        await service.DisposeAsync();

        var reloaded = CreateService();
        await reloaded.LoadAsync(Token);
        Assert.Equal(1234, reloaded.Current.Appearance.PanelWidth);
    }

    [Fact]
    public async Task PrivacyDefaults_AreTheConservativeChoice()
    {
        var service = CreateService();
        await service.LoadAsync(Token);

        // A user who never opens settings must get the most private configuration.
        // Browser history is the most sensitive source Cayrast can reach, so it stays
        // opt-in; clipboard encryption and exclusion handling stay on.
        Assert.False(service.Current.Privacy.EnableBrowserHistory);
        Assert.True(service.Current.Privacy.EncryptClipboard);
        Assert.True(service.Current.Privacy.RespectClipboardExclusions);
        Assert.False(service.Current.Behavior.LaunchAtStartup);
        Assert.False(service.Current.Updates.AutomaticallyInstall);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A temp directory left behind is not worth failing a test run over.
        }
    }
}
