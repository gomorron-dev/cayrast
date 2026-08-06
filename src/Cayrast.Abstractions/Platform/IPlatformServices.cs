using Cayrast.Abstractions.Input;

namespace Cayrast.Abstractions.Platform;

/// <summary>Registers and releases system-wide hotkeys.</summary>
/// <remarks>
/// Implemented over Win32 <c>RegisterHotKey</c>, which is exclusive: only one process
/// may hold a given combination at a time. Registration therefore fails routinely and
/// legitimately — another application got there first — so callers must handle a
/// <see langword="false"/> result as an expected outcome and tell the user which
/// combination is unavailable, not treat it as an error.
/// </remarks>
public interface IHotkeyService
{
    /// <summary>Attempts to register a hotkey.</summary>
    /// <param name="id">Caller-chosen identifier, used to unregister later.</param>
    /// <param name="binding">The combination to capture.</param>
    /// <param name="callback">Invoked on the UI thread when the hotkey fires.</param>
    /// <returns><see langword="false"/> if the combination is already taken.</returns>
    bool TryRegister(string id, HotkeyBinding binding, Action callback);

    /// <summary>Releases a previously registered hotkey. Safe to call for an unknown id.</summary>
    void Unregister(string id);
}

/// <summary>The system tray icon.</summary>
public interface ITrayIconService
{
    /// <summary>Raised when the icon is left-clicked or double-clicked.</summary>
    event EventHandler? Activated;

    /// <summary>Raised when a context-menu entry is chosen, carrying the item's id.</summary>
    event EventHandler<string>? MenuItemInvoked;

    /// <summary>Creates the icon and shows it.</summary>
    /// <param name="tooltip">Hover text.</param>
    /// <param name="menuItems">Context-menu entries as (id, label) pairs; an empty label is a separator.</param>
    void Show(string tooltip, IReadOnlyList<(string Id, string Label)> menuItems);

    /// <summary>Removes the icon.</summary>
    /// <remarks>
    /// Must run before the process exits. A tray icon whose owning process died leaves
    /// a ghost that lingers until the user hovers over it.
    /// </remarks>
    void Hide();
}

/// <summary>Backdrop styles a window can request from the desktop compositor.</summary>
public enum WindowBackdrop
{
    /// <summary>Plain opaque window; no compositor effect.</summary>
    None = 0,

    /// <summary>Wallpaper-tinted, opaque. Intended for long-lived primary windows.</summary>
    Mica = 1,

    /// <summary>Translucent and blurred. What Windows itself uses for transient command surfaces.</summary>
    Acrylic = 2,
}

/// <summary>Applies native window appearance that WPF cannot express.</summary>
/// <remarks>
/// Every method degrades silently on older Windows builds. Mica and Acrylic backdrops
/// need Windows 11 22H2, and rounded corners need Windows 11 — on Windows 10 the calls
/// simply have no effect, which is the correct behaviour: the window still works and
/// still looks reasonable, just without the compositor flourish.
/// </remarks>
public interface IWindowEffects
{
    /// <summary>Requests a compositor backdrop for the window.</summary>
    void ApplyBackdrop(nint windowHandle, WindowBackdrop backdrop);

    /// <summary>Requests rounded corners.</summary>
    void ApplyRoundedCorners(nint windowHandle, bool rounded);

    /// <summary>Switches the window frame between light and dark.</summary>
    void ApplyDarkMode(nint windowHandle, bool dark);

    /// <summary>Removes the window from the Alt+Tab switcher.</summary>
    /// <remarks>
    /// Hiding from the taskbar is not sufficient on its own; the window also needs the
    /// tool-window extended style. A transient launcher overlay appearing in Alt+Tab is
    /// both wrong and irritating when cycling windows.
    /// </remarks>
    void ExcludeFromAltTab(nint windowHandle);

    /// <summary>Positions and sizes a window in physical device pixels.</summary>
    /// <remarks>
    /// Bypasses WPF's logical coordinate space, which is anchored to a single scale
    /// factor and therefore mispositions windows on mixed-DPI multi-monitor setups.
    /// </remarks>
    void SetBounds(nint windowHandle, int x, int y, int width, int height);

    /// <summary>
    /// Brings a window to the foreground reliably.
    /// </summary>
    /// <remarks>
    /// Windows deliberately restricts foreground activation to prevent applications
    /// stealing focus while the user is typing elsewhere, so a bare
    /// <c>SetForegroundWindow</c> frequently fails and merely flashes the taskbar
    /// button. A launcher is the legitimate exception — the user just pressed its
    /// hotkey — so this uses the documented thread-input-attachment workaround.
    /// </remarks>
    void ForceForeground(nint windowHandle);
}
