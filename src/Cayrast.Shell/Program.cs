using System.IO;
using System.Windows;
using Cayrast.Abstractions;
using Cayrast.Abstractions.Platform;
using Cayrast.Core.Settings;
using Cayrast.Core.Storage;
using Cayrast.Platform.Windows;
using Cayrast.Shell.Bridge;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace Cayrast.Shell;

/// <summary>
/// Process entry point.
/// </summary>
/// <remarks>
/// <para>
/// The ordering below is deliberate. The single-instance check happens before any
/// expensive initialisation so a duplicate launch costs almost nothing, and logging is
/// configured immediately after so that every subsequent failure is recorded rather
/// than lost.
/// </para>
/// <para>
/// Cayrast has no main window in the usual sense. The launcher is created hidden and
/// toggled by hotkey, so shutdown is explicit — closing the window must not end the
/// process, or pressing Escape would quit the application.
/// </para>
/// </remarks>
internal static class Program
{
    /// <summary>
    /// Main entry point. STA is required by WPF, the tray icon, and the clipboard and
    /// shell COM APIs.
    /// </summary>
    [STAThread]
    internal static int Main(string[] args)
    {
        // Runs before the container exists, so it uses the shared default instance.
        // Everything built by the container takes ICayrastPaths instead.
        CayrastPaths.Default.EnsureCreated();
        ConfigureLogging();

        // Claim the instance lock before anything costly. A second launch should wake
        // the running instance and exit, not start a competing process that would
        // fight over the hotkey, the tray icon, and the settings file.
        using var instance = SingleInstance.Acquire();
        if (!instance.IsPrimary)
        {
            Log.Information("{Product} is already running; activating the existing instance.", CayrastBrand.ProductName);

            if (!SingleInstance.SignalExistingInstance())
            {
                // The primary exited between our mutex check and the signal. Rather
                // than leave the user with nothing having happened, tell them to try
                // again — the next launch will win the mutex cleanly.
                Log.Warning("The existing instance could not be signalled; it may be shutting down.");
            }

            Log.CloseAndFlush();
            return 0;
        }

        try
        {
            return Run(args, instance);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "{Product} terminated unexpectedly.", CayrastBrand.ProductName);
            ShowStartupFailure(ex);
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static int Run(string[] args, SingleInstance instance)
    {
        Log.Information("Starting {Product} (arguments: {Arguments}).",
            CayrastBrand.ProductName, args.Length == 0 ? "none" : string.Join(' ', args));

        using var services = BuildServiceProvider(instance);

        var application = new Application
        {
            // Without this, hiding the launcher would look like the last window
            // closing and WPF would shut the process down.
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };

        // Resolved eagerly, and before anything touches WebView2: child processes only
        // join the job if it exists when they are spawned. Disposed with the container.
        _ = services.GetRequiredService<ChildProcessJob>();

        var host = services.GetRequiredService<CayrastHost>();
        var startupFailed = false;

        application.Startup += async (_, _) =>
        {
            try
            {
                await host.StartAsync();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Startup failed.");
                startupFailed = true;
                ShowStartupFailure(ex);
                application.Shutdown(1);
            }
        };

        application.Exit += (_, _) => host.DisposeAsync().AsTask().GetAwaiter().GetResult();

        var exitCode = application.Run();
        return startupFailed ? 1 : exitCode;
    }

    private static ServiceProvider BuildServiceProvider(SingleInstance instance)
    {
        var services = new ServiceCollection();

        // Registered as an existing instance because Main owns its lifetime: the lock
        // must be held from before the container exists until after it is torn down.
        services.AddSingleton(instance);

        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(dispose: false);
        });

        // Platform integration. MessageWindow is a singleton because the hotkey and
        // tray services must share one HWND — two message windows would mean messages
        // arriving where nothing is listening.
        services.AddSingleton<ChildProcessJob>();
        services.AddSingleton<MessageWindow>();
        services.AddSingleton<IHotkeyService, HotkeyService>();
        services.AddSingleton<ITrayIconService, TrayIconService>();
        services.AddSingleton<IWindowEffects, WindowEffects>();
        services.AddSingleton<IMonitorService, MonitorService>();

        // Core services.
        services.AddSingleton<ICayrastPaths>(CayrastPaths.Default);
        services.AddSingleton<ISettingsService, SettingsService>();

        // Shell.
        services.AddSingleton<WebMessageBridge>();
        services.AddSingleton<LauncherWindow>();
        services.AddSingleton<CayrastHost>();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            // Catches a captive dependency (a singleton holding a scoped service) at
            // startup instead of as a confusing lifetime bug much later.
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    private static void ConfigureLogging()
    {
        var logPath = Path.Combine(CayrastPaths.Default.Logs, "cayrast-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()

            // WebView2 and WPF are noisy at debug level and drown out our own events.
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.WithProperty("Version", typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0")
            .WriteTo.File(
                logPath,
                rollingInterval: RollingInterval.Day,

                // A second launch overlaps the primary for a moment. Without shared
                // access each one would take an exclusive lock and spill into
                // cayrast-<date>_001.log, _002.log, and so on, scattering a single
                // session's story across files.
                shared: true,

                // Bounded so logs cannot grow without limit on a machine that is never
                // restarted. Seven files at 16 MB is enough to diagnose an intermittent
                // fault without turning into a disk-space problem of its own.
                retainedFileCountLimit: 7,
                fileSizeLimitBytes: 16 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    private static void ShowStartupFailure(Exception exception)
    {
        // The log is the real diagnostic, but a user whose launcher did not appear
        // needs to be told why and where to look rather than left with silence.
        var message =
            $"{CayrastBrand.ProductName} could not start.\n\n" +
            $"{exception.Message}\n\n" +
            $"Details have been written to:\n{CayrastPaths.Default.Logs}";

        MessageBox.Show(message, $"{CayrastBrand.ProductName} — Startup Error",
            MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
