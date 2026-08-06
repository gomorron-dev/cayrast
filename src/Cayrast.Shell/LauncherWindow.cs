using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Cayrast.Abstractions;
using Cayrast.Abstractions.Platform;
using Cayrast.Core.Settings;
using Cayrast.Core.Storage;
using Cayrast.Shell.Bridge;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

// WPF 10 added Window.ThemeMode, which shadows our settings enum inside this type.
// Aliasing keeps the reference unambiguous without renaming a public settings type.
using CayrastThemeMode = Cayrast.Core.Settings.ThemeMode;

namespace Cayrast.Shell;

/// <summary>
/// The launcher window: a borderless WPF host containing a single WebView2.
/// </summary>
/// <remarks>
/// <para>
/// <b>This window is created once and never destroyed.</b> It is built hidden during
/// startup and thereafter only shown and hidden. Constructing a WebView2 costs on the
/// order of a hundred milliseconds — imperceptible once at sign-in, unacceptable on
/// every press of the hotkey. Everything else in this class follows from that choice.
/// </para>
/// <para>
/// WPF is a window host here and nothing more. There is no XAML view layer: all
/// interface lives in the web frontend, which is what makes themes, module UIs, and
/// community contribution tractable.
/// </para>
/// </remarks>
public sealed class LauncherWindow : Window, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly ICayrastPaths _paths;
    private readonly IWindowEffects _windowEffects;
    private readonly IMonitorService _monitors;
    private readonly WebMessageBridge _bridge;
    private readonly ILogger<LauncherWindow> _logger;
    private readonly WebView2 _webView = new();

    /// <summary>
    /// How long after showing to ignore deactivation.
    /// </summary>
    /// <remarks>
    /// Taking the foreground away from another application is not instantaneous, and
    /// during the handover Windows can briefly report this window as deactivated.
    /// Acting on that would hide the launcher in the same frame it appeared, which
    /// reads to the user as the hotkey flashing something on screen and losing it.
    /// Long enough to cover the transition, short enough that a genuine click away
    /// still dismisses immediately.
    /// </remarks>
    private static readonly TimeSpan ActivationGrace = TimeSpan.FromMilliseconds(350);

    private nint _handle;
    private bool _webViewReady;
    private long _shownAtTicks;

    /// <summary>Creates the window. Call <see cref="WarmUpAsync"/> before showing it.</summary>
    public LauncherWindow(
        ISettingsService settings,
        ICayrastPaths paths,
        IWindowEffects windowEffects,
        IMonitorService monitors,
        WebMessageBridge bridge,
        ILogger<LauncherWindow> logger)
    {
        _settings = settings;
        _paths = paths;
        _windowEffects = windowEffects;
        _monitors = monitors;
        _bridge = bridge;
        _logger = logger;

        ConfigureWindow();
        Content = _webView;
    }

    private void ConfigureWindow()
    {
        Title = CayrastBrand.ProductName;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = true;

        // Deliberately NOT AllowsTransparency. WPF's layered-window transparency
        // forces software rendering, which makes WebView2 render incorrectly and
        // destroys scrolling performance. Translucency comes from the DWM backdrop
        // applied to the native window instead, which stays hardware accelerated.
        AllowsTransparency = false;
        Background = Brushes.Transparent;

        // Sized in device pixels during Show(); these are only a sane starting point
        // for the hidden warm-up instance.
        var appearance = _settings.Current.Appearance;
        Width = appearance.PanelWidth;
        Height = appearance.PanelMaxHeight;
    }

    /// <summary>
    /// Builds the WebView2 and loads the frontend, without showing the window.
    /// </summary>
    /// <remarks>
    /// Runs during startup. When it completes the window is fully rendered and merely
    /// hidden, so the first hotkey press is a <c>ShowWindow</c> call rather than a cold
    /// browser initialisation.
    /// </remarks>
    public async Task WarmUpAsync(CancellationToken cancellationToken = default)
    {
        // A hidden window still needs to be realised for its HWND to exist and for
        // WebView2 to have something to attach to.
        Show();
        Hide();

        _handle = new WindowInteropHelper(this).EnsureHandle();
        _windowEffects.ExcludeFromAltTab(_handle);
        ApplyAppearance();

        try
        {
            await InitialiseWebViewAsync(cancellationToken);
            _webViewReady = true;
            _logger.LogInformation("Launcher window warmed up and ready.");
        }
        catch (Exception ex)
        {
            // Without a WebView there is no interface at all. Report it rather than
            // leaving the user with a hotkey that appears to do nothing.
            _logger.LogCritical(ex, "Failed to initialise WebView2. The launcher cannot display its interface.");
            throw;
        }
    }

    private async Task InitialiseWebViewAsync(CancellationToken cancellationToken)
    {
        // The user-data folder must be explicit and per-user: the default sits beside
        // the executable, which is unwritable for an all-users installation.
        Directory.CreateDirectory(_paths.WebViewData);

        var environment = await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: _paths.WebViewData);

        cancellationToken.ThrowIfCancellationRequested();
        await _webView.EnsureCoreWebView2Async(environment);

        var core = _webView.CoreWebView2;
        ConfigureWebViewSettings(core);

        var uiRoot = UiAssets.ResolveRoot();
        if (uiRoot is null)
        {
            throw new InvalidOperationException(
                "The Cayrast frontend build was not found. Run 'npm install && npm run build' in ui/shell.");
        }

        // Serve the UI from a real https origin rather than file://. This gives the
        // frontend a stable origin with working CSP and storage semantics, and is what
        // allows each module UI to later get its own isolated origin.
        core.SetVirtualHostNameToFolderMapping(
            CayrastBrand.ShellVirtualHost,
            uiRoot,
            CoreWebView2HostResourceAccessKind.Allow);

        _bridge.Attach(core);

        // Transparent so the DWM backdrop shows through the page.
        _webView.DefaultBackgroundColor = System.Drawing.Color.Transparent;

        core.Navigate($"https://{CayrastBrand.ShellVirtualHost}/index.html");
        _logger.LogDebug("Frontend served from {Path}.", uiRoot);
    }

    private void ConfigureWebViewSettings(CoreWebView2 core)
    {
        var settings = core.Settings;

        // This is an application surface, not a browser. Every affordance that would
        // reveal the WebView underneath is switched off.
        settings.AreDefaultContextMenusEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.IsZoomControlEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.IsSwipeNavigationEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        settings.IsGeneralAutofillEnabled = false;

        // DevTools stay available: this is an open-source project whose users are
        // encouraged to write themes and modules, and the inspector is how they do it.
        settings.AreDevToolsEnabled = true;

        // Nothing in the shell UI should ever open a browser window. Any attempt is a
        // bug or an attack, so refuse it and record it.
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            _logger.LogWarning("Blocked an attempt to open a new window for {Uri}.", e.Uri);
        };
    }

    /// <summary>Applies appearance settings to the native window.</summary>
    public void ApplyAppearance()
    {
        if (_handle == 0)
        {
            return;
        }

        var appearance = _settings.Current.Appearance;

        // Acrylic rather than Mica: Acrylic is what Windows itself uses for transient
        // command surfaces, and the launcher is exactly that. Mica is for windows that
        // stay on screen.
        _windowEffects.ApplyBackdrop(_handle, WindowBackdrop.Acrylic);
        _windowEffects.ApplyRoundedCorners(_handle, appearance.BorderRadius > 0);
        _windowEffects.ApplyDarkMode(_handle, appearance.Theme != CayrastThemeMode.Light);
    }

    /// <summary>Shows the launcher, positioned on the appropriate monitor and focused.</summary>
    public void ShowLauncher()
    {
        if (!_webViewReady)
        {
            _logger.LogWarning("Hotkey pressed before the interface finished loading; ignoring.");
            return;
        }

        PositionOnActiveMonitor();

        // Recorded before Show() so the grace window covers the whole activation
        // sequence, including the foreground handover below.
        _shownAtTicks = Environment.TickCount64;

        Show();
        Activate();

        // WPF's Activate() is unreliable when another application owns the foreground.
        // The launcher is the legitimate exception to the focus-stealing rules — the
        // user just pressed its hotkey — so foreground is taken deliberately.
        _windowEffects.ForceForeground(_handle);

        // Focus must reach the WebView or keystrokes go nowhere, which presents as the
        // launcher opening but ignoring typing.
        _webView.Focus();
        _bridge.PublishEvent("app.shown", null);
        _logger.LogDebug("Launcher shown (visible: {IsVisible}).", IsVisible);
    }

    /// <summary>Hides the launcher.</summary>
    public void HideLauncher()
    {
        if (!IsVisible)
        {
            return;
        }

        Hide();
        _bridge.PublishEvent("app.hidden", null);
    }

    /// <summary>Shows the launcher if hidden, hides it if visible.</summary>
    public void ToggleLauncher()
    {
        if (IsVisible)
        {
            HideLauncher();
        }
        else
        {
            ShowLauncher();
        }
    }

    private void PositionOnActiveMonitor()
    {
        var behavior = _settings.Current.Behavior;
        var appearance = _settings.Current.Appearance;

        var monitor = behavior.ShowOnActiveMonitor
            ? _monitors.GetMonitorUnderCursor()
            : _monitors.GetMonitorForWindow(_handle);

        // Settings are authored in logical pixels so they mean the same thing on every
        // display; scale to physical pixels for the target monitor specifically.
        var scale = monitor.Scale * appearance.UiScale;
        var width = (int)(appearance.PanelWidth * scale);
        var height = (int)(appearance.PanelMaxHeight * scale);

        // Never exceed the monitor: a panel wider than the screen is unusable, and on
        // a small laptop display the default width can genuinely be too large.
        width = Math.Min(width, monitor.WorkAreaWidth);
        height = Math.Min(height, monitor.WorkAreaHeight);

        var (x, y) = appearance.DockPosition switch
        {
            DockPosition.Top => (Centered(monitor.WorkAreaX, monitor.WorkAreaWidth, width), monitor.WorkAreaY + (int)(48 * scale)),
            DockPosition.Bottom => (Centered(monitor.WorkAreaX, monitor.WorkAreaWidth, width), monitor.WorkAreaY + monitor.WorkAreaHeight - height - (int)(48 * scale)),
            DockPosition.Left => (monitor.WorkAreaX + (int)(48 * scale), Centered(monitor.WorkAreaY, monitor.WorkAreaHeight, height)),
            DockPosition.Right => (monitor.WorkAreaX + monitor.WorkAreaWidth - width - (int)(48 * scale), Centered(monitor.WorkAreaY, monitor.WorkAreaHeight, height)),

            // Centre horizontally but sit above the vertical midpoint: that is where
            // the eye already rests, and it leaves room for results to grow downwards
            // without the panel drifting off the bottom of the screen.
            _ => (Centered(monitor.WorkAreaX, monitor.WorkAreaWidth, width),
                  monitor.WorkAreaY + (int)((monitor.WorkAreaHeight - height) * 0.32)),
        };

        _windowEffects.SetBounds(_handle, x, y, width, height);
    }

    private static int Centered(int origin, int available, int size) => origin + ((available - size) / 2);

    /// <inheritdoc />
    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);

        if (!_settings.Current.Behavior.HideOnFocusLoss)
        {
            return;
        }

        // Windows can report a spurious deactivation while the foreground is still
        // being handed over to us. Without this guard the launcher appears and
        // disappears in the same instant, which looks like the hotkey is broken.
        if (Environment.TickCount64 - _shownAtTicks < ActivationGrace.TotalMilliseconds)
        {
            _logger.LogDebug("Ignoring deactivation during the activation grace period.");
            return;
        }

        HideLauncher();
    }

    /// <summary>Tears down the WebView2 and its browser process.</summary>
    /// <remarks>
    /// WPF's <see cref="Window"/> is not disposable, but the WebView2 it hosts owns an
    /// out-of-process browser. Without an explicit dispose that process can outlive
    /// ours, leaving an orphaned msedgewebview2.exe behind after every run.
    /// </remarks>
    public void Dispose() => _webView.Dispose();

    /// <inheritdoc />
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // The window outlives every close attempt. Cayrast exits through the tray or
        // the quit command; closing the launcher only dismisses it, and destroying the
        // window here would throw away the warm WebView that makes it fast.
        e.Cancel = true;
        HideLauncher();
        base.OnClosing(e);
    }
}
