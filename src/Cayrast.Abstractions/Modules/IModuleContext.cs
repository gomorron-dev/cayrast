using Cayrast.Abstractions.Commands;
using Cayrast.Abstractions.Search;
using Cayrast.Abstractions.Settings;

namespace Cayrast.Abstractions.Modules;

/// <summary>
/// A module's entire view of the host. Everything a module can do, it does through here.
/// </summary>
/// <remarks>
/// <para>
/// This is the permission broker's front door and the most security-sensitive type in
/// the codebase. It is deliberately narrow: there is no <c>GetService</c>, no
/// <c>IServiceProvider</c>, and no escape hatch that would let a module reach past the
/// broker into host internals. Widening this interface is how a plugin system loses its
/// security properties, so additions deserve the same scrutiny as a new permission.
/// </para>
/// <para>
/// The same implementation is used for in-process and sandboxed modules. For sandboxed
/// ones every method marshals across the IPC boundary; for in-process ones it is a
/// direct call. Module code cannot tell the difference, which is exactly what allows a
/// module's trust level to change without recompiling it.
/// </para>
/// </remarks>
public interface IModuleContext
{
    /// <summary>Identity of the module this context belongs to.</summary>
    ModuleId ModuleId { get; }

    /// <summary>Permissions the user actually granted — not what the manifest requested.</summary>
    /// <remarks>
    /// A user may grant a subset. Well-behaved modules check this and degrade gracefully
    /// instead of failing at the first denied call.
    /// </remarks>
    ModulePermission GrantedPermissions { get; }

    /// <summary>
    /// Per-module private data directory under <c>%APPDATA%\Cayrast\Plugins\{id}\</c>.
    /// </summary>
    /// <remarks>
    /// Created on demand and always writable without the
    /// <see cref="ModulePermission.FileSystem"/> permission — a module storing its own
    /// state is not a capability that needs gating, and forcing it through the permission
    /// prompt would train users to grant filesystem access reflexively.
    /// </remarks>
    string DataDirectory { get; }

    /// <summary>Structured logging scoped to this module. Output is tagged with the module id.</summary>
    IModuleLogger Logger { get; }

    /// <summary>Contributes a source of search results.</summary>
    void RegisterSearchProvider(ISearchProvider provider);

    /// <summary>Contributes a command invocable from the search bar.</summary>
    void RegisterCommand(CommandDescriptor descriptor, ICommandHandler handler);

    /// <summary>
    /// Contributes a setting, which appears in the module's settings page and becomes
    /// findable through settings search.
    /// </summary>
    void RegisterSetting(SettingDescriptor descriptor);

    /// <summary>Reads a previously stored value for one of this module's settings.</summary>
    ValueTask<T?> GetSettingAsync<T>(string settingId, CancellationToken cancellationToken = default);

    /// <summary>Stores a value for one of this module's settings.</summary>
    ValueTask SetSettingAsync<T>(string settingId, T value, CancellationToken cancellationToken = default);
}

/// <summary>Minimal structured logging surface exposed to modules.</summary>
/// <remarks>
/// Intentionally not <c>Microsoft.Extensions.Logging.ILogger</c>: that would drag a
/// package dependency into <c>Cayrast.Abstractions</c> and couple the public module
/// contract to a logging library's release cadence. The host adapts this to whatever
/// it uses internally.
/// </remarks>
public interface IModuleLogger
{
    /// <summary>Diagnostic detail, off unless the user enables verbose logging.</summary>
    void Debug(string message);

    /// <summary>Normal operational events worth keeping.</summary>
    void Information(string message);

    /// <summary>Something recoverable went wrong.</summary>
    void Warning(string message, Exception? exception = null);

    /// <summary>The module failed to do what was asked.</summary>
    void Error(string message, Exception? exception = null);
}
