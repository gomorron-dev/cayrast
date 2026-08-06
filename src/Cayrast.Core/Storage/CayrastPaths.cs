using Cayrast.Abstractions;

namespace Cayrast.Core.Storage;

/// <summary>
/// Resolves every filesystem location Cayrast uses.
/// </summary>
/// <remarks>
/// <para>
/// The specification is explicit that user data must never live under the install
/// directory, and Windows agrees: <c>Program Files</c> is read-only for standard
/// users, and writing there either fails or silently redirects into VirtualStore,
/// which is worse than failing because the data appears to save and then vanishes.
/// </para>
/// <para>
/// The split follows Windows conventions:
/// </para>
/// <list type="bullet">
///   <item><b>Roaming</b> (<c>%APPDATA%</c>) — settings, themes, commands, plugins.
///         Things a user would want to follow them to another machine.</item>
///   <item><b>Local</b> (<c>%LOCALAPPDATA%</c>) — caches, logs, indexes, the WebView2
///         user-data folder. Machine-specific, regenerable, and often large; roaming
///         it would bloat profiles and slow domain logins for no benefit.</item>
/// </list>
/// <para>
/// Nothing in the codebase should compose these paths by hand. Centralising them here
/// is what makes the backup, export, and uninstall features tractable: each needs an
/// exact inventory of what Cayrast wrote and where.
/// </para>
/// </remarks>
public static class CayrastPaths
{
    private static readonly string RoamingRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        CayrastBrand.DataFolderName);

    private static readonly string LocalRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        CayrastBrand.DataFolderName);

    /// <summary><c>%APPDATA%\Cayrast</c> — root of all roaming user data.</summary>
    public static string Roaming => RoamingRoot;

    /// <summary><c>%LOCALAPPDATA%\Cayrast</c> — root of all machine-local data.</summary>
    public static string Local => LocalRoot;

    /// <summary>Settings files, including <c>settings.json</c> and profiles.</summary>
    public static string Settings => Path.Combine(RoamingRoot, "Settings");

    /// <summary>Installed module packages, one directory per module id.</summary>
    public static string Plugins => Path.Combine(RoamingRoot, "Plugins");

    /// <summary>Installed themes.</summary>
    public static string Themes => Path.Combine(RoamingRoot, "Themes");

    /// <summary>User-defined commands.</summary>
    public static string Commands => Path.Combine(RoamingRoot, "Commands");

    /// <summary>SQLite databases, including clipboard history and the frecency store.</summary>
    public static string Database => Path.Combine(RoamingRoot, "Database");

    /// <summary>Rolling log files.</summary>
    /// <remarks>
    /// Local rather than roaming: logs are diagnostic, can reach tens of megabytes, and
    /// are meaningless on a different machine.
    /// </remarks>
    public static string Logs => Path.Combine(LocalRoot, "Logs");

    /// <summary>Regenerable caches — icon bitmaps, the application index, thumbnails.</summary>
    /// <remarks>Safe to delete at any time; everything here rebuilds on demand.</remarks>
    public static string Cache => Path.Combine(LocalRoot, "Cache");

    /// <summary>WebView2's user-data folder.</summary>
    /// <remarks>
    /// Must be an explicit, writable, per-user path. Left unset, WebView2 defaults to a
    /// folder beside the executable — which fails outright when Cayrast is installed
    /// for all users.
    /// </remarks>
    public static string WebViewData => Path.Combine(LocalRoot, "WebView2");

    /// <summary>Private data directory for a specific module.</summary>
    /// <param name="moduleId">The module's validated identifier.</param>
    /// <remarks>
    /// Takes a parsed <c>ModuleId</c> rather than a string precisely so a malformed id
    /// cannot traverse out of the plugins directory.
    /// </remarks>
    public static string ModuleData(Abstractions.Modules.ModuleId moduleId) =>
        Path.Combine(Plugins, moduleId.Value, "data");

    /// <summary>
    /// Creates every directory Cayrast writes to. Safe to call repeatedly.
    /// </summary>
    /// <remarks>
    /// Called once during startup so that later writes can assume their target exists,
    /// rather than every writer defensively creating directories.
    /// </remarks>
    public static void EnsureCreated()
    {
        foreach (var directory in new[] { Settings, Plugins, Themes, Commands, Database, Logs, Cache, WebViewData })
        {
            Directory.CreateDirectory(directory);
        }
    }
}
