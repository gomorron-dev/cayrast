using System.Runtime.InteropServices;

namespace Cayrast.Platform.Windows.Interop;

/// <summary>
/// Every P/Invoke declaration in Cayrast.
/// </summary>
/// <remarks>
/// <para>
/// Centralised so the native surface can be audited in one place. Nothing outside
/// <c>Cayrast.Platform.Windows</c> may declare an import; if you need one, add it here
/// and expose a managed service around it.
/// </para>
/// <para>
/// <see cref="LibraryImportAttribute"/> is used throughout — it source-generates the
/// marshalling stub at compile time rather than emitting IL at runtime, which is both
/// faster and trim/AOT-safe. Structs are laid out to be blittable so the generator does
/// not have to synthesise conversions.
/// </para>
/// </remarks>
internal static partial class NativeMethods
{
    private const string User32 = "user32.dll";
    private const string Kernel32 = "kernel32.dll";
    private const string Shell32 = "shell32.dll";
    private const string DwmApi = "dwmapi.dll";

    // ---------------------------------------------------------------- Window class

    [LibraryImport(User32, EntryPoint = "RegisterClassExW", SetLastError = true)]
    internal static partial ushort RegisterClassEx(in WNDCLASSEXW lpwcx);

    [LibraryImport(User32, EntryPoint = "UnregisterClassW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnregisterClass(nint lpClassName, nint hInstance);

    [LibraryImport(User32, EntryPoint = "CreateWindowExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint CreateWindowEx(
        uint dwExStyle,
        nint lpClassName,
        string? lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        nint hWndParent,
        nint hMenu,
        nint hInstance,
        nint lpParam);

    [LibraryImport(User32, EntryPoint = "DestroyWindow", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyWindow(nint hWnd);

    [LibraryImport(User32, EntryPoint = "DefWindowProcW")]
    internal static partial nint DefWindowProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [LibraryImport(Kernel32, EntryPoint = "GetModuleHandleW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint GetModuleHandle(string? lpModuleName);

    /// <summary>Special parent HWND that creates a message-only window.</summary>
    /// <remarks>
    /// A message-only window is never visible, never appears in the task bar or
    /// Alt+Tab, and receives no input — but it does receive posted messages. It is
    /// exactly the right host for a global hotkey and a tray icon.
    /// </remarks>
    internal static readonly nint HWND_MESSAGE = -3;

    // ---------------------------------------------------------------- Hotkeys

    [LibraryImport(User32, EntryPoint = "RegisterHotKey", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport(User32, EntryPoint = "UnregisterHotKey", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnregisterHotKey(nint hWnd, int id);

    // ---------------------------------------------------------------- Tray icon

    [LibraryImport(Shell32, EntryPoint = "Shell_NotifyIconW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATAW lpData);

    [LibraryImport(User32, EntryPoint = "LoadImageW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint LoadImage(nint hInst, string name, uint type, int cx, int cy, uint fuLoad);

    [LibraryImport(User32, EntryPoint = "DestroyIcon", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyIcon(nint hIcon);

    /// <summary>Extracts an icon embedded in an executable.</summary>
    /// <remarks>
    /// Used to give the tray icon the application's own icon without shipping a
    /// separate .ico beside the executable, where it could go missing.
    /// </remarks>
    [LibraryImport(Shell32, EntryPoint = "ExtractIconW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint ExtractIcon(nint hInst, string pszExeFileName, uint nIconIndex);

    [LibraryImport(User32, EntryPoint = "LoadIconW", SetLastError = true)]
    internal static partial nint LoadIcon(nint hInstance, nint lpIconName);

    /// <summary>Predefined application icon, used as a last-resort tray fallback.</summary>
    internal static readonly nint IDI_APPLICATION = 32512;

    [LibraryImport(User32, EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint RegisterWindowMessage(string lpString);

    // ---------------------------------------------------------------- Tray menu

    [LibraryImport(User32, EntryPoint = "CreatePopupMenu", SetLastError = true)]
    internal static partial nint CreatePopupMenu();

    [LibraryImport(User32, EntryPoint = "DestroyMenu", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyMenu(nint hMenu);

    [LibraryImport(User32, EntryPoint = "AppendMenuW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AppendMenu(nint hMenu, uint uFlags, nuint uIDNewItem, string? lpNewItem);

    [LibraryImport(User32, EntryPoint = "TrackPopupMenuEx", SetLastError = true)]
    internal static partial int TrackPopupMenuEx(nint hMenu, uint uFlags, int x, int y, nint hwnd, nint lptpm);

    [LibraryImport(User32, EntryPoint = "GetCursorPos", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetCursorPos(out POINT lpPoint);

    [LibraryImport(User32, EntryPoint = "SetForegroundWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetForegroundWindow(nint hWnd);

    [LibraryImport(User32, EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    // ---------------------------------------------------------------- Window appearance

    [LibraryImport(DwmApi, EntryPoint = "DwmSetWindowAttribute")]
    internal static partial int DwmSetWindowAttribute(nint hwnd, uint dwAttribute, in int pvAttribute, uint cbAttribute);

    [LibraryImport(DwmApi, EntryPoint = "DwmExtendFrameIntoClientArea")]
    internal static partial int DwmExtendFrameIntoClientArea(nint hwnd, in MARGINS pMarInset);

    // ---------------------------------------------------------------- Job objects

    [LibraryImport(Kernel32, EntryPoint = "CreateJobObjectW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint CreateJobObject(nint lpJobAttributes, string? lpName);

    [LibraryImport(Kernel32, EntryPoint = "SetInformationJobObject", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetInformationJobObject(
        nint hJob, int jobObjectInformationClass, nint lpJobObjectInformation, uint cbJobObjectInformationLength);

    [LibraryImport(Kernel32, EntryPoint = "AssignProcessToJobObject", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AssignProcessToJobObject(nint hJob, nint hProcess);

    [LibraryImport(Kernel32, EntryPoint = "GetCurrentProcess")]
    internal static partial nint GetCurrentProcess();

    [LibraryImport(Kernel32, EntryPoint = "CloseHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(nint hObject);

    /// <summary>JobObjectExtendedLimitInformation.</summary>
    internal const int JobObjectExtendedLimitInformation = 9;

    /// <summary>Terminates every process in the job when the last handle to it closes.</summary>
    internal const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;

    // ---------------------------------------------------------------- Monitors

    [LibraryImport(User32, EntryPoint = "MonitorFromPoint")]
    internal static partial nint MonitorFromPoint(POINT pt, uint dwFlags);

    [LibraryImport(User32, EntryPoint = "MonitorFromWindow")]
    internal static partial nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [LibraryImport(User32, EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);

    /// <summary>Per-monitor DPI. Requires PerMonitorV2 awareness, declared in app.manifest.</summary>
    [LibraryImport(User32, EntryPoint = "GetDpiForWindow")]
    internal static partial uint GetDpiForWindow(nint hwnd);

    [LibraryImport(User32, EntryPoint = "SetWindowPos", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    /// <summary>Fall back to the nearest monitor rather than failing when a point is off-screen.</summary>
    internal const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    // ---------------------------------------------------------------- Window styles

    [LibraryImport(User32, EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    internal static partial nint GetWindowLongPtr(nint hWnd, int nIndex);

    [LibraryImport(User32, EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    internal static partial nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    /// <summary>Index of the extended window style.</summary>
    internal const int GWL_EXSTYLE = -20;

    /// <summary>
    /// Marks a window as a tool window, which removes it from Alt+Tab.
    /// </summary>
    /// <remarks>
    /// <c>ShowInTaskbar = false</c> alone does not do this. Without the tool-window
    /// style the launcher appears as an Alt+Tab entry, which is wrong for a transient
    /// overlay and actively annoying when cycling windows.
    /// </remarks>
    internal const int WS_EX_TOOLWINDOW = 0x00000080;

    // SetWindowPos flags
    internal const uint SWP_NOZORDER = 0x0004;
    internal const uint SWP_NOACTIVATE = 0x0010;
    internal const uint SWP_SHOWWINDOW = 0x0040;

    // ---------------------------------------------------------------- Focus stealing

    [LibraryImport(User32, EntryPoint = "GetForegroundWindow")]
    internal static partial nint GetForegroundWindow();

    [LibraryImport(User32, EntryPoint = "GetWindowThreadProcessId")]
    internal static partial uint GetWindowThreadProcessId(nint hWnd, nint lpdwProcessId);

    [LibraryImport(Kernel32, EntryPoint = "GetCurrentThreadId")]
    internal static partial uint GetCurrentThreadId();

    [LibraryImport(User32, EntryPoint = "AttachThreadInput")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);
}
