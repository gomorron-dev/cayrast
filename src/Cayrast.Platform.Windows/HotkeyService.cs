using System.Runtime.InteropServices;
using Cayrast.Abstractions.Input;
using Cayrast.Abstractions.Platform;
using Cayrast.Platform.Windows.Interop;
using Microsoft.Extensions.Logging;

namespace Cayrast.Platform.Windows;

/// <summary>
/// Global hotkeys via Win32 <c>RegisterHotKey</c>.
/// </summary>
/// <remarks>
/// Registration is process-exclusive system-wide, so failure is a normal outcome
/// rather than an error: another application may already own the combination. Callers
/// get <see langword="false"/> and are expected to tell the user, not to retry.
/// </remarks>
public sealed class HotkeyService : IHotkeyService, IDisposable
{
    private readonly MessageWindow _messageWindow;
    private readonly ILogger<HotkeyService> _logger;
    private readonly Dictionary<string, Registration> _byId = [];
    private readonly Dictionary<int, Action> _byNativeId = [];
    private int _nextNativeId = 1;
    private bool _disposed;

    /// <summary>Creates the service and begins listening for hotkey messages.</summary>
    public HotkeyService(MessageWindow messageWindow, ILogger<HotkeyService> logger)
    {
        _messageWindow = messageWindow;
        _logger = logger;
        _messageWindow.MessageReceived += OnMessageReceived;
    }

    /// <inheritdoc />
    public bool TryRegister(string id, HotkeyBinding binding, Action callback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(callback);

        if (!binding.IsValid)
        {
            _logger.LogWarning("Refusing to register hotkey '{Id}': {Binding} has no modifier.", id, binding);
            return false;
        }

        // Re-registering the same id replaces the old binding, which is what the
        // settings UI needs when the user changes a hotkey.
        Unregister(id);

        var nativeId = _nextNativeId++;
        var modifiers = ToNativeModifiers(binding.Modifiers) | Win32.MOD_NOREPEAT;

        if (!NativeMethods.RegisterHotKey(_messageWindow.Handle, nativeId, modifiers, binding.VirtualKey))
        {
            var error = Marshal.GetLastWin32Error();
            _logger.LogWarning(
                "Could not register hotkey {Binding} for '{Id}' (Win32 error {Error}). Another application likely owns it.",
                binding, id, error);
            return false;
        }

        _byId[id] = new Registration(nativeId, binding);
        _byNativeId[nativeId] = callback;
        _logger.LogInformation("Registered hotkey {Binding} for '{Id}'.", binding, id);
        return true;
    }

    /// <inheritdoc />
    public void Unregister(string id)
    {
        if (!_byId.Remove(id, out var registration))
        {
            return;
        }

        NativeMethods.UnregisterHotKey(_messageWindow.Handle, registration.NativeId);
        _byNativeId.Remove(registration.NativeId);
        _logger.LogDebug("Unregistered hotkey {Binding} for '{Id}'.", registration.Binding, id);
    }

    private void OnMessageReceived(object? sender, WindowMessageEventArgs e)
    {
        if (e.Message != Win32.WM_HOTKEY)
        {
            return;
        }

        // wParam carries the id passed to RegisterHotKey.
        if (!_byNativeId.TryGetValue((int)e.WParam, out var callback))
        {
            return;
        }

        e.Handled = true;

        try
        {
            callback();
        }
        catch (Exception ex)
        {
            // A failing callback must not take down the message pump, and must not
            // leave the hotkey silently dead for the rest of the session.
            _logger.LogError(ex, "Hotkey callback threw.");
        }
    }

    private static uint ToNativeModifiers(HotkeyModifiers modifiers)
    {
        uint result = 0;
        if (modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            result |= Win32.MOD_ALT;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Control))
        {
            result |= Win32.MOD_CONTROL;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            result |= Win32.MOD_SHIFT;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Windows))
        {
            result |= Win32.MOD_WIN;
        }

        return result;
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

        foreach (var registration in _byId.Values)
        {
            NativeMethods.UnregisterHotKey(_messageWindow.Handle, registration.NativeId);
        }

        _byId.Clear();
        _byNativeId.Clear();
    }

    private readonly record struct Registration(int NativeId, HotkeyBinding Binding);
}
