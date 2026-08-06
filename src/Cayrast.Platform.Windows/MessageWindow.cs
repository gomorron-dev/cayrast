using System.Runtime.InteropServices;
using Cayrast.Platform.Windows.Interop;
using Microsoft.Extensions.Logging;

namespace Cayrast.Platform.Windows;

/// <summary>Arguments for a raw window message.</summary>
public sealed class WindowMessageEventArgs(uint message, nint wParam, nint lParam) : EventArgs
{
    /// <summary>The Win32 message identifier.</summary>
    public uint Message { get; } = message;

    /// <summary>Message-specific first parameter.</summary>
    public nint WParam { get; } = wParam;

    /// <summary>Message-specific second parameter.</summary>
    public nint LParam { get; } = lParam;

    /// <summary>Set to <see langword="true"/> to stop the default handler running.</summary>
    public bool Handled { get; set; }

    /// <summary>Value returned to Windows when <see cref="Handled"/> is set.</summary>
    public nint Result { get; set; }
}

/// <summary>
/// A hidden message-only window shared by the platform services.
/// </summary>
/// <remarks>
/// <para>
/// Global hotkeys and tray icons both need an HWND to deliver messages to, but neither
/// needs anything visible. A message-only window (parented to <c>HWND_MESSAGE</c>) is
/// the Win32 answer: it never renders, never appears in Alt+Tab or the taskbar, and
/// receives no input — but posted messages reach its window procedure normally.
/// </para>
/// <para>
/// <b>Must be constructed on the UI thread.</b> Window messages are dispatched per
/// thread, so this window only receives messages if it is created on a thread that
/// pumps a message loop. In Cayrast that is the WPF dispatcher thread.
/// </para>
/// </remarks>
public sealed class MessageWindow : IDisposable
{
    private readonly ILogger<MessageWindow> _logger;

    /// <summary>
    /// Held in a field for the window's entire lifetime.
    /// </summary>
    /// <remarks>
    /// Win32 stores a raw function pointer to this delegate. If the managed instance
    /// were collected while the window still existed, the next dispatched message would
    /// call into freed memory. Keeping the reference here is what prevents that.
    /// </remarks>
    private readonly WndProc _windowProcedure;

    private readonly nint _classNamePointer;
    private readonly ushort _classAtom;
    private bool _disposed;

    /// <summary>Creates the window and registers its class.</summary>
    /// <exception cref="InvalidOperationException">The window class or window could not be created.</exception>
    public MessageWindow(ILogger<MessageWindow> logger)
    {
        _logger = logger;
        _windowProcedure = HandleMessage;

        // The class name includes the process id: two Cayrast processes (for example
        // during an update handover) must not collide on a per-process class atom.
        var className = $"Cayrast.MessageWindow.{Environment.ProcessId}";
        _classNamePointer = Marshal.StringToHGlobalUni(className);

        var moduleHandle = NativeMethods.GetModuleHandle(null);
        var windowClass = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_windowProcedure),
            hInstance = moduleHandle,
            lpszClassName = _classNamePointer,
        };

        _classAtom = NativeMethods.RegisterClassEx(in windowClass);
        if (_classAtom == 0)
        {
            var error = Marshal.GetLastWin32Error();
            Marshal.FreeHGlobal(_classNamePointer);
            throw new InvalidOperationException($"Failed to register the message window class (Win32 error {error}).");
        }

        Handle = NativeMethods.CreateWindowEx(
            dwExStyle: 0,
            lpClassName: _classNamePointer,
            lpWindowName: "Cayrast",
            dwStyle: Win32.WS_OVERLAPPED,
            x: 0, y: 0, nWidth: 0, nHeight: 0,
            hWndParent: NativeMethods.HWND_MESSAGE,
            hMenu: 0,
            hInstance: moduleHandle,
            lpParam: 0);

        if (Handle == 0)
        {
            var error = Marshal.GetLastWin32Error();
            NativeMethods.UnregisterClass(_classNamePointer, moduleHandle);
            Marshal.FreeHGlobal(_classNamePointer);
            throw new InvalidOperationException($"Failed to create the message window (Win32 error {error}).");
        }

        _logger.LogDebug("Message window created (handle {Handle:X}).", Handle);
    }

    /// <summary>The window handle other services register against.</summary>
    public nint Handle { get; private set; }

    /// <summary>Raised for every message the window receives.</summary>
    /// <remarks>
    /// Handlers run on the UI thread and must return quickly — blocking here stalls
    /// the entire message pump, freezing the application.
    /// </remarks>
    public event EventHandler<WindowMessageEventArgs>? MessageReceived;

    private nint HandleMessage(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        var args = new WindowMessageEventArgs(msg, wParam, lParam);

        try
        {
            MessageReceived?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            // An exception must never propagate across the native boundary: the CLR
            // cannot unwind through the Win32 dispatcher and the process would die.
            // Log it and let Windows handle the message normally.
            _logger.LogError(ex, "Unhandled exception while processing window message 0x{Message:X}.", msg);
            args.Handled = false;
        }

        return args.Handled
            ? args.Result
            : NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (Handle != 0)
        {
            NativeMethods.DestroyWindow(Handle);
            Handle = 0;
        }

        if (_classAtom != 0)
        {
            NativeMethods.UnregisterClass(_classNamePointer, NativeMethods.GetModuleHandle(null));
        }

        Marshal.FreeHGlobal(_classNamePointer);
        _logger.LogDebug("Message window destroyed.");
    }
}
