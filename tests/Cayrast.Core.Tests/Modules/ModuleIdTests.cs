using Cayrast.Abstractions.Modules;

namespace Cayrast.Core.Tests.Modules;

/// <summary>
/// Tests for <see cref="ModuleId"/>.
/// </summary>
/// <remarks>
/// These matter more than their size suggests. A module id is read from a manifest
/// inside a <c>.cayrast</c> file the user downloaded from anywhere, and it is then
/// used to build filesystem paths, database keys, and WebView2 origins. If a
/// malformed id survives parsing, it escapes all three at once — so the rejection
/// cases below are security tests, not input-validation pedantry.
/// </remarks>
public sealed class ModuleIdTests
{
    [Theory]
    [InlineData("cayrast.clipboard")]
    [InlineData("example.module")]
    [InlineData("acme.dev-tools")]
    [InlineData("acme.dev_tools")]
    [InlineData("a.b.c.d")]
    [InlineData("vendor123.module456")]
    public void Parse_AcceptsWellFormedIds(string value)
    {
        var id = ModuleId.Parse(value);

        Assert.Equal(value, id.Value);
    }

    [Theory]
    // Path traversal — the case that would let a module write outside its own directory.
    [InlineData("../../evil")]
    [InlineData("..")]
    [InlineData("foo/../bar")]
    // Path separators, which would silently create nested directories.
    [InlineData("foo/bar")]
    [InlineData("foo\\bar")]
    // Absolute and UNC paths.
    [InlineData("C:\\Windows\\System32")]
    [InlineData("\\\\server\\share")]
    // Missing the required namespace segment: a bare word could collide with a folder name.
    [InlineData("clipboard")]
    // Structurally malformed.
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".leading")]
    [InlineData("trailing.")]
    [InlineData("double..dot")]
    // Characters that are illegal in filesystem paths or DNS labels.
    [InlineData("foo.bar baz")]
    [InlineData("foo.bar:baz")]
    [InlineData("foo.bar*")]
    [InlineData("foo.<script>")]
    // Null byte truncation, a classic way past naive path validation.
    [InlineData("foo.bar\0evil")]
    public void TryParse_RejectsMalformedIds(string value)
    {
        Assert.False(ModuleId.TryParse(value, out _));
    }

    [Fact]
    public void TryParse_RejectsNull()
    {
        Assert.False(ModuleId.TryParse(null, out _));
    }

    [Fact]
    public void Parse_ThrowsOnMalformedInput()
    {
        var exception = Assert.Throws<FormatException>(() => ModuleId.Parse("../../evil"));

        // The message must name the offending value: module load failures are surfaced
        // to users, and "invalid module id" alone is not actionable.
        Assert.Contains("../../evil", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Cayrast.Clipboard", "cayrast.clipboard")]
    [InlineData("  cayrast.clipboard  ", "cayrast.clipboard")]
    [InlineData("CAYRAST.CLIPBOARD", "cayrast.clipboard")]
    public void Parse_NormalisesCaseAndWhitespace(string input, string expected)
    {
        // Normalising once at the boundary means everything downstream — dictionary
        // lookups, path building, permission checks — can compare ordinally without
        // each site remembering to be case-insensitive.
        Assert.Equal(expected, ModuleId.Parse(input).Value);
    }

    [Fact]
    public void Slug_IsSafeForUseAsDnsLabel()
    {
        // Dots would create unintended subdomain nesting in the module's WebView2
        // origin, so they collapse to hyphens: mod-cayrast-clipboard.cayrast.local
        var id = ModuleId.Parse("cayrast.clipboard");

        Assert.Equal("cayrast-clipboard", id.Slug);
        Assert.DoesNotContain('.', id.Slug);
    }

    [Fact]
    public void Equality_IsValueBased()
    {
        // ModuleId is used as a dictionary key throughout the module registry.
        Assert.Equal(ModuleId.Parse("cayrast.clipboard"), ModuleId.Parse("Cayrast.Clipboard"));
        Assert.NotEqual(ModuleId.Parse("cayrast.clipboard"), ModuleId.Parse("cayrast.qr"));
    }
}
