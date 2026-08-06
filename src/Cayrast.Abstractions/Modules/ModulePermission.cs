namespace Cayrast.Abstractions.Modules;

/// <summary>
/// Capabilities a module must declare in its manifest and the user must grant.
/// </summary>
/// <remarks>
/// <para>
/// These are enforced at the <em>broker</em> inside the host, not by the module.
/// A module never receives a raw <see cref="System.IO.FileStream"/> or
/// <see cref="System.Net.Http.HttpClient"/>; it asks the host to act on its behalf
/// and the host checks the granted set first. That is what makes the permission
/// list meaningful rather than decorative.
/// </para>
/// <para>
/// For untrusted modules the broker check is backed by OS-level isolation: the
/// module runs in a separate low-integrity process, so bypassing the broker by
/// calling Win32 directly fails at the kernel rather than succeeding silently.
/// </para>
/// <para>
/// <b>Known limitation (v1):</b> <see cref="FileSystem"/> is coarse — it grants
/// broker-mediated access to the user profile as a whole. Path-scoped grants
/// ("this module may read %USERPROFILE%\Music only") are planned; the manifest
/// format reserves room for them so adding scoping will not be a breaking change.
/// </para>
/// </remarks>
[Flags]
public enum ModulePermission
{
    /// <summary>No capabilities. A module with this set can still contribute search results and commands.</summary>
    None = 0,

    /// <summary>Read and write files through the host broker.</summary>
    FileSystem = 1 << 0,

    /// <summary>Make outbound network requests through the host broker.</summary>
    Network = 1 << 1,

    /// <summary>Read and write the system clipboard.</summary>
    Clipboard = 1 << 2,

    /// <summary>Capture audio from an input device.</summary>
    Microphone = 1 << 3,

    /// <summary>Change system or per-application audio volume and routing.</summary>
    AudioControl = 1 << 4,

    /// <summary>Enumerate, start, and terminate processes.</summary>
    ProcessManagement = 1 << 5,

    /// <summary>Enumerate, move, resize, and focus windows belonging to other applications.</summary>
    WindowManagement = 1 << 6,

    /// <summary>Capture the screen or a screen region.</summary>
    ScreenCapture = 1 << 7,

    /// <summary>
    /// Execute arbitrary shell commands (PowerShell, CMD, WSL).
    /// </summary>
    /// <remarks>
    /// This is effectively equivalent to full user-level trust: a module that can run
    /// a shell can do anything the user can. The consent UI must present it as such
    /// rather than listing it as one checkbox among many.
    /// </remarks>
    ShellExecute = 1 << 8,

    /// <summary>Post toast notifications.</summary>
    Notifications = 1 << 9,
}
