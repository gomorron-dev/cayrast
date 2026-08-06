using System.Text.Json;
using System.Windows;
using Cayrast.Abstractions;
using Cayrast.Abstractions.Applications;
using Cayrast.Abstractions.Input;
using Cayrast.Abstractions.Platform;
using Cayrast.Abstractions.Search;
using Cayrast.Core.Commands;
using Cayrast.Core.Modules;
using Cayrast.Core.Search;
using Cayrast.Core.Settings;
using Cayrast.Platform.Windows;
using Cayrast.Shell.Bridge;
using Microsoft.Extensions.Logging;

namespace Cayrast.Shell;

/// <summary>
/// Wires the application together and owns its lifetime.
/// </summary>
/// <remarks>
/// Startup order is deliberate and load-bearing:
/// <list type="number">
///   <item>Settings load first — everything else reads them.</item>
///   <item>Bridge channels register before the frontend can call them, otherwise the
///   UI's first request races against handler registration.</item>
///   <item>The window warms up, paying the WebView2 construction cost once.</item>
///   <item>The hotkey registers last, so it cannot fire before there is a window to show.</item>
/// </list>
/// </remarks>
public sealed class CayrastHost(
    ISettingsService settings,
    IHotkeyService hotkeys,
    ITrayIconService trayIcon,
    SingleInstance singleInstance,
    WebMessageBridge bridge,
    LauncherWindow window,
    ISearchEngine searchEngine,
    ICommandEngine commandEngine,
    IFrecencyStore frecency,
    IApplicationIndex applicationIndex,
    ApplicationSearchProvider applicationProvider,
    SettingsSearchProvider settingsProvider,
    ISettingsRegistry settingsRegistry,
    IModuleRegistry moduleRegistry,
    SearchCoordinator searchCoordinator,
    ILogger<CayrastHost> logger) : IAsyncDisposable
{
    private const string LauncherHotkeyId = "launcher.toggle";

    /// <summary>Runs the full startup sequence.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await settings.LoadAsync(cancellationToken);
        await frecency.LoadAsync(cancellationToken);

        RegisterSearchAndCommands();
        RegisterBridgeChannels();
        searchCoordinator.RegisterChannels();
        ListenForSecondInstance();

        await window.WarmUpAsync(cancellationToken);

        RegisterHotkey();
        ShowTrayIcon();

        settings.Changed += OnSettingsChanged;
        logger.LogInformation("{Product} started.", CayrastBrand.ProductName);

        // Deliberately not awaited. Indexing applications takes a moment and the
        // launcher is fully usable without it — commands work immediately, and
        // applications appear as soon as the scan lands. Blocking startup on it would
        // delay sign-in for no benefit.
        _ = Task.Run(() => applicationIndex.InitializeAsync(CancellationToken.None), CancellationToken.None);

        // Likewise deferred. Modules are third-party code; discovering them must not be
        // able to delay the launcher becoming usable.
        _ = Task.Run(async () =>
        {
            try
            {
                await moduleRegistry.DiscoverAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Module discovery failed.");
            }
        }, CancellationToken.None);
    }

    private void RegisterSearchAndCommands()
    {
        BuiltInCommands.RegisterAll(commandEngine);

        // The command engine is itself a search provider, so commands rank alongside
        // applications through the same pipeline rather than being special-cased.
        searchEngine.RegisterProvider((ISearchProvider)commandEngine);
        searchEngine.RegisterProvider(applicationProvider);
        searchEngine.RegisterProvider(settingsProvider);
    }

    private void RegisterBridgeChannels()
    {
        bridge.Register("app.info", (_, _) => Task.FromResult<object?>(new
        {
            product = CayrastBrand.ProductName,
            version = typeof(CayrastHost).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            os = Environment.OSVersion.VersionString,
        }));

        bridge.Register("app.hide", (_, _) =>
        {
            // Bridge handlers arrive on a WebView callback, not the UI thread, so any
            // window operation must be marshalled back to the dispatcher.
            window.Dispatcher.Invoke(window.HideLauncher);
            return Task.FromResult<object?>(null);
        });

        bridge.Register("settings.get", (_, _) => Task.FromResult<object?>(settings.Current));

        // The settings screen is generated from these descriptors rather than
        // hand-built, which is also what makes settings searchable.
        bridge.Register("settings.schema", (_, _) => Task.FromResult<object?>(new
        {
            settings = settingsRegistry.All,
        }));

        bridge.Register("modules.list", (_, _) => Task.FromResult<object?>(new
        {
            modules = moduleRegistry.Modules.Select(module => new
            {
                id = module.Id.Value,
                name = module.Manifest.Name,
                version = module.Manifest.Version,
                author = module.Manifest.Author,
                description = module.Manifest.Description,
                permissions = module.RequestedPermissions.ToString(),
                trustLevel = module.TrustLevel.ToString(),
                state = module.State.ToString(),
                failureReason = module.FailureReason,
            }),
        }));

        bridge.Register("settings.set", async (payload, token) =>
        {
            if (payload is null)
            {
                return null;
            }

            var updated = payload.Value.Deserialize<CayrastSettings>(BridgeJsonOptions.Default);
            if (updated is null)
            {
                return null;
            }

            await settings.UpdateAsync(_ => updated, token);
            return settings.Current;
        });

    }

    private void ListenForSecondInstance() =>
        singleInstance.OnActivationRequested(() =>
        {
            logger.LogInformation("A second launch requested activation.");

            // The callback arrives on a thread-pool thread; window operations must
            // be marshalled to the dispatcher.
            window.Dispatcher.Invoke(window.ShowLauncher);
        });

    private void RegisterHotkey()
    {
        var configured = settings.Current.Behavior.Hotkey;

        if (!HotkeyBinding.TryParse(configured, out var binding))
        {
            logger.LogWarning("Hotkey '{Configured}' could not be parsed; falling back to {Default}.",
                configured, HotkeyBinding.Default);
            binding = HotkeyBinding.Default;
        }

        if (hotkeys.TryRegister(LauncherHotkeyId, binding, () => window.ToggleLauncher()))
        {
            return;
        }

        // Registration failing is normal — another application may own the
        // combination. The launcher still works from the tray, so this is a warning
        // the user needs to see rather than a fatal error.
        logger.LogWarning(
            "Could not register {Binding}. Another application is using it. Use the tray icon, or choose a different hotkey in settings.",
            binding);
    }

    private void ShowTrayIcon()
    {
        if (!settings.Current.Behavior.ShowTrayIcon)
        {
            logger.LogInformation("Tray icon suppressed by settings (hidden mode).");
            return;
        }

        trayIcon.Activated += (_, _) => window.Dispatcher.Invoke(window.ShowLauncher);
        trayIcon.MenuItemInvoked += OnTrayMenuItemInvoked;

        trayIcon.Show(CayrastBrand.ProductName,
        [
            ("show", $"Open {CayrastBrand.ProductName}"),
            ("settings", "Settings"),
            ("separator", string.Empty),
            ("quit", "Quit"),
        ]);
    }

    private void OnTrayMenuItemInvoked(object? sender, string id)
    {
        switch (id)
        {
            case "show":
                window.Dispatcher.Invoke(window.ShowLauncher);
                break;

            case "settings":
                window.Dispatcher.Invoke(() =>
                {
                    window.ShowLauncher();
                    bridge.PublishEvent("navigate", new { view = "settings" });
                });
                break;

            case "quit":
                window.Dispatcher.Invoke(() => Application.Current.Shutdown());
                break;
        }
    }

    private void OnSettingsChanged(object? sender, CayrastSettings updated)
    {
        window.Dispatcher.Invoke(window.ApplyAppearance);

        // Tell the frontend too: theme, accent, and layout all live in CSS variables
        // the UI derives from settings, so it needs to know without polling.
        bridge.PublishEvent("settings.changed", updated);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        settings.Changed -= OnSettingsChanged;
        hotkeys.Unregister(LauncherHotkeyId);
        trayIcon.Hide();

        // Flush before exit so a debounced settings change is not lost.
        await settings.FlushAsync(CancellationToken.None);

        // Frecency is only written on shutdown: it changes on every launch, and
        // persisting each one would mean a disk write every time anything is opened.
        await frecency.SaveAsync(CancellationToken.None);

        searchCoordinator.Dispose();

        // Releases the out-of-process browser; without this it can outlive us.
        window.Dispose();
        logger.LogInformation("{Product} stopped.", CayrastBrand.ProductName);
    }
}
