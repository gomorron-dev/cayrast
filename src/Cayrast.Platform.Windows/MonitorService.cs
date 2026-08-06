using System.Runtime.InteropServices;
using Cayrast.Abstractions.Platform;
using Cayrast.Platform.Windows.Interop;

namespace Cayrast.Platform.Windows;

/// <summary>Monitor geometry via the Win32 multi-monitor API.</summary>
public sealed class MonitorService : IMonitorService
{
    /// <summary>Assumed when a monitor's DPI cannot be determined. 96 is 100% scaling.</summary>
    private const uint DefaultDpi = 96;

    /// <inheritdoc />
    public MonitorInfo GetMonitorUnderCursor()
    {
        if (!NativeMethods.GetCursorPos(out var cursor))
        {
            // Can fail on a locked or secure desktop. A sensible fallback beats
            // throwing on a path that runs every time the launcher opens.
            cursor = default;
        }

        var monitor = NativeMethods.MonitorFromPoint(cursor, NativeMethods.MONITOR_DEFAULTTONEAREST);
        return Describe(monitor, windowForDpi: 0);
    }

    /// <inheritdoc />
    public MonitorInfo GetMonitorForWindow(nint windowHandle)
    {
        var monitor = NativeMethods.MonitorFromWindow(windowHandle, NativeMethods.MONITOR_DEFAULTTONEAREST);
        return Describe(monitor, windowHandle);
    }

    private static MonitorInfo Describe(nint monitor, nint windowForDpi)
    {
        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };

        if (monitor == 0 || !NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            // Degrade to a plausible single-screen layout rather than failing. The
            // launcher appearing in a slightly wrong place beats it not appearing.
            return new MonitorInfo(0, 0, 1920, 1080, DefaultDpi);
        }

        // GetDpiForWindow needs a window. Before one exists (the first positioning
        // happens during warm-up) fall back to the system default; the window is
        // repositioned on every show, so any initial guess is quickly corrected.
        var dpi = windowForDpi != 0 ? NativeMethods.GetDpiForWindow(windowForDpi) : DefaultDpi;
        if (dpi == 0)
        {
            dpi = DefaultDpi;
        }

        return new MonitorInfo(
            info.rcWork.Left,
            info.rcWork.Top,
            info.rcWork.Width,
            info.rcWork.Height,
            dpi);
    }
}
