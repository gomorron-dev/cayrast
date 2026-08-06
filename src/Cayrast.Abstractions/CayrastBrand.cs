namespace Cayrast.Abstractions;

/// <summary>
/// Single source of truth for product identity.
/// </summary>
/// <remarks>
/// The specification requires that Cayrast can be rebranded without invasive code
/// changes. Every user-visible name, folder name, file extension, and protocol
/// host is defined here and nowhere else. If you find a hard-coded "Cayrast"
/// string elsewhere in the codebase, it is a bug.
/// </remarks>
public static class CayrastBrand
{
    /// <summary>Display name shown in the UI, tray, installer, and window titles.</summary>
    public const string ProductName = "Cayrast";

    /// <summary>
    /// Folder name used under <c>%APPDATA%</c> and <c>%LOCALAPPDATA%</c>.
    /// Kept separate from <see cref="ProductName"/> so a rebrand does not orphan
    /// existing user data without an explicit migration step.
    /// </summary>
    public const string DataFolderName = "Cayrast";

    /// <summary>Windows registry key and mutex name root. Must be filesystem- and registry-safe.</summary>
    public const string SlugName = "cayrast";

    /// <summary>Packaged module extension, e.g. <c>Spotify.cayrast</c>.</summary>
    public const string ModulePackageExtension = ".cayrast";

    /// <summary>Installable theme extension, e.g. <c>Midnight.cayrast-theme</c>.</summary>
    public const string ThemePackageExtension = ".cayrast-theme";

    /// <summary>Full settings/data backup extension.</summary>
    public const string BackupExtension = ".cayrast-backup";

    /// <summary>Exported configuration extension.</summary>
    public const string ConfigExtension = ".cayrast-config";

    /// <summary>
    /// Virtual host the shell UI is served from inside WebView2.
    /// </summary>
    /// <remarks>
    /// The frontend is mapped to <c>https://{ShellVirtualHost}/</c> rather than loaded
    /// from <c>file://</c>. This gives the UI a real, stable origin so that normal web
    /// security applies: a meaningful Content-Security-Policy, working service-worker
    /// and storage semantics, and — most importantly — a distinct origin per module so
    /// the browser itself isolates untrusted plugin UIs from the shell.
    /// </remarks>
    public const string ShellVirtualHost = "shell.cayrast.local";

    /// <summary>
    /// Format string for per-module UI origins. Formatted with the module id slug.
    /// </summary>
    /// <remarks>
    /// Each module's frontend gets its own subdomain, e.g.
    /// <c>https://mod-spotify.cayrast.local/</c>, and is embedded in a sandboxed
    /// iframe. Same-origin policy then prevents any module UI from reading the
    /// shell's DOM, its storage, or another module's storage — enforcement we get
    /// from the browser rather than having to build and audit ourselves.
    /// </remarks>
    public const string ModuleVirtualHostFormat = "mod-{0}.cayrast.local";
}
