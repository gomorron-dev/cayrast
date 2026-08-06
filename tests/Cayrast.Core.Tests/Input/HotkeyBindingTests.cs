using Cayrast.Abstractions.Input;

namespace Cayrast.Core.Tests.Input;

/// <summary>
/// Tests for <see cref="HotkeyBinding"/>.
/// </summary>
/// <remarks>
/// Hotkeys round-trip through settings as text, so parsing and formatting must be
/// exact inverses — a binding that formats to something it cannot re-parse silently
/// reverts to the default on the next launch.
/// </remarks>
public sealed class HotkeyBindingTests
{
    [Theory]
    [InlineData("Alt+Space")]
    [InlineData("Ctrl+Shift+P")]
    [InlineData("Ctrl+Alt+Shift+Win+F12")]
    [InlineData("Win+R")]
    [InlineData("Ctrl+1")]
    public void ParseAndFormat_RoundTrip(string text)
    {
        Assert.True(HotkeyBinding.TryParse(text, out var binding));
        Assert.Equal(text, binding.ToString());
    }

    [Fact]
    public void Default_IsAltSpace()
    {
        Assert.Equal("Alt+Space", HotkeyBinding.Default.ToString());
        Assert.True(HotkeyBinding.Default.IsValid);
    }

    [Theory]
    [InlineData("ctrl+shift+p", "Ctrl+Shift+P")]
    [InlineData("CONTROL+ALT+DELETE", "Ctrl+Alt+Delete")]
    [InlineData("  Alt  +  Space  ", "Alt+Space")]
    [InlineData("meta+r", "Win+R")]
    public void TryParse_AcceptsVariantSpellings(string input, string expected)
    {
        // Settings files are hand-editable, so casing and spacing must be forgiving.
        Assert.True(HotkeyBinding.TryParse(input, out var binding));
        Assert.Equal(expected, binding.ToString());
    }

    [Fact]
    public void TryParse_NormalisesModifierOrder()
    {
        // Modifiers always format in a canonical order, so two spellings of the same
        // combination compare equal rather than appearing to be different hotkeys.
        Assert.True(HotkeyBinding.TryParse("Shift+Ctrl+Alt+K", out var a));
        Assert.True(HotkeyBinding.TryParse("Alt+Shift+Ctrl+K", out var b));

        Assert.Equal(a, b);
        Assert.Equal("Ctrl+Alt+Shift+K", a.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Alt+")]
    [InlineData("+Space")]
    [InlineData("Alt+NotAKey")]
    [InlineData("Alt+Space+Enter")]
    [InlineData("Ctrl")]
    public void TryParse_RejectsMalformedInput(string input)
    {
        Assert.False(HotkeyBinding.TryParse(input, out _));
    }

    [Fact]
    public void TryParse_RejectsNull()
    {
        Assert.False(HotkeyBinding.TryParse(null, out _));
    }

    [Fact]
    public void IsValid_RequiresAModifier()
    {
        // Registering a bare key would capture it system-wide: a global hotkey of
        // "Space" alone makes it impossible to type a space in any application.
        // The settings UI relies on this to stop a user locking their own keyboard.
        var bare = new HotkeyBinding(HotkeyModifiers.None, VirtualKeys.Space);
        Assert.False(bare.IsValid);

        var withModifier = new HotkeyBinding(HotkeyModifiers.Alt, VirtualKeys.Space);
        Assert.True(withModifier.IsValid);
    }

    [Fact]
    public void IsValid_RequiresAKey()
    {
        var modifiersOnly = new HotkeyBinding(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0);
        Assert.False(modifiersOnly.IsValid);
    }

    [Theory]
    [InlineData("F1")]
    [InlineData("F12")]
    [InlineData("F24")]
    public void VirtualKeys_CoversTheFullFunctionRow(string name)
    {
        Assert.True(VirtualKeys.TryGetCode(name, out var code));
        Assert.Equal(name, VirtualKeys.GetName(code));
    }

    [Fact]
    public void ToString_OnDefaultInstance_IsEmpty()
    {
        // An unset binding must not format as a plausible-looking hotkey; the settings
        // UI shows this as "not set" rather than as a combination that does nothing.
        Assert.Equal(string.Empty, default(HotkeyBinding).ToString());
    }
}
