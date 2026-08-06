using System.Collections.Concurrent;
using System.Text.Json;
using Cayrast.Abstractions.Commands;
using Cayrast.Abstractions.Modules;
using Cayrast.Abstractions.Search;
using Cayrast.Abstractions.Settings;
using Cayrast.Core.Commands;
using Cayrast.Core.Search;
using Microsoft.Extensions.Logging;

namespace Cayrast.Core.Modules;

/// <summary>
/// A module's brokered view of the host.
/// </summary>
/// <remarks>
/// Deliberately narrow. There is no <c>GetService</c> and no escape hatch that would
/// let a module reach past the broker into host internals — widening this interface is
/// how a plugin system quietly loses its security properties, so additions belong
/// under the same scrutiny as a new permission.
/// </remarks>
internal sealed class ModuleContext(
    ModuleId moduleId,
    PermissionBroker broker,
    ISearchEngine searchEngine,
    ICommandEngine commandEngine,
    string dataDirectory,
    ILogger logger) : IModuleContext
{
    private readonly ConcurrentDictionary<string, JsonElement> _settings = new(StringComparer.Ordinal);

    /// <summary>Search providers this module registered, so they can be removed on unload.</summary>
    public List<string> RegisteredProviderIds { get; } = [];

    /// <summary>Settings this module declared, exposed to the settings registry.</summary>
    public List<SettingDescriptor> RegisteredSettings { get; } = [];

    /// <inheritdoc />
    public ModuleId ModuleId => moduleId;

    /// <inheritdoc />
    public ModulePermission GrantedPermissions => broker.GetGranted(moduleId);

    /// <inheritdoc />
    public string DataDirectory
    {
        get
        {
            // Created on demand rather than at load time: most modules never write
            // anything, and an empty directory per installed module is just litter.
            Directory.CreateDirectory(dataDirectory);
            return dataDirectory;
        }
    }

    /// <inheritdoc />
    public IModuleLogger Logger { get; } = new ScopedModuleLogger(moduleId, logger);

    /// <inheritdoc />
    public void RegisterSearchProvider(ISearchProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        searchEngine.RegisterProvider(provider);
        RegisteredProviderIds.Add(provider.Id);
    }

    /// <inheritdoc />
    public void RegisterCommand(CommandDescriptor descriptor, ICommandHandler handler)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        // Stamped with the owning module so the command can be removed when the module
        // is unloaded, and so `help` can show where it came from.
        commandEngine.Register(descriptor with { OwnerModuleId = moduleId.Value }, handler);
    }

    /// <inheritdoc />
    public void RegisterSetting(SettingDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        RegisteredSettings.Add(descriptor with { OwnerModuleId = moduleId.Value });
    }

    /// <inheritdoc />
    public ValueTask<T?> GetSettingAsync<T>(string settingId, CancellationToken cancellationToken = default)
    {
        if (!_settings.TryGetValue(settingId, out var stored))
        {
            return ValueTask.FromResult<T?>(default);
        }

        try
        {
            return ValueTask.FromResult(stored.Deserialize<T>());
        }
        catch (JsonException)
        {
            // A module asking for a type that does not match what was stored gets the
            // default rather than an exception, so a settings-shape change during
            // development cannot brick the module.
            return ValueTask.FromResult<T?>(default);
        }
    }

    /// <inheritdoc />
    public ValueTask SetSettingAsync<T>(string settingId, T value, CancellationToken cancellationToken = default)
    {
        _settings[settingId] = JsonSerializer.SerializeToElement(value);
        return ValueTask.CompletedTask;
    }

    /// <summary>Adapts the host logger, tagging every entry with the module id.</summary>
    private sealed class ScopedModuleLogger(ModuleId moduleId, ILogger inner) : IModuleLogger
    {
        public void Debug(string message) => inner.LogDebug("[{Module}] {Message}", moduleId, message);

        public void Information(string message) => inner.LogInformation("[{Module}] {Message}", moduleId, message);

        public void Warning(string message, Exception? exception = null) =>
            inner.LogWarning(exception, "[{Module}] {Message}", moduleId, message);

        public void Error(string message, Exception? exception = null) =>
            inner.LogError(exception, "[{Module}] {Message}", moduleId, message);
    }
}
