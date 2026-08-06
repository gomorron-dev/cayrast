namespace Cayrast.ModuleHost;

/// <summary>
/// Entry point for a sandboxed module's host process.
/// </summary>
/// <remarks>
/// <para>
/// Launched by the shell, one process per untrusted module, with:
/// </para>
/// <list type="bullet">
///   <item>a low-integrity token, so it cannot write to the user's profile or send
///         input to normal-integrity windows;</item>
///   <item>a named pipe to the shell, which is its only channel to the outside world;</item>
///   <item>a job object, so it dies with the shell and cannot outlive it or spawn
///         escapees.</item>
/// </list>
/// <para>
/// The module assembly is loaded into a collectable <c>AssemblyLoadContext</c> so a
/// module can be updated or unloaded without restarting even this process.
/// </para>
/// <para>
/// This process is expected to be killed without warning. It must hold no unflushed
/// state that the user would miss.
/// </para>
/// </remarks>
internal static class Program
{
    /// <summary>
    /// Entry point. STA because modules may use clipboard or shell COM APIs that
    /// require it.
    /// </summary>
    /// <param name="args">
    /// Supplied by the shell: the module id, its package directory, and the pipe name
    /// to connect back on.
    /// </param>
    [STAThread]
    internal static int Main(string[] args)
    {
        // Implemented alongside the module loader in Phase 2 (see docs/ROADMAP.md).
        Console.Error.WriteLine("Cayrast.ModuleHost is not yet implemented (Phase 2).");
        Console.Error.WriteLine($"Arguments: {(args.Length == 0 ? "(none)" : string.Join(' ', args))}");
        return 1;
    }
}
