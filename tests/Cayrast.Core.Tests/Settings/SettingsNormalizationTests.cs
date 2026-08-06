using System.Text.Json;
using Cayrast.Core.Settings;

namespace Cayrast.Core.Tests.Settings;

/// <summary>
/// Tests for <see cref="CayrastSettings.Normalized"/>.
/// </summary>
/// <remarks>
/// <para>
/// These exist because property initialisers alone are not enough to guarantee a
/// usable settings tree, which was established empirically rather than assumed:
/// </para>
/// <list type="bullet">
///   <item>An explicit <c>null</c> in the JSON overwrites a non-nullable property.</item>
///   <item>Whether an absent property keeps its initialiser differs between the
///   reflection serialiser and the source generator.</item>
/// </list>
/// <para>
/// Normalisation is the single place that makes the result trustworthy regardless,
/// and it also clamps the out-of-range values a hand-edited file can contain.
/// </para>
/// </remarks>
public sealed class SettingsNormalizationTests
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Normalized_ReplacesExplicitNullsWithDefaults()
    {
        // The serialiser genuinely produces null here for a non-nullable property.
        // Without normalisation this surfaces as a crash far from its cause.
        const string Json = """{ "schemaVersion": 1, "appearance": { "accentColor": null, "fontFamily": null } }""";

        var settings = JsonSerializer.Deserialize<CayrastSettings>(Json, WebOptions)!.Normalized();

        Assert.Equal(AppearanceSettings.DefaultAccentColor, settings.Appearance.AccentColor);
        Assert.Equal(string.Empty, settings.Appearance.FontFamily);
    }

    [Fact]
    public void Normalized_ReplacesNullSectionsWithDefaults()
    {
        const string Json = """{ "schemaVersion": 1, "appearance": null, "privacy": null }""";

        var settings = JsonSerializer.Deserialize<CayrastSettings>(Json, WebOptions)!.Normalized();

        Assert.NotNull(settings.Appearance);
        Assert.NotNull(settings.Privacy);
        Assert.Equal(AppearanceSettings.DefaultAccentColor, settings.Appearance.AccentColor);
    }

    [Fact]
    public void Normalized_ReplacesNullCollectionsWithEmpty()
    {
        // A null category list would fault on the first keystroke.
        const string Json = """{ "schemaVersion": 1, "search": { "enabledCategories": null, "indexedFolders": null } }""";

        var settings = JsonSerializer.Deserialize<CayrastSettings>(Json, WebOptions)!.Normalized();

        Assert.NotNull(settings.Search.EnabledCategories);
        Assert.NotNull(settings.Search.IndexedFolders);
    }

    [Theory]
    // A fully transparent panel would be invisible with no way to find or dismiss it.
    [InlineData(0.0, 0.2)]
    [InlineData(-5.0, 0.2)]
    [InlineData(5.0, 1.0)]
    [InlineData(0.85, 0.85)]
    public void Normalized_ClampsTransparencyAwayFromInvisible(double input, double expected)
    {
        var settings = new CayrastSettings
        {
            Appearance = new AppearanceSettings { Transparency = input },
        }.Normalized();

        Assert.Equal(expected, settings.Appearance.Transparency);
    }

    [Theory]
    [InlineData(-100, 320)]
    [InlineData(0, 320)]
    [InlineData(99999, 2000)]
    [InlineData(720, 720)]
    public void Normalized_ClampsPanelWidthToAUsableRange(int input, int expected)
    {
        var settings = new CayrastSettings
        {
            Appearance = new AppearanceSettings { PanelWidth = input },
        }.Normalized();

        Assert.Equal(expected, settings.Appearance.PanelWidth);
    }

    [Fact]
    public void Normalized_AllowsZeroAnimationSpeed()
    {
        // Zero is meaningful rather than invalid: it is how animation is switched off.
        var settings = new CayrastSettings
        {
            Appearance = new AppearanceSettings { AnimationSpeed = 0.0 },
        }.Normalized();

        Assert.Equal(0.0, settings.Appearance.AnimationSpeed);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-10, 0)]
    [InlineData(5000, 250)]
    [InlineData(40, 40)]
    public void Normalized_ClampsDebounceBelowThePerceptibleThreshold(int input, int expected)
    {
        var settings = new CayrastSettings
        {
            Search = new SearchSettings { DebounceMilliseconds = input },
        }.Normalized();

        Assert.Equal(expected, settings.Search.DebounceMilliseconds);
    }

    [Fact]
    public void Normalized_RestoresAnEmptyHotkey()
    {
        var settings = new CayrastSettings
        {
            Behavior = new BehaviorSettings { Hotkey = "   " },
        }.Normalized();

        Assert.Equal("Alt+Space", settings.Behavior.Hotkey);
    }

    [Fact]
    public void Normalized_AlwaysStampsTheCurrentSchemaVersion()
    {
        var settings = new CayrastSettings { SchemaVersion = 0 }.Normalized();

        Assert.Equal(CayrastSettings.CurrentSchemaVersion, settings.SchemaVersion);
    }

    [Fact]
    public void Normalized_IsIdempotent()
    {
        // Normalisation runs on both load and update, so applying it twice must not
        // drift a value that was already valid.
        var once = new CayrastSettings().Normalized();
        var twice = once.Normalized();

        Assert.Equal(once, twice);
    }
}
