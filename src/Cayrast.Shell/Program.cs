using Cayrast.Abstractions;

namespace Cayrast.Shell;

/// <summary>
/// Process entry point.
/// </summary>
/// <remarks>
/// <para>
/// Startup order matters here and is not arbitrary:
/// </para>
/// <list type="number">
///   <item>Single-instance check — before anything expensive is initialised.</item>
///   <item>Logging — so failures in every later step are recorded.</item>
///   <item>Composition root — build the service graph.</item>
///   <item>Warm the window — create the launcher window and WebView2 hidden.</item>
///   <item>Register the global hotkey and tray icon, then pump messages.</item>
/// </list>
/// <para>
/// Step 4 is what makes Alt+Space feel instantaneous. The window and its WebView2
/// instance are constructed once at login and then shown and hidden for the rest of
/// the session — never destroyed and rebuilt. Creating a WebView2 takes on the order
/// of a hundred milliseconds, which is unacceptable per-invocation but unnoticeable
/// once at boot, when nobody is waiting on it.
/// </para>
/// </remarks>
internal static class Program
{
    /// <summary>
    /// Main entry point. STA is required: WPF, the tray icon, and the clipboard and
    /// shell COM APIs all demand a single-threaded apartment.
    /// </summary>
    [STAThread]
    internal static int Main(string[] args)
    {
        // Phase 1 milestone (see docs/ROADMAP.md) replaces this with the real
        // startup sequence described above. Kept minimal and honest for now so the
        // tree builds and runs from the first commit rather than pretending to work.
        Console.WriteLine($"{CayrastBrand.ProductName} shell — not yet implemented (Phase 1).");
        Console.WriteLine($"Arguments: {(args.Length == 0 ? "(none)" : string.Join(' ', args))}");
        return 0;
    }
}
