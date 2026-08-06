using System.Runtime.InteropServices;

namespace Cayrast.Platform.Windows.Interop;

/// <summary>Window procedure callback.</summary>
/// <remarks>
/// Instances handed to Win32 must be kept alive by a managed field for as long as the
/// window exists. If the delegate is collected while Windows still holds the pointer,
/// the next message dispatched to it crashes the process — and it will do so rarely
/// and unreproducibly, which makes it one of the nastier interop bugs to diagnose.
/// </remarks>
internal delegate nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam);

/// <summary>Window class registration. Matches the Win32 <c>WNDCLASSEXW</c>.</summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct WNDCLASSEXW
{
    public uint cbSize;
    public uint style;
    public nint lpfnWndProc;
    public int cbClsExtra;
    public int cbWndExtra;
    public nint hInstance;
    public nint hIcon;
    public nint hCursor;
    public nint hbrBackground;
    public nint lpszMenuName;
    public nint lpszClassName;
    public nint hIconSm;
}

/// <summary>A screen coordinate pair.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct POINT
{
    public int X;
    public int Y;
}

/// <summary>A rectangle in device pixels.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    public readonly int Width => Right - Left;

    public readonly int Height => Bottom - Top;
}

/// <summary>Monitor geometry. Matches <c>MONITORINFO</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct MONITORINFO
{
    public uint cbSize;

    /// <summary>Full monitor bounds.</summary>
    public RECT rcMonitor;

    /// <summary>Bounds excluding the taskbar and any docked app bars.</summary>
    public RECT rcWork;

    public uint dwFlags;
}

/// <summary>Frame extension margins for <c>DwmExtendFrameIntoClientArea</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct MARGINS
{
    public int Left;
    public int Right;
    public int Top;
    public int Bottom;
}

/// <summary>
/// Tray icon data. Matches <c>NOTIFYICONDATAW</c>.
/// </summary>
/// <remarks>
/// Uses inline fixed buffers rather than marshalled strings so the struct stays
/// blittable and <see cref="LibraryImportAttribute"/> can generate a direct call with
/// no allocation. The sizes are fixed by the Win32 contract and must not be changed.
/// </remarks>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal unsafe struct NOTIFYICONDATAW
{
    public uint cbSize;
    public nint hWnd;
    public uint uID;
    public uint uFlags;
    public uint uCallbackMessage;
    public nint hIcon;
    public fixed char szTip[128];
    public uint dwState;
    public uint dwStateMask;
    public fixed char szInfo[256];
    public uint uVersion;
    public fixed char szInfoTitle[64];
    public uint dwInfoFlags;
    public Guid guidItem;
    public nint hBalloonIcon;

    /// <summary>Copies text into a fixed buffer, truncating safely and NUL-terminating.</summary>
    /// <remarks>
    /// Win32 requires NUL termination and will read past the end of an unterminated
    /// buffer. Truncation is preferable to refusing a long tooltip.
    /// </remarks>
    public static void SetFixedString(char* destination, int capacity, string value)
    {
        var length = Math.Min(value.Length, capacity - 1);
        for (var i = 0; i < length; i++)
        {
            destination[i] = value[i];
        }

        destination[length] = '\0';
    }
}

/// <summary>Basic job limits. Matches <c>JOBOBJECT_BASIC_LIMIT_INFORMATION</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct JOBOBJECT_BASIC_LIMIT_INFORMATION
{
    public long PerProcessUserTimeLimit;
    public long PerJobUserTimeLimit;
    public uint LimitFlags;
    public nuint MinimumWorkingSetSize;
    public nuint MaximumWorkingSetSize;
    public uint ActiveProcessLimit;
    public nuint Affinity;
    public uint PriorityClass;
    public uint SchedulingClass;
}

/// <summary>I/O totals. Matches <c>IO_COUNTERS</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct IO_COUNTERS
{
    public ulong ReadOperationCount;
    public ulong WriteOperationCount;
    public ulong OtherOperationCount;
    public ulong ReadTransferCount;
    public ulong WriteTransferCount;
    public ulong OtherTransferCount;
}

/// <summary>Extended job limits. Matches <c>JOBOBJECT_EXTENDED_LIMIT_INFORMATION</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
{
    public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
    public IO_COUNTERS IoInfo;
    public nuint ProcessMemoryLimit;
    public nuint JobMemoryLimit;
    public nuint PeakProcessMemoryUsed;
    public nuint PeakJobMemoryUsed;
}

/// <summary>Win32 constants used by the platform services.</summary>
internal static class Win32
{
    // Window messages
    public const uint WM_DESTROY = 0x0002;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_COMMAND = 0x0111;
    public const uint WM_HOTKEY = 0x0312;
    public const uint WM_RBUTTONUP = 0x0205;
    public const uint WM_LBUTTONUP = 0x0202;
    public const uint WM_LBUTTONDBLCLK = 0x0203;
    public const uint WM_DPICHANGED = 0x02E0;
    public const uint WM_SETTINGCHANGE = 0x001A;

    /// <summary>First message id Cayrast uses for its own private notifications.</summary>
    public const uint WM_APP = 0x8000;

    /// <summary>Callback message the tray icon sends to our message window.</summary>
    public const uint WM_TRAYICON = WM_APP + 1;

    // Hotkey modifiers (MOD_*)
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;

    /// <summary>Suppresses auto-repeat while the hotkey is held down.</summary>
    /// <remarks>
    /// Without this, holding Alt+Space fires continuously and the launcher flickers
    /// open and shut as the toggle is hit dozens of times a second.
    /// </remarks>
    public const uint MOD_NOREPEAT = 0x4000;

    // Shell_NotifyIcon messages
    public const uint NIM_ADD = 0x00000000;
    public const uint NIM_MODIFY = 0x00000001;
    public const uint NIM_DELETE = 0x00000002;
    public const uint NIM_SETVERSION = 0x00000004;

    // NOTIFYICONDATA flags
    public const uint NIF_MESSAGE = 0x00000001;
    public const uint NIF_ICON = 0x00000002;
    public const uint NIF_TIP = 0x00000004;
    public const uint NIF_INFO = 0x00000010;

    public const uint NOTIFYICON_VERSION_4 = 4;

    // LoadImage
    public const uint IMAGE_ICON = 1;
    public const uint LR_LOADFROMFILE = 0x00000010;
    public const uint LR_DEFAULTSIZE = 0x00000040;
    public const uint LR_SHARED = 0x00008000;

    // Menu
    public const uint MF_STRING = 0x00000000;
    public const uint MF_SEPARATOR = 0x00000800;
    public const uint TPM_RIGHTBUTTON = 0x0002;
    public const uint TPM_RETURNCMD = 0x0100;
    public const uint TPM_NONOTIFY = 0x0080;

    // DWM attributes
    /// <summary>Controls window corner rounding (Windows 11 build 22000+).</summary>
    public const uint DWMWA_WINDOW_CORNER_PREFERENCE = 33;

    /// <summary>Selects the system backdrop: Mica, Acrylic, or none (Windows 11 22H2+).</summary>
    public const uint DWMWA_SYSTEMBACKDROP_TYPE = 38;

    /// <summary>Switches the title bar and frame to dark mode (Windows 10 2004+).</summary>
    public const uint DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    // DWM_WINDOW_CORNER_PREFERENCE
    public const int DWMWCP_DEFAULT = 0;
    public const int DWMWCP_DONOTROUND = 1;
    public const int DWMWCP_ROUND = 2;
    public const int DWMWCP_ROUNDSMALL = 3;

    // DWM_SYSTEMBACKDROP_TYPE
    public const int DWMSBT_AUTO = 0;
    public const int DWMSBT_NONE = 1;

    /// <summary>Mica — the opaque, wallpaper-tinted backdrop used by long-lived windows.</summary>
    public const int DWMSBT_MAINWINDOW = 2;

    /// <summary>Acrylic — the translucent, blurred backdrop used by transient surfaces.</summary>
    /// <remarks>
    /// The right choice for a launcher: Acrylic is what Windows itself uses for
    /// flyouts and command surfaces that appear over other content.
    /// </remarks>
    public const int DWMSBT_TRANSIENTWINDOW = 3;

    public const int DWMSBT_TABBEDWINDOW = 4;

    // Window styles
    public const uint WS_OVERLAPPED = 0x00000000;
    public const uint WS_POPUP = 0x80000000;
}
