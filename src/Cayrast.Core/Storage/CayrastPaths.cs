using Cayrast.Abstractions;
using Cayrast.Abstractions.Modules;

namespace Cayrast.Core.Storage;

/// <summary>Every filesystem location Cayrast uses.</summary>
/// <remarks>
/// An interface rather than a static class so components that write to disk can be
/// tested against a temporary directory. Settings persistence, the clipboard store,
/// and module data all need that, and a hard-coded <c>%APPDATA%</c> would make each
/// of them testable only by writing to the developer's real profile.
/// </remarks>
public interface ICayrastPaths
{
    /// <summary>Root of all roaming user data.</summary>
    string Roaming { get; }

    /// <summary>Root of all machine-local data.</summary>
    string Local { get; }

    /// <summary>Settings files, including <c>settings.json</c> and profiles.</summary>
    string Settings { get; }

    /// <summary>Installed module packages, one directory per module id.</summary>
    string Plugins { get; }

    /// <summary>Installed themes.</summary>
    string Themes { get; }

    /// <summary>User-defined commands.</summary>
    string Commands { get; }

    /// <summary>SQLite databases, including clipboard history and the frecency store.</summary>
    string Database { get; }

    /// <summary>Rolling log files.</summary>
    string Logs { get; }

    /// <summary>Regenerable caches — icon bitmaps, the application index, thumbnails.</summary>
    string Cache { get; }

    /// <summary>WebView2's user-data folder.</summary>
    string WebViewData { get; }

    /// <summary>Private data directory for a specific module.</summary>
    string ModuleData(ModuleId moduleId);

    /// <summary>Creates every directory Cayrast writes to. Safe to call repeatedly.</summary>
    void EnsureCreated();
}

/// <summary>
/// Resolves Cayrast's storage locations under the user's profile.
/// </summary>
/// <remarks>
/// <para>
/// The specification is explicit that user data must never live in the install
/// directory, and Windows agrees: <c>Program Files</c> is read-only for standard
/// users, and writing there either fails or silently redirects into VirtualStore —
/// which is worse than failing, because the data appears to save and then vanishes.
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
/// Nothing composes these paths by hand. Centralising them is what makes backup,
/// export, and clean uninstall tractable: each needs an exact inventory of what
/// Cayrast wrote and where.
/// </para>
/// </remarks>
public sealed class CayrastPaths : ICayrastPaths
{
    /// <summary>
    /// The shared instance rooted in the real user profile.
    /// </summary>
    /// <remarks>
    /// Exists because logging and the single-instance check run before the dependency
    /// injection container is built. Everything constructed by the container should
    /// take <see cref="ICayrastPaths"/> instead.
    /// </remarks>
    public static CayrastPaths Default { get; } = new(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify));

    /// <summary>Creates a path set rooted at the given profile directories.</summary>
    /// <param name="roamingRoot">Directory standing in for <c>%APPDATA%</c>.</param>
    /// <param name="localRoot">Directory standing in for <c>%LOCALAPPDATA%</c>.</param>
    public CayrastPaths(string roamingRoot, string localRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roamingRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(localRoot);

        Roaming = Path.Combine(roamingRoot, CayrastBrand.DataFolderName);
        Local = Path.Combine(localRoot, CayrastBrand.DataFolderName);
    }

    /// <inheritdoc />
    public string Roaming { get; }

    /// <inheritdoc />
    public string Local { get; }

    /// <inheritdoc />
    public string Settings => Path.Combine(Roaming, "Settings");

    /// <inheritdoc />
    public string Plugins => Path.Combine(Roaming, "Plugins");

    /// <inheritdoc />
    public string Themes => Path.Combine(Roaming, "Themes");

    /// <inheritdoc />
    public string Commands => Path.Combine(Roaming, "Commands");

    /// <inheritdoc />
    public string Database => Path.Combine(Roaming, "Database");

    /// <inheritdoc />
    /// <remarks>
    /// Local rather than roaming: logs are diagnostic, can reach tens of megabytes,
    /// and are meaningless on a different machine.
    /// </remarks>
    public string Logs => Path.Combine(Local, "Logs");

    /// <inheritdoc />
    /// <remarks>Safe to delete at any time; everything here rebuilds on demand.</remarks>
    public string Cache => Path.Combine(Local, "Cache");

    /// <inheritdoc />
    /// <remarks>
    /// Must be an explicit, writable, per-user path. Left unset, WebView2 defaults to
    /// a folder beside the executable, which fails outright for an all-users install.
    /// </remarks>
    public string WebViewData => Path.Combine(Local, "WebView2");

    /// <inheritdoc />
    /// <remarks>
    /// Takes a parsed <see cref="ModuleId"/> rather than a string precisely so a
    /// malformed id from an untrusted manifest cannot traverse out of the plugins
    /// directory.
    /// </remarks>
    public string ModuleData(ModuleId moduleId) => Path.Combine(Plugins, moduleId.Value, "data");

    /// <inheritdoc />
    /// <remarks>
    /// Called once during startup so later writes can assume their target exists,
    /// rather than every writer defensively creating directories.
    /// </remarks>
    public void EnsureCreated()
    {
        foreach (var directory in new[] { Settings, Plugins, Themes, Commands, Database, Logs, Cache, WebViewData })
        {
            Directory.CreateDirectory(directory);
        }
    }
}
