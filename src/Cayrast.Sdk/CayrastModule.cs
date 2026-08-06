using Cayrast.Abstractions.Modules;

namespace Cayrast.Sdk;

/// <summary>
/// A convenient base class for modules.
/// </summary>
/// <remarks>
/// <para>
/// Implementing <see cref="ICayrastModule"/> directly is entirely supported and
/// nothing here is privileged — this only removes boilerplate that most modules would
/// otherwise write identically. It holds the context so you do not have to, and gives
/// <see cref="ShutdownAsync"/> a default so a module with nothing to clean up can omit
/// it.
/// </para>
/// <para>
/// <b>Do not block in <see cref="OnInitializeAsync"/>.</b> The host applies a startup
/// budget and will report your module as failed rather than let it delay the launcher.
/// If you need to build an index or open a connection, start it and return; do not wait
/// for it.
/// </para>
/// </remarks>
public abstract class CayrastModule : ICayrastModule
{
    private IModuleContext? _context;

    /// <summary>The host, available once initialisation has begun.</summary>
    /// <exception cref="InvalidOperationException">Accessed before initialisation.</exception>
    protected IModuleContext Context =>
        _context ?? throw new InvalidOperationException(
            "The module context is not available until InitializeAsync has been called.");

    /// <summary>Convenience access to this module's logger.</summary>
    protected IModuleLogger Log => Context.Logger;

    /// <summary>Whether the user granted a capability.</summary>
    /// <remarks>
    /// Users may grant a subset of what the manifest requested. Checking here and
    /// degrading gracefully is better than calling and handling the failure — a module
    /// that works with less is more likely to be kept installed.
    /// </remarks>
    protected bool HasPermission(ModulePermission permission) =>
        (Context.GrantedPermissions & permission) == permission;

    /// <summary>Register search providers, commands, and settings here.</summary>
    /// <param name="cancellationToken">Cancelled if the host is shutting down or your budget expired.</param>
    protected abstract Task OnInitializeAsync(CancellationToken cancellationToken);

    /// <summary>Release resources here. Optional.</summary>
    /// <remarks>
    /// Not guaranteed to run — a sandboxed module process can be killed outright. Write
    /// durable state when it changes rather than deferring it to shutdown.
    /// </remarks>
    protected virtual Task OnShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task InitializeAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        return OnInitializeAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task ShutdownAsync(CancellationToken cancellationToken) => OnShutdownAsync(cancellationToken);
}
