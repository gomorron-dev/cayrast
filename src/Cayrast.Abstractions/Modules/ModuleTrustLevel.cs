namespace Cayrast.Abstractions.Modules;

/// <summary>
/// Determines how a module is hosted, and therefore how strongly its declared
/// permissions are enforced.
/// </summary>
/// <remarks>
/// This is the switch that makes the hybrid hosting model work. Because in-process
/// and sandboxed modules speak the identical IPC contract, moving a module between
/// these levels is a configuration change and never an API change — including for
/// modules already published by third parties.
/// </remarks>
public enum ModuleTrustLevel
{
    /// <summary>
    /// Runs sandboxed in a separate low-integrity process. The default for anything
    /// the user installed from outside the project.
    /// </summary>
    /// <remarks>
    /// Costs roughly 10-15 MB and a sub-millisecond IPC hop per call. In exchange, a
    /// crash, hang, or memory leak in the module cannot take down the shell, and
    /// permission checks cannot be bypassed by calling Win32 directly.
    /// </remarks>
    Sandboxed = 0,

    /// <summary>
    /// Runs in the shell process for speed. Reserved for modules shipped and signed
    /// as part of Cayrast itself.
    /// </summary>
    /// <remarks>
    /// Users may opt an individual third-party module up to this level, but the UI
    /// must state plainly that doing so removes the isolation boundary entirely.
    /// </remarks>
    InProcess = 1,
}
