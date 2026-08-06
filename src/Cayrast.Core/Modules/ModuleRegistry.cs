using System.Collections.Concurrent;
using System.Reflection;
using Cayrast.Abstractions;
using Cayrast.Abstractions.Modules;
using Cayrast.Abstractions.Settings;
using Cayrast.Core.Commands;
using Cayrast.Core.Search;
using Cayrast.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Cayrast.Core.Modules;

/// <summary>What state an installed module is in.</summary>
public enum ModuleState
{
    /// <summary>Installed and present, but not loaded.</summary>
    Disabled,

    /// <summary>Loaded and running.</summary>
    Enabled,

    /// <summary>Loading failed. See <see cref="InstalledModule.FailureReason"/>.</summary>
    Failed,
}

/// <summary>An installed module and its current state.</summary>
/// <param name="Id">Module identifier.</param>
/// <param name="Manifest">Its validated manifest.</param>
/// <param name="Directory">Where it is installed.</param>
/// <param name="RequestedPermissions">What the manifest asked for.</param>
/// <param name="TrustLevel">How it is hosted.</param>
/// <param name="State">Current state.</param>
/// <param name="FailureReason">Why it failed, when <see cref="State"/> is Failed.</param>
public sealed record InstalledModule(
    ModuleId Id,
    ModuleManifest Manifest,
    string Directory,
    ModulePermission RequestedPermissions,
    ModuleTrustLevel TrustLevel,
    ModuleState State,
    string? FailureReason = null);

/// <summary>Installs, loads, and unloads modules.</summary>
public interface IModuleRegistry
{
    /// <summary>Every installed module.</summary>
    IReadOnlyList<InstalledModule> Modules { get; }

    /// <summary>Settings declared by loaded modules.</summary>
    IReadOnlyList<SettingDescriptor> ModuleSettings { get; }

    /// <summary>Reads a package's manifest so permissions can be shown before installing.</summary>
    /// <remarks>
    /// Deliberately separate from installing. The user must be able to see what a module
    /// is asking for and decline, which is impossible if inspection and installation are
    /// the same step.
    /// </remarks>
    (ModuleManifest Manifest, ModulePermission Permissions) Inspect(string packagePath);

    /// <summary>Extracts a package into the plugins directory.</summary>
    Task<InstalledModule> InstallAsync(string packagePath, CancellationToken cancellationToken = default);

    /// <summary>Loads previously installed modules from disk.</summary>
    Task DiscoverAsync(CancellationToken cancellationToken = default);

    /// <summary>Loads and starts a module, granting the permissions the user approved.</summary>
    Task<bool> EnableAsync(ModuleId moduleId, ModulePermission grantedPermissions, CancellationToken cancellationToken = default);

    /// <summary>Stops and unloads a module.</summary>
    Task DisableAsync(ModuleId moduleId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The module lifecycle: discovery, loading, and unloading.
/// </summary>
/// <remarks>
/// <para>
/// Modules load into their own collectible <see cref="ModuleLoadContext"/> so they can
/// be disabled or updated without restarting a launcher that is meant to stay resident
/// all day.
/// </para>
/// <para>
/// <b>Everything a module does is time-budgeted and exception-guarded.</b> A module
/// that hangs in <c>InitializeAsync</c> is reported as failed rather than allowed to
/// delay startup — the rule is that no module can make Alt+Space slower.
/// </para>
/// </remarks>
public sealed class ModuleRegistry(
    ICayrastPaths paths,
    PermissionBroker broker,
    ISearchEngine searchEngine,
    ICommandEngine commandEngine,
    ILogger<ModuleRegistry> logger) : IModuleRegistry, IAsyncDisposable
{
    /// <summary>How long a module may take to initialise before it is abandoned.</summary>
    private static readonly TimeSpan InitializeTimeout = TimeSpan.FromSeconds(10);

    /// <summary>How long a module may take to shut down before it is dropped.</summary>
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<ModuleId, InstalledModule> _modules = [];
    private readonly ConcurrentDictionary<ModuleId, LoadedModule> _loaded = [];

    /// <inheritdoc />
    public IReadOnlyList<InstalledModule> Modules => [.. _modules.Values];

    /// <inheritdoc />
    public IReadOnlyList<SettingDescriptor> ModuleSettings =>
        [.. _loaded.Values.SelectMany(loaded => loaded.Context.RegisteredSettings)];

    /// <inheritdoc />
    public (ModuleManifest Manifest, ModulePermission Permissions) Inspect(string packagePath)
    {
        var (manifest, _, permissions) = ModulePackage.ReadManifest(packagePath);
        return (manifest, permissions);
    }

    /// <inheritdoc />
    public async Task<InstalledModule> InstallAsync(string packagePath, CancellationToken cancellationToken = default)
    {
        var (manifest, id, permissions) = ModulePackage.ReadManifest(packagePath);

        // Built from a validated ModuleId, never from the raw manifest string, so a
        // malformed id cannot traverse out of the plugins directory.
        var directory = Path.Combine(paths.Plugins, id.Value);

        if (Directory.Exists(directory))
        {
            // Replacing an existing install: unload first so its files are not locked.
            await DisableAsync(id, cancellationToken);
            Directory.Delete(directory, recursive: true);
        }

        ModulePackage.Extract(packagePath, directory);

        var installed = new InstalledModule(
            id,
            manifest,
            directory,
            permissions,

            // Third-party modules are sandboxed by default. Promotion to in-process is
            // an explicit user action that states what is being given up.
            ModuleTrustLevel.Sandboxed,
            ModuleState.Disabled);

        _modules[id] = installed;
        logger.LogInformation("Installed module '{Module}' v{Version}.", id, manifest.Version);
        return installed;
    }

    /// <inheritdoc />
    public async Task DiscoverAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(paths.Plugins);

        foreach (var directory in Directory.EnumerateDirectories(paths.Plugins))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var manifestPath = Path.Combine(directory, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            try
            {
                await using var stream = File.OpenRead(manifestPath);
                var (manifest, id, permissions) = ModulePackage.ParseManifest(stream);

                _modules[id] = new InstalledModule(
                    id, manifest, directory, permissions, ModuleTrustLevel.Sandboxed, ModuleState.Disabled);
            }
            catch (Exception ex) when (ex is PackageException or IOException)
            {
                // A broken module must not stop the others from being discovered.
                logger.LogWarning(ex, "Ignoring unreadable module at '{Directory}'.", directory);
            }
        }

        logger.LogInformation("Discovered {Count} installed modules.", _modules.Count);
    }

    /// <inheritdoc />
    public async Task<bool> EnableAsync(
        ModuleId moduleId,
        ModulePermission grantedPermissions,
        CancellationToken cancellationToken = default)
    {
        if (!_modules.TryGetValue(moduleId, out var installed))
        {
            logger.LogWarning("Cannot enable unknown module '{Module}'.", moduleId);
            return false;
        }

        if (_loaded.ContainsKey(moduleId))
        {
            return true;
        }

        // The user may grant less than the manifest requested; a module can never end
        // up with more than it asked for.
        broker.Grant(moduleId, grantedPermissions & installed.RequestedPermissions);

        try
        {
            var loaded = await LoadAsync(installed, cancellationToken);
            _loaded[moduleId] = loaded;
            _modules[moduleId] = installed with { State = ModuleState.Enabled };

            logger.LogInformation("Enabled module '{Module}'.", moduleId);
            return true;
        }
        catch (Exception ex)
        {
            // Loading arbitrary third-party code fails in many ways: a missing
            // dependency, a mismatched SDK, an exception in a static constructor. None
            // of them may take down the host.
            logger.LogError(ex, "Failed to enable module '{Module}'.", moduleId);

            broker.Revoke(moduleId);
            _modules[moduleId] = installed with { State = ModuleState.Failed, FailureReason = ex.Message };
            return false;
        }
    }

    private async Task<LoadedModule> LoadAsync(InstalledModule installed, CancellationToken cancellationToken)
    {
        var context = new ModuleContext(
            installed.Id,
            broker,
            searchEngine,
            commandEngine,
            paths.ModuleData(installed.Id),
            logger);

        // A frontend-only module has no assembly to load, which is the cheapest and
        // safest kind: no code runs in the host at all.
        if (string.IsNullOrEmpty(installed.Manifest.Entry))
        {
            return new LoadedModule(null, null, context);
        }

        var assemblyPath = Path.Combine(installed.Directory, "backend", installed.Manifest.Entry);
        if (!File.Exists(assemblyPath))
        {
            throw new FileNotFoundException($"The module's entry assembly '{installed.Manifest.Entry}' is missing.", assemblyPath);
        }

        var loadContext = new ModuleLoadContext(installed.Id.Value, assemblyPath);
        var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);

        var moduleType = Array.Find(assembly.GetTypes(),
            type => typeof(ICayrastModule).IsAssignableFrom(type) && type is { IsAbstract: false, IsInterface: false })
            ?? throw new InvalidOperationException(
                $"'{installed.Manifest.Entry}' contains no public type implementing {nameof(ICayrastModule)}.");

        var instance = (ICayrastModule)Activator.CreateInstance(moduleType)!;

        // Time-budgeted: a module that blocks here would otherwise delay startup, and
        // the rule is that no module can make the launcher slower.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(InitializeTimeout);

        try
        {
            await instance.InitializeAsync(context, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"The module did not finish initialising within {InitializeTimeout.TotalSeconds:N0} seconds.");
        }

        return new LoadedModule(instance, loadContext, context);
    }

    /// <inheritdoc />
    public async Task DisableAsync(ModuleId moduleId, CancellationToken cancellationToken = default)
    {
        if (!_loaded.TryRemove(moduleId, out var loaded))
        {
            return;
        }

        // Contributions are withdrawn before the assembly is unloaded, or the engines
        // would hold references to types in a context that is being torn down — which
        // both prevents collection and can fault on the next query.
        foreach (var providerId in loaded.Context.RegisteredProviderIds)
        {
            searchEngine.UnregisterProvider(providerId);
        }

        commandEngine.UnregisterModule(moduleId.Value);

        if (loaded.Instance is not null)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ShutdownTimeout);

            try
            {
                await loaded.Instance.ShutdownAsync(timeout.Token);
            }
            catch (Exception ex)
            {
                // A module refusing to shut down cleanly does not get to block its own
                // removal; it is dropped regardless.
                logger.LogWarning(ex, "Module '{Module}' failed to shut down cleanly.", moduleId);
            }
        }

        broker.Revoke(moduleId);

        // Unload is a request, not a guarantee. The context is collected once nothing
        // references anything inside it, which is why the host only ever holds
        // interfaces defined in Cayrast.Abstractions.
        loaded.LoadContext?.Unload();

        if (_modules.TryGetValue(moduleId, out var installed))
        {
            _modules[moduleId] = installed with { State = ModuleState.Disabled, FailureReason = null };
        }

        logger.LogInformation("Disabled module '{Module}'.", moduleId);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var moduleId in _loaded.Keys.ToList())
        {
            await DisableAsync(moduleId, CancellationToken.None);
        }
    }

    private sealed record LoadedModule(ICayrastModule? Instance, ModuleLoadContext? LoadContext, ModuleContext Context);
}
