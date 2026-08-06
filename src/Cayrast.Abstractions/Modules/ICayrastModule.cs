namespace Cayrast.Abstractions.Modules;

/// <summary>
/// Implemented once per module package. The host discovers this type in the
/// assembly named by <see cref="ModuleManifest.Entry"/>.
/// </summary>
/// <remarks>
/// <para>
/// Both lifecycle methods are asynchronous and are called off the UI thread.
/// A module must never block: the host applies a startup timeout and will report a
/// module as failed rather than let it delay the launcher, because a slow module
/// must never be able to make Alt+Space feel slow.
/// </para>
/// <para>
/// Registration is push-based — a module declares what it offers during
/// <see cref="InitializeAsync"/> via <see cref="IModuleContext"/>. The host never
/// reflects over module types looking for conventions, which keeps loading fast and
/// keeps the contract explicit.
/// </para>
/// </remarks>
public interface ICayrastModule
{
    /// <summary>
    /// Called once after the module is loaded and its permissions have been granted.
    /// Register search providers, commands, settings, and widgets here.
    /// </summary>
    /// <param name="context">The module's brokered view of the host.</param>
    /// <param name="cancellationToken">
    /// Cancelled if the host is shutting down or the module exceeded its startup budget.
    /// </param>
    Task InitializeAsync(IModuleContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Called when the module is being disabled, updated, or the host is shutting down.
    /// Flush state and release resources here.
    /// </summary>
    /// <remarks>
    /// Not guaranteed to run — a sandboxed module process can be killed outright if it
    /// stops responding. Durable state must be written when it changes, not deferred
    /// to shutdown.
    /// </remarks>
    Task ShutdownAsync(CancellationToken cancellationToken);
}
