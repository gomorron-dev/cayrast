using System.Diagnostics.CodeAnalysis;

namespace Cayrast.Abstractions.Input;

/// <summary>Modifier keys held alongside a hotkey's main key.</summary>
[Flags]
public enum HotkeyModifiers
{
    /// <summary>No modifiers.</summary>
    None = 0,

    /// <summary>Alt.</summary>
    Alt = 1,

    /// <summary>Ctrl.</summary>
    Control = 2,

    /// <summary>Shift.</summary>
    Shift = 4,

    /// <summary>The Windows key.</summary>
    Windows = 8,
}

/// <summary>
/// A global hotkey, such as <c>Alt+Space</c>.
/// </summary>
/// <remarks>
/// Stored as a virtual-key code rather than a framework key enum so this type stays
/// in <c>Cayrast.Abstractions</c> with no dependencies, and round-trips through
/// settings JSON as a plain readable string.
/// </remarks>
/// <param name="Modifiers">Modifier keys that must be held.</param>
/// <param name="VirtualKey">Win32 virtual-key code of the main key.</param>
public readonly record struct HotkeyBinding(HotkeyModifiers Modifiers, uint VirtualKey)
{
    /// <summary>The application default, <c>Alt+Space</c>.</summary>
    public static readonly HotkeyBinding Default = new(HotkeyModifiers.Alt, VirtualKeys.Space);

    /// <summary>Whether this binding is usable as a global hotkey.</summary>
    /// <remarks>
    /// A bare key with no modifier would swallow that key system-wide — registering
    /// <c>Space</c> alone would make it impossible to type a space in any application.
    /// The settings UI must reject such a binding rather than let a user lock
    /// themselves out of their own keyboard.
    /// </remarks>
    public bool IsValid => Modifiers != HotkeyModifiers.None && VirtualKey != 0;

    /// <summary>Formats the binding for display and for settings storage, e.g. <c>Alt+Space</c>.</summary>
    public override string ToString()
    {
        if (VirtualKey == 0)
        {
            return string.Empty;
        }

        var parts = new List<string>(4);
        if (Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(VirtualKeys.GetName(VirtualKey));
        return string.Join('+', parts);
    }

    /// <summary>Parses a binding such as <c>Alt+Space</c> or <c>Ctrl+Shift+P</c>.</summary>
    public static bool TryParse([NotNullWhen(true)] string? text, out HotkeyBinding binding)
    {
        binding = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        uint virtualKey = 0;

        // Empty segments are kept rather than discarded so that malformed input such
        // as "+Space" or "Alt++X" is rejected. Silently dropping them would accept a
        // string that does not round-trip, and the binding would appear to change by
        // itself the next time settings were written.
        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries))
        {
            if (raw.Length == 0)
            {
                return false;
            }

            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control":
                    modifiers |= HotkeyModifiers.Control;
                    break;
                case "alt":
                    modifiers |= HotkeyModifiers.Alt;
                    break;
                case "shift":
                    modifiers |= HotkeyModifiers.Shift;
                    break;
                case "win" or "windows" or "meta":
                    modifiers |= HotkeyModifiers.Windows;
                    break;
                default:
                    // More than one non-modifier key is malformed, not merely unusual.
                    if (virtualKey != 0 || !VirtualKeys.TryGetCode(raw, out virtualKey))
                    {
                        return false;
                    }

                    break;
            }
        }

        if (virtualKey == 0)
        {
            return false;
        }

        binding = new HotkeyBinding(modifiers, virtualKey);
        return true;
    }
}

/// <summary>Virtual-key codes and their display names.</summary>
public static class VirtualKeys
{
    /// <summary>The space bar.</summary>
    public const uint Space = 0x20;

    /// <summary>Escape.</summary>
    public const uint Escape = 0x1B;

    private static readonly Dictionary<string, uint> NameToCode = BuildNameToCode();
    private static readonly Dictionary<uint, string> CodeToName =
        NameToCode.GroupBy(pair => pair.Value)
                  .ToDictionary(group => group.Key, group => group.First().Key);

    /// <summary>Resolves a key name such as <c>Space</c> or <c>F1</c> to its virtual-key code.</summary>
    public static bool TryGetCode(string name, out uint code) =>
        NameToCode.TryGetValue(name.Trim(), out code);

    /// <summary>Returns the display name for a virtual-key code.</summary>
    public static string GetName(uint code) =>
        CodeToName.TryGetValue(code, out var name) ? name : $"0x{code:X2}";

    private static Dictionary<string, uint> BuildNameToCode()
    {
        var map = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
        {
            ["Space"] = 0x20,
            ["Enter"] = 0x0D,
            ["Tab"] = 0x09,
            ["Escape"] = 0x1B,
            ["Backspace"] = 0x08,
            ["Delete"] = 0x2E,
            ["Insert"] = 0x2D,
            ["Home"] = 0x24,
            ["End"] = 0x23,
            ["PageUp"] = 0x21,
            ["PageDown"] = 0x22,
            ["Left"] = 0x25,
            ["Up"] = 0x26,
            ["Right"] = 0x27,
            ["Down"] = 0x28,
            ["`"] = 0xC0,
            ["-"] = 0xBD,
            ["="] = 0xBB,
            ["["] = 0xDB,
            ["]"] = 0xDD,
            ["\\"] = 0xDC,
            [";"] = 0xBA,
            ["'"] = 0xDE,
            [","] = 0xBC,
            ["."] = 0xBE,
            ["/"] = 0xBF,
        };

        // A-Z map directly to their ASCII codes.
        for (var c = 'A'; c <= 'Z'; c++)
        {
            map[c.ToString()] = c;
        }

        // 0-9 likewise.
        for (var c = '0'; c <= '9'; c++)
        {
            map[c.ToString()] = c;
        }

        // F1-F24 are contiguous from 0x70.
        for (uint i = 1; i <= 24; i++)
        {
            map[$"F{i}"] = 0x70 + i - 1;
        }

        return map;
    }
}
