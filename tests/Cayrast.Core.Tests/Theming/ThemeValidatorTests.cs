using Cayrast.Core.Theming;

namespace Cayrast.Core.Tests.Theming;

/// <summary>
/// Tests for <see cref="ThemeValidator"/>.
/// </summary>
/// <remarks>
/// Theme files are downloaded from anywhere and their values are injected into the
/// stylesheet as custom properties. That makes an unvalidated value a CSS injection
/// vector, so these are security tests rather than input-tidying tests.
/// </remarks>
public sealed class ThemeValidatorTests
{
    private static CayrastTheme ThemeWith(params (string Name, string Value)[] variables) => new()
    {
        Name = "Test",
        Id = "test.theme",
        Variables = variables.ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal),
    };

    [Theory]
    [InlineData("--cy-accent", "#8d8473")]
    [InlineData("--cy-accent", "#fff")]
    [InlineData("--cy-accent", "#8d8473ff")]
    [InlineData("--cy-bg-panel", "rgba(32, 32, 34, 0.72)")]
    [InlineData("--cy-fg-primary", "hsl(210, 40%, 96%)")]
    [InlineData("--cy-radius-panel", "12px")]
    [InlineData("--cy-motion-scale", "1.5")]
    [InlineData("--cy-duration-fast", "120ms")]
    [InlineData("--cy-font", "Segoe UI")]
    [InlineData("--cy-ease", "cubic-bezier(0.22, 1, 0.36, 1)")]
    [InlineData("--cy-accent-muted", "color-mix(in srgb, red 18%, transparent)")]
    public void Sanitise_AcceptsLegitimateValues(string name, string value)
    {
        var safe = ThemeValidator.Sanitise(ThemeWith((name, value)), out var rejected);

        Assert.Empty(rejected);
        Assert.Equal(value, safe[name]);
    }

    [Theory]
    // Closing the declaration and writing arbitrary rules.
    [InlineData("--cy-accent", "red; } body { display: none")]
    [InlineData("--cy-accent", "red;}html{background:url(http://evil.test/x)}")]
    // Remote fetch, which would leak that the user is running Cayrast.
    [InlineData("--cy-bg-panel", "url(http://evil.test/track.png)")]
    [InlineData("--cy-bg-panel", "url('http://evil.test/track.png')")]
    // At-rules.
    [InlineData("--cy-accent", "@import url(http://evil.test/x.css)")]
    // Script-flavoured legacy expressions.
    [InlineData("--cy-accent", "expression(alert(1))")]
    public void Sanitise_RejectsInjectionAttempts(string name, string value)
    {
        var safe = ThemeValidator.Sanitise(ThemeWith((name, value)), out var rejected);

        Assert.False(safe.ContainsKey(name), $"'{value}' should not have been accepted.");
        Assert.Contains(name, rejected, StringComparer.Ordinal);
    }

    [Theory]
    // Only Cayrast's own token namespace may be set. Allowing arbitrary property names
    // would let a theme override variables belonging to a module's UI.
    [InlineData("color")]
    [InlineData("--evil")]
    [InlineData("--cy-Accent")]
    [InlineData("--cy_accent")]
    [InlineData("background")]
    public void Sanitise_RejectsVariableNamesOutsideTheCayrastNamespace(string name)
    {
        var safe = ThemeValidator.Sanitise(ThemeWith((name, "#ffffff")), out var rejected);

        Assert.Empty(safe);
        Assert.Contains(name, rejected, StringComparer.Ordinal);
    }

    [Fact]
    public void Sanitise_KeepsGoodVariablesWhenOneIsBad()
    {
        // One bad value should cost the author that token, not make the whole theme
        // refuse to load with no indication of which line was wrong.
        var theme = ThemeWith(
            ("--cy-accent", "#8d8473"),
            ("--cy-bg-panel", "red; } body { display:none"),
            ("--cy-radius-panel", "16px"));

        var safe = ThemeValidator.Sanitise(theme, out var rejected);

        Assert.Equal(2, safe.Count);
        Assert.Equal("#8d8473", safe["--cy-accent"]);
        Assert.Equal("16px", safe["--cy-radius-panel"]);
        Assert.Single(rejected);
    }

    [Fact]
    public void Sanitise_IgnoresEmptyEntries()
    {
        var safe = ThemeValidator.Sanitise(ThemeWith(("--cy-accent", "   "), ("  ", "#fff")), out _);

        Assert.Empty(safe);
    }

    [Theory]
    [InlineData("light", "light")]
    [InlineData("LIGHT", "light")]
    [InlineData("dark", "dark")]
    [InlineData("nonsense", "dark")]
    [InlineData(null, "dark")]
    public void NormaliseBase_FallsBackToDark(string? input, string expected)
    {
        // Dark is the fallback because the launcher is an overlay: a bright panel over
        // a dark desktop is the more jarring failure.
        Assert.Equal(expected, ThemeValidator.NormaliseBase(input));
    }
}
