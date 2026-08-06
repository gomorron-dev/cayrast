using System.Runtime.InteropServices;
using Cayrast.Abstractions;
using Cayrast.Abstractions.Platform;
using Cayrast.Platform.Windows.Interop;
using Microsoft.Extensions.Logging;

namespace Cayrast.Platform.Windows;

/// <summary>
/// The system tray icon, via <c>Shell_NotifyIcon</c>.
/// </summary>
/// <remarks>
/// Uses <c>NOTIFYICON_VERSION_4</c>, which changes how click notifications are packed:
/// the cursor position arrives in <c>wParam</c> and the event id in the low word of
/// <c>lParam</c>. Version 4 is worth the different unpacking because it delivers
/// reliable screen coordinates, which the older protocol does not.
/// </remarks>
public sealed class TrayIconService : ITrayIconService, IDisposable
{
    private const uint IconId = 1;

    private readonly MessageWindow _messageWindow;
    private readonly ILogger<TrayIconService> _logger;
    private readonly uint _taskbarCreatedMessage;

    private IReadOnlyList<(string Id, string Label)> _menuItems = [];
    private string _tooltip = CayrastBrand.ProductName;
    private nint _iconHandle;
    private bool _visible;
    private bool _disposed;

    /// <summary>Creates the service and begins listening for tray messages.</summary>
    public TrayIconService(MessageWindow messageWindow, ILogger<TrayIconService> logger)
    {
        _messageWindow = messageWindow;
        _logger = logger;

        // Explorer broadcasts this when it restarts. Without re-adding the icon the
        // tray entry disappears permanently and the app becomes unreachable for any
        // user who relies on it — a genuinely common failure that looks like a crash.
        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");

        _messageWindow.MessageReceived += OnMessageReceived;
    }

    /// <inheritdoc />
    public event EventHandler? Activated;

    /// <inheritdoc />
    public event EventHandler<string>? MenuItemInvoked;

    /// <inheritdoc />
    public void Show(string tooltip, IReadOnlyList<(string Id, string Label)> menuItems)
    {
        _tooltip = tooltip;
        _menuItems = menuItems;
        _iconHandle = LoadApplicationIcon();

        if (AddOrModify(Win32.NIM_ADD))
        {
            _visible = true;
            SetVersion();
            _logger.LogInformation("Tray icon shown.");
        }
    }

    /// <inheritdoc />
    public void Hide()
    {
        if (!_visible)
        {
            return;
        }

        var data = CreateData(Win32.NIF_ICON);
        NativeMethods.Shell_NotifyIcon(Win32.NIM_DELETE, ref data);
        _visible = false;
        _logger.LogDebug("Tray icon hidden.");
    }

    private unsafe NOTIFYICONDATAW CreateData(uint flags)
    {
        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _messageWindow.Handle,
            uID = IconId,
            uFlags = flags,
            uCallbackMessage = Win32.WM_TRAYICON,
            hIcon = _iconHandle,
            uVersion = Win32.NOTIFYICON_VERSION_4,
        };

        NOTIFYICONDATAW.SetFixedString(data.szTip, 128, _tooltip);
        return data;
    }

    private bool AddOrModify(uint message)
    {
        var data = CreateData(Win32.NIF_MESSAGE | Win32.NIF_ICON | Win32.NIF_TIP);
        if (NativeMethods.Shell_NotifyIcon(message, ref data))
        {
            return true;
        }

        _logger.LogWarning(
            "Shell_NotifyIcon({Message}) failed with Win32 error {Error}.",
            message, Marshal.GetLastWin32Error());
        return false;
    }

    private void SetVersion()
    {
        var data = CreateData(0);
        NativeMethods.Shell_NotifyIcon(Win32.NIM_SETVERSION, ref data);
    }

    private nint LoadApplicationIcon()
    {
        // Prefer the icon embedded in our own executable so the tray matches the
        // Start Menu entry without shipping a loose .ico that could go missing.
        var executablePath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(executablePath))
        {
            var handle = NativeMethods.ExtractIcon(NativeMethods.GetModuleHandle(null), executablePath, 0);

            // ExtractIcon returns 1 (not 0) to mean "no icons in this file".
            if (handle != 0 && handle != 1)
            {
                return handle;
            }
        }

        _logger.LogDebug("No embedded application icon found; using the system default.");
        return NativeMethods.LoadIcon(0, NativeMethods.IDI_APPLICATION);
    }

    private void OnMessageReceived(object? sender, WindowMessageEventArgs e)
    {
        if (e.Message == _taskbarCreatedMessage && _visible)
        {
            _logger.LogInformation("Explorer restarted; restoring the tray icon.");
            AddOrModify(Win32.NIM_ADD);
            SetVersion();
            return;
        }

        if (e.Message == Win32.WM_TRAYICON)
        {
            // Version 4 packing: lParam low word is the event, wParam holds the cursor.
            var notification = (uint)(e.LParam & 0xFFFF);
            var x = (short)(e.WParam & 0xFFFF);
            var y = (short)((e.WParam >> 16) & 0xFFFF);

            switch (notification)
            {
                case Win32.WM_LBUTTONUP:
                case Win32.WM_LBUTTONDBLCLK:
                    e.Handled = true;
                    Activated?.Invoke(this, EventArgs.Empty);
                    break;

                case Win32.WM_RBUTTONUP:
                    e.Handled = true;
                    ShowContextMenu(x, y);
                    break;
            }
        }
        else if (e.Message == Win32.WM_COMMAND)
        {
            var commandId = (int)(e.WParam & 0xFFFF);
            if (commandId > 0 && commandId <= _menuItems.Count)
            {
                e.Handled = true;
                MenuItemInvoked?.Invoke(this, _menuItems[commandId - 1].Id);
            }
        }
    }

    private void ShowContextMenu(int x, int y)
    {
        if (_menuItems.Count == 0)
        {
            return;
        }

        var menu = NativeMethods.CreatePopupMenu();
        if (menu == 0)
        {
            return;
        }

        try
        {
            for (var i = 0; i < _menuItems.Count; i++)
            {
                var (_, label) = _menuItems[i];

                // Menu command ids are 1-based: TrackPopupMenuEx returns 0 for
                // "dismissed without choosing", so 0 cannot also mean the first item.
                if (string.IsNullOrEmpty(label))
                {
                    NativeMethods.AppendMenu(menu, Win32.MF_SEPARATOR, 0, null);
                }
                else
                {
                    NativeMethods.AppendMenu(menu, Win32.MF_STRING, (nuint)(i + 1), label);
                }
            }

            // Documented requirement: without foreground activation the menu will not
            // dismiss when the user clicks elsewhere, leaving it stuck on screen.
            NativeMethods.SetForegroundWindow(_messageWindow.Handle);

            var selected = NativeMethods.TrackPopupMenuEx(
                menu,
                Win32.TPM_RIGHTBUTTON | Win32.TPM_RETURNCMD | Win32.TPM_NONOTIFY,
                x, y,
                _messageWindow.Handle,
                0);

            if (selected > 0 && selected <= _menuItems.Count)
            {
                MenuItemInvoked?.Invoke(this, _menuItems[selected - 1].Id);
            }
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _messageWindow.MessageReceived -= OnMessageReceived;

        // Removing the icon before exit matters: an icon whose process has died
        // lingers as a ghost until the user happens to hover over it.
        Hide();

        if (_iconHandle != 0)
        {
            NativeMethods.DestroyIcon(_iconHandle);
            _iconHandle = 0;
        }
    }
}
