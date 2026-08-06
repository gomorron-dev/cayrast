using Cayrast.Abstractions.Platform;
using Cayrast.Platform.Windows.Interop;
using Microsoft.Extensions.Logging;

namespace Cayrast.Platform.Windows;

/// <summary>
/// Native window appearance that WPF cannot express, applied through DWM.
/// </summary>
/// <remarks>
/// Every attribute here was added in a specific Windows build, and DWM returns a
/// failure HRESULT rather than throwing when asked for one the running build does not
/// support. That is treated as normal: on Windows 10 the launcher simply renders
/// without a compositor backdrop or rounded corners, which looks plainer but works
/// identically. Version-gating each call instead would mean maintaining a build-number
/// table that goes stale.
/// </remarks>
public sealed class WindowEffects(ILogger<WindowEffects> logger) : IWindowEffects
{
    /// <inheritdoc />
    public void ApplyBackdrop(nint windowHandle, WindowBackdrop backdrop)
    {
        if (windowHandle == 0)
        {
            return;
        }

        var value = backdrop switch
        {
            WindowBackdrop.Mica => Win32.DWMSBT_MAINWINDOW,
            WindowBackdrop.Acrylic => Win32.DWMSBT_TRANSIENTWINDOW,
            _ => Win32.DWMSBT_NONE,
        };

        var result = NativeMethods.DwmSetWindowAttribute(
            windowHandle, Win32.DWMWA_SYSTEMBACKDROP_TYPE, in value, sizeof(int));

        if (result != 0)
        {
            logger.LogDebug(
                "Backdrop {Backdrop} unavailable on this Windows build (HRESULT 0x{Result:X}).",
                backdrop, result);
        }
    }

    /// <inheritdoc />
    public void ApplyRoundedCorners(nint windowHandle, bool rounded)
    {
        if (windowHandle == 0)
        {
            return;
        }

        var value = rounded ? Win32.DWMWCP_ROUND : Win32.DWMWCP_DONOTROUND;
        NativeMethods.DwmSetWindowAttribute(
            windowHandle, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, in value, sizeof(int));
    }

    /// <inheritdoc />
    public void ApplyDarkMode(nint windowHandle, bool dark)
    {
        if (windowHandle == 0)
        {
            return;
        }

        var value = dark ? 1 : 0;
        NativeMethods.DwmSetWindowAttribute(
            windowHandle, Win32.DWMWA_USE_IMMERSIVE_DARK_MODE, in value, sizeof(int));
    }

    /// <inheritdoc />
    public void ExcludeFromAltTab(nint windowHandle)
    {
        if (windowHandle == 0)
        {
            return;
        }

        var style = NativeMethods.GetWindowLongPtr(windowHandle, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLongPtr(
            windowHandle, NativeMethods.GWL_EXSTYLE, style | NativeMethods.WS_EX_TOOLWINDOW);
    }

    /// <inheritdoc />
    public void SetBounds(nint windowHandle, int x, int y, int width, int height)
    {
        if (windowHandle == 0)
        {
            return;
        }

        // NOACTIVATE because positioning happens before the deliberate foreground
        // activation; letting SetWindowPos activate would race that and can leave the
        // window visible but without keyboard focus.
        NativeMethods.SetWindowPos(
            windowHandle, 0, x, y, width, height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    /// <inheritdoc />
    public void ForceForeground(nint windowHandle)
    {
        if (windowHandle == 0)
        {
            return;
        }

        // Windows only lets the process that owns the current foreground window hand
        // focus away. A launcher is the legitimate case this restriction was never
        // meant to block — the user just pressed our hotkey — so we temporarily attach
        // our input queue to the foreground thread's, which makes the system treat the
        // call as coming from the active application.
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == 0 || foreground == windowHandle)
        {
            NativeMethods.SetForegroundWindow(windowHandle);
            return;
        }

        var foregroundThread = NativeMethods.GetWindowThreadProcessId(foreground, 0);
        var currentThread = NativeMethods.GetCurrentThreadId();

        if (foregroundThread == currentThread)
        {
            NativeMethods.SetForegroundWindow(windowHandle);
            return;
        }

        var attached = NativeMethods.AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            NativeMethods.SetForegroundWindow(windowHandle);
        }
        finally
        {
            // Detaching matters: a leaked attachment couples our input queue to
            // another process, so if that process hangs, ours stops receiving input too.
            if (attached)
            {
                NativeMethods.AttachThreadInput(currentThread, foregroundThread, false);
            }
        }
    }
}
