using System.Reflection;
using Cayrast.Abstractions.Applications;
using Microsoft.Extensions.Logging;

namespace Cayrast.Platform.Windows.Applications;

/// <summary>
/// Enumerates installed applications through the shell's AppsFolder.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why AppsFolder rather than scanning the Start Menu.</b> Walking Start Menu
/// <c>.lnk</c> files is the obvious approach and it misses every packaged application —
/// Calculator, Settings, Terminal, and most of what ships with Windows 11 have no
/// shortcut on disk. AppsFolder is the same virtual folder the Start Menu itself
/// presents, so it covers Win32 and packaged apps through one path and yields an
/// AppUserModelID that launches either kind identically.
/// </para>
/// <para>
/// <b>COM and threading.</b> The Shell Automation objects are apartment-threaded, so
/// enumeration runs on its own dedicated STA thread rather than on the thread pool —
/// calling them from an MTA pool thread marshals every property access through a proxy
/// and turns a fast scan into a slow one.
/// </para>
/// <para>
/// <b>Late binding.</b> The interop is done through reflection rather than a generated
/// interop assembly. It keeps the dependency count at zero and confines the fragility
/// to this one file, where a failure degrades to an empty index rather than a crash.
/// </para>
/// </remarks>
public sealed class ApplicationIndexer(ILogger<ApplicationIndexer> logger) : IApplicationIndex, IDisposable
{
    /// <summary>The shell folder containing every launchable application.</summary>
    private const string AppsFolderPath = "shell:AppsFolder";

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private volatile IReadOnlyList<InstalledApplication> _applications = [];
    private Timer? _periodicRefresh;
    private bool _disposed;

    /// <inheritdoc />
    public IReadOnlyList<InstalledApplication> Applications => _applications;

    /// <inheritdoc />
    public event EventHandler? Updated;

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await RefreshAsync(cancellationToken);

        // AppsFolder has no change notification, so the index is rebuilt periodically.
        // Installing software is rare enough that five minutes is imperceptible, and a
        // scan is cheap relative to how long an install takes anyway.
        _periodicRefresh = new Timer(
            _ => _ = RefreshAsync(CancellationToken.None),
            state: null,
            dueTime: TimeSpan.FromMinutes(5),
            period: TimeSpan.FromMinutes(5));
    }

    /// <inheritdoc />
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        // Never let two scans overlap: they would duplicate work and could publish an
        // older result over a newer one.
        if (!await _refreshLock.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            var started = Environment.TickCount64;
            var applications = await Task.Run(EnumerateOnStaThread, cancellationToken);

            if (applications.Count > 0)
            {
                _applications = applications;
                Updated?.Invoke(this, EventArgs.Empty);

                logger.LogInformation(
                    "Indexed {Count} applications in {Elapsed} ms.",
                    applications.Count, Environment.TickCount64 - started);
            }
            else
            {
                // Publishing an empty list over a good one would make search silently
                // stop finding anything, which is worse than serving slightly stale data.
                logger.LogWarning("Application scan returned nothing; keeping the previous index.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to index applications.");
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>Runs the COM enumeration on a dedicated single-threaded-apartment thread.</summary>
    private List<InstalledApplication> EnumerateOnStaThread()
    {
        List<InstalledApplication> result = [];

        var thread = new Thread(() =>
        {
            try
            {
                result = Enumerate();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Shell enumeration failed.");
            }
        })
        {
            IsBackground = true,
            Name = "Cayrast.ApplicationIndexer",
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        // Bounded so a wedged shell extension cannot hold the indexer forever. A scan
        // normally takes well under a second.
        if (!thread.Join(TimeSpan.FromSeconds(30)))
        {
            logger.LogWarning("Application scan timed out.");
            return [];
        }

        return result;
    }

    private List<InstalledApplication> Enumerate()
    {
        var applications = new List<InstalledApplication>(256);

        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType is null)
        {
            logger.LogWarning("Shell.Application is unavailable; no applications will be indexed.");
            return applications;
        }

        object? shell = null;
        object? folder = null;
        object? items = null;

        try
        {
            shell = Activator.CreateInstance(shellType);
            if (shell is null)
            {
                return applications;
            }

            folder = Invoke(shell, "NameSpace", AppsFolderPath);
            if (folder is null)
            {
                logger.LogWarning("Could not open {Folder}.", AppsFolderPath);
                return applications;
            }

            items = Invoke(folder, "Items");
            if (items is null)
            {
                return applications;
            }

            var count = Convert.ToInt32(GetProperty(items, "Count") ?? 0, System.Globalization.CultureInfo.InvariantCulture);

            for (var i = 0; i < count; i++)
            {
                object? item = null;

                try
                {
                    item = Invoke(items, "Item", i);
                    if (item is null)
                    {
                        continue;
                    }

                    var name = GetProperty(item, "Name") as string;
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    // Prefer the AppUserModelID and fall back to Path. For entries that
                    // have an identity the shell returns the same value from both, so
                    // the order only matters for the ones that do not.
                    var launchId = GetExtendedProperty(item, "System.AppUserModel.ID");

                    if (string.IsNullOrWhiteSpace(launchId))
                    {
                        launchId = GetProperty(item, "Path") as string;
                    }

                    if (string.IsNullOrWhiteSpace(launchId))
                    {
                        continue;
                    }

                    // The only reliable test is whether this is a path. Checking for a
                    // packaged-looking identity instead ("contains !") gets it wrong for
                    // desktop applications that register a plain AppUserModelID such as
                    // "Anysphere.Cursor" — no bundle suffix, no '!', and not a path
                    // either. Those would then be handed to Process.Start as a filename
                    // and fail to launch.
                    var isPath = launchId.Contains('\\', StringComparison.Ordinal)
                                 || launchId.Contains(":\\", StringComparison.Ordinal);

                    applications.Add(new InstalledApplication(
                        name,
                        launchId,

                        // Icons can only be extracted from a real file.
                        IconSource: isPath ? launchId : null,
                        LaunchViaAppsFolder: !isPath));
                }
                catch (Exception ex)
                {
                    // One malformed entry — usually a broken third-party shell
                    // extension — must not abort the whole scan.
                    logger.LogDebug(ex, "Skipped an unreadable AppsFolder entry at index {Index}.", i);
                }
                finally
                {
                    Release(item);
                }
            }
        }
        finally
        {
            Release(items);
            Release(folder);
            Release(shell);
        }

        return applications;
    }

    private static object? Invoke(object target, string method, params object[] arguments) =>
        target.GetType().InvokeMember(method, BindingFlags.InvokeMethod, binder: null, target, arguments,
            System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Reads a shell property such as <c>System.AppUserModel.ID</c>.</summary>
    /// <remarks>
    /// Returns <see langword="null"/> rather than throwing when the property is absent,
    /// which is normal: desktop entries predating packaged identity simply do not have
    /// one, and the caller falls back to the item's path.
    /// </remarks>
    private static string? GetExtendedProperty(object item, string property)
    {
        try
        {
            return Invoke(item, "ExtendedProperty", property) as string;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or MissingMethodException or TargetInvocationException)
        {
            return null;
        }
    }

    private static object? GetProperty(object target, string property) =>
        target.GetType().InvokeMember(property, BindingFlags.GetProperty, binder: null, target, args: null,
            System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Releases a runtime-callable wrapper promptly.</summary>
    /// <remarks>
    /// Without this the COM objects survive until a garbage collection happens to run.
    /// A scan creates thousands of them, and holding shell objects open blocks
    /// operations elsewhere in Windows such as ejecting a drive.
    /// </remarks>
    private static void Release(object? comObject)
    {
        if (comObject is not null && System.Runtime.InteropServices.Marshal.IsComObject(comObject))
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(comObject);
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
        _periodicRefresh?.Dispose();
        _refreshLock.Dispose();
    }
}
