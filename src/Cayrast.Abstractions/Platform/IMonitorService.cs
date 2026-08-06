namespace Cayrast.Abstractions.Platform;

/// <summary>
/// A monitor's usable area, in physical device pixels.
/// </summary>
/// <remarks>
/// Device pixels rather than WPF's device-independent units. On a mixed-DPI setup —
/// a 150% laptop panel beside a 100% external display — WPF's logical coordinate
/// space is anchored to one scale factor, so positioning a window on the other
/// monitor by logical coordinates lands it in the wrong place or the wrong size.
/// Working in device pixels and calling SetWindowPos directly sidesteps that entirely.
/// </remarks>
/// <param name="WorkAreaX">Left edge of the work area, excluding the taskbar.</param>
/// <param name="WorkAreaY">Top edge of the work area, excluding the taskbar.</param>
/// <param name="WorkAreaWidth">Work area width.</param>
/// <param name="WorkAreaHeight">Work area height.</param>
/// <param name="Dpi">Monitor DPI; 96 is 100% scaling.</param>
public readonly record struct MonitorInfo(
    int WorkAreaX,
    int WorkAreaY,
    int WorkAreaWidth,
    int WorkAreaHeight,
    uint Dpi)
{
    /// <summary>Scale factor where 1.0 is 100%.</summary>
    public double Scale => Dpi / 96.0;
}

/// <summary>Locates monitors and their usable areas.</summary>
public interface IMonitorService
{
    /// <summary>The monitor currently containing the mouse cursor.</summary>
    /// <remarks>
    /// The cursor is the best available proxy for where the user is looking. Opening
    /// the launcher on the monitor they are working on — rather than always the
    /// primary — is what makes a multi-monitor setup feel handled rather than ignored.
    /// </remarks>
    MonitorInfo GetMonitorUnderCursor();

    /// <summary>The monitor a window currently occupies most of.</summary>
    MonitorInfo GetMonitorForWindow(nint windowHandle);
}
